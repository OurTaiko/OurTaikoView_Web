using System;
using System.Collections;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using State = OurTaiko.SongSelectManager.State;
using ItemKind = OurTaiko.SongSelectManager.ItemKind;
using WheelItem = OurTaiko.SongSelectManager.WheelItem;

namespace OurTaiko
{
    // Nijiiro single-player song select (scenes/song_select.cpp + Scripts/song_select/song_select.lua):
    // the vertical song-board wheel, the course-select panel and the 演奏オプション panel behind the option
    // button. ServerLogin's categories follow the local songs as closed genre folders; a folder opens
    // inline (Navigator::load_current_directory without child folders): its board turns into もどる,
    // its songs follow with a もどる every ten songs, and opening another folder closes it first.
    // A root もどる ends the list (the wheel wraps, so it sits above the first song) and leaves for
    // Entry like the back key.
    // Nested folders, the standalone neiro panel, dan boards and 2P are not ported.
    //
    // This is the view: SongSelectManager holds the wheel, focus, phase, cursor and options and runs
    // every command; this component draws it from the saved layout, animates its events (board
    // slides, select_on/off, genre fades, the course panel, the Ura flip, the option panel) and plays
    // the sounds, voices, BGM and preview.
    public sealed class SongSelectScene : MonoBehaviour
    {
        [Header("Stage")]
        public RectTransform wheel, coursePanel;
        public SongSelectView view;
        public Image[] backgroundTiles; // previous genre x2, current genre x2

        [Header("Wheel art")]
        public Sprite[] genreBackgrounds, boards;
        public Sprite cursorGlow, branch;
        public Sprite[] plates, levels, stars, crownClear, crownFullCombo, crownDonderful;

        [Header("Folder art")]
        [Tooltip("folder_graphic 0..12 (open folder board), indexed like boards.")]
        public Sprite[] folderBoards;
        [Tooltip("box_chara 0..9, left and right 960x480 halves.")]
        public Sprite[] charaLeft, charaRight;
        public Sprite backBoard;

        [Header("Course select art")]
        public Sprite[] backboards, courseBoards, courseMarks, smallCrowns, smallStars;
        public Sprite courseFrame, playerBalloon, backButton, optionButton, buttonGlow, levelBar, levelDot, courseBranch, autoIcon;
        public Texture2D uraChangeToUra, uraChangeToOni;

        [Header("Play options")]
        public OptionPanelArt optionArt;

        [Header("Player")]
        public NameplateView nameplatePrefab;

        [Header("Global chrome")]
        public ArcadeOverlayArt overlay;

        [Header("Timelines")]
        public TextAsset songBoardTimeline, cursorGlowTimeline, uraLoopTimeline, folderBoardTimeline;

        [Header("Audio")]
        public AudioSource bgm, preview, sfx, voice;
        public AudioClip don, ka, uraSwitch, voiceEnter, voiceStartSong;

        const double MoveMs = 166, OpenHoldMs = 61 / 120.0 * 1000, OpenGrowMs = 233, CloseMs = 13 / 0.06, FolderCloseMs = 8 / 0.06;
        const double CourseExitMoveMs = 500, CourseEnterMoveMs = 800, BoardFadeMs = 166;
        const double CourseFadeDelayMs = 400, CourseFadeMs = 483, GenreFadeMs = 200, BackgroundLoopMs = 15000;
        const double UraChangeMs = 1500, UraSwapMs = 450;
        const int UraCells = 90;
        static readonly string[] ChipNames = { "かんたん", "ふつう", "むずかしい", "おに", "おに(裏)" };

        public const int ListTimerSeconds = 100, CourseTimerSeconds = 60;
        public SongSelectManager Manager { get; private set; }
        public ArcadeTimerView TimerView { get; private set; }
        public CoinOverlayView Coins { get; private set; }
        public double CourseFade => Manager.Phase == State.Browsing ? 0 : Clamp01((Now - courseEnteredAt - CourseFadeDelayMs) / CourseFadeMs);
        public bool IsPreviewPlaying => preview != null && preview.IsAudioPlaying();

        sealed class Plate { public Difficulty Difficulty; public CanvasGroup Group; }
        // A board view and its authored sizes/positions before any song changed them.
        sealed class Slot
        {
            public SongBoardView View;
            public Vector2 PanelSize, GlowSize, TitlePosition, CrownPosition, CrownSize, RankPosition, RootOffset;
            public Vector2[] PlateBase;
            public bool Saved;
        }
        // Only boards on screen hold a view (Slot): the authored songs keep their saved scene
        // boards, the rest share pooled SongBoard prefab instances, so a long online catalog costs
        // a dozen board objects instead of one per song.
        sealed class FolderSlot
        {
            public FolderBoardView View;
            public Vector2 PanelSize, GlowSize, TitlePosition, CountPosition;
        }
        // How one wheel item is drawn: its bound view and its slide, select_on/off and fade timing.
        sealed class Board
        {
            public WheelItem Item;
            public ItemKind Kind => Item.Kind;
            public int Folder => Item.Folder;
            public int Genre => Item.Genre;
            public SongDefinition Song => Item.Song;
            public SongInfo Info => Item.Info;
            public FolderSlot FolderSlot;
            public Slot Slot;
            public SongBoardView View;
            public Vector2 PanelSize, GlowSize, TitlePosition, CrownPosition, CrownSize, RankPosition, RootOffset;
            public RectTransform Root;
            public CanvasGroup Group;
            public Image Glow, Panel, Crown;
            public TextMeshProUGUI Title, Subtitle;
            public CanvasGroup Contents;
            public readonly List<Plate> Plates = new List<Plate>();
            public double Position, MoveFrom, MoveTo, MoveStart = -1, MoveDuration;
            public double Cross, CrossFrom, CrossTo;
            public double OpenStart = -1, CloseStart = -1, Hold;
            public double FadeStart = -1, FadeFrom = 1, FadeTo = 1;
        }
        sealed class CourseCard
        {
            public Image Board, Crown, Star, Level, Bar, Branch;
            public ScoreRankView Rank;
            public Image[] Dots;
            public TextMeshProUGUI Name;
        }

        readonly Dictionary<WheelItem, Board> visuals = new Dictionary<WheelItem, Board>();
        readonly Stack<Slot> boardPool = new Stack<Slot>();
        readonly Stack<FolderSlot> folderPool = new Stack<FolderSlot>();
        LumenClip folderClip;
        bool restackBoards;
        readonly CourseCard[] cards = new CourseCard[4];
        LumenClip songBoard, glowClip, uraLoop;
        SceneSwitcher switcher;
        double startedAt, genreChangedAt = -10000, courseEnteredAt, uraChangedAt = -1;
        int previousGenre, currentGenre;
        bool uraChangeToUraSide, previewStarted, started;
        Sprite[] uraToUraCells, uraToOniCells;
        Image mark, backboard, back, option, auto, frame, glow, balloon, uraChange;
        TextMeshProUGUI header, headerSub;
        OptionPanel optionPanel;
        Vector2 framePosition, glowPosition, balloonPosition;
        Vector2[] backgroundPositions;
        Func<bool> courseReady;

        double Now => GameTimeline.FrameTime * 1000 - startedAt;
        static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
        Board BoardOf(WheelItem item) => visuals[item];
        Board FocusedBoard => visuals[Manager.FocusedItem];

        void Awake()
        {
            switcher = SceneSwitcher.EnsureInstance();
            startedAt = GameTimeline.FrameTime * 1000;
            songBoard = LumenClip.Parse(songBoardTimeline.text);
            glowClip = LumenClip.Parse(cursorGlowTimeline.text);
            uraLoop = LumenClip.Parse(uraLoopTimeline.text);
            folderClip = folderBoardTimeline != null ? LumenClip.Parse(folderBoardTimeline.text) : LumenClip.Empty;
            switcher.SceneChanging += OnSceneChanging;
            if (view == null) throw new InvalidOperationException("SongSelect requires a saved layout. Run OurTaiko/Apply Song Select Layout in the Editor.");
            wheel.gameObject.SetActive(true);
            Manager = SongSelectManager.EnsureInstance();
            Manager.Load(optionArt.hitSounds != null ? optionArt.hitSounds.Count : 0);
            Manager.CourseReady = courseReady = () => CourseFade >= 1;
            Manager.SoundRequested += OnSound;
            Manager.VoiceRequested += OnVoice;
            Manager.FolderChanged += OnFolderChanged;
            Manager.WheelRebuilt += OnWheelRebuilt;
            Manager.SearchRequested += OnSearchRequested;
            Manager.FocusChanged += OnFocusChanged;
            Manager.PhaseChanged += OnPhaseChanged;
            Manager.UraToggled += OnUraToggled;
            Manager.OptionsOpened += OnOptionsOpened;
            Manager.OptionChanged += OnOptionChanged;
            Manager.OptionsClosing += OnOptionsClosing;
            BindBoards();
            backgroundPositions = backgroundTiles.Select(tile => tile.rectTransform.anchoredPosition).ToArray();
            currentGenre = previousGenre = Manager.FocusedItem.Genre;
            BindCoursePanel();
            TimerView = new ArcadeTimerView(view.overlays, overlay);
            Coins = new CoinOverlayView(view.overlays, overlay);
            // Slice the 90-cell Oni/Ura change sheets up front so the first flip does not hitch.
            UraFrames(ref uraToUraCells, uraChangeToUra);
            UraFrames(ref uraToOniCells, uraChangeToOni);
            SetPositions(true, 0);
            // SecondLoading: the initially focused board opens at once, without the 508 ms hold.
            OpenFocused(holdMs: 0);
            coursePanel.gameObject.SetActive(false);
            DrawOverlays(0);
            if (view.search != null) view.search.Bind(Manager);
        }

        void Start()
        {
            bgm.SetAudioGroup(AudioGroup.Bgm);
            voice.SetAudioGroup(AudioGroup.Voice);
            sfx.PrepareAudioEffects(don, ka, uraSwitch);
            if (optionArt.hitSounds != null) { sfx.PrepareAudioEffects(optionArt.hitSounds.don); sfx.PrepareAudioEffects(optionArt.hitSounds.ka); }
            voice.PrepareAudioTracks(voiceEnter, voiceStartSong, optionArt.voice);
            bgm.loop = true;
            bgm.PlayAudio();
            PlayVoice(voiceEnter);
            started = true;
        }

        void OnDisable() => StopPreview();

        void OnDestroy()
        {
            StopPreview();
            if (switcher != null) switcher.SceneChanging -= OnSceneChanging;
            if (Manager == null) return;
            Manager.SoundRequested -= OnSound;
            Manager.VoiceRequested -= OnVoice;
            Manager.FolderChanged -= OnFolderChanged;
            Manager.WheelRebuilt -= OnWheelRebuilt;
            Manager.SearchRequested -= OnSearchRequested;
            Manager.FocusChanged -= OnFocusChanged;
            Manager.PhaseChanged -= OnPhaseChanged;
            Manager.UraToggled -= OnUraToggled;
            Manager.OptionsOpened -= OnOptionsOpened;
            Manager.OptionChanged -= OnOptionChanged;
            Manager.OptionsClosing -= OnOptionsClosing;
            // A newer song select may already have bound itself.
            if (Manager.CourseReady == courseReady) Manager.CourseReady = null;
        }

        void OnSceneChanging(string scene)
        {
            StopPreview();
            bgm.StopAudio(); preview.StopAudio();
            Manager.Leave();
        }

        void Update()
        {
            if (!started) return;
            double now = Now;
            if (view.search != null) view.search.Tick();
            if (view.search == null || !view.search.BlocksInput) Manager.HandleInput();
            if (Manager.Phase == State.Decided && !voice.IsAudioPlaying() && !switcher.IsSwitching) Manager.StartSong();
            UpdatePreview(now);
            DrawBackground(now);
            var items = Manager.Items;
            for (int i = 0; i < items.Count; i++) DrawBoard(BoardOf(items[i]), i == Manager.Focused, now);
            if (restackBoards) RestackBoards();
            if (Manager.Phase != State.Browsing) DrawCoursePanel(now);
            if (optionPanel.Draw(now)) Manager.OptionsHidden();
            DrawOverlays(now);
            if (view.bestScore != null)
                view.bestScore.Show(Manager.FocusedSong, Manager.FocusedKind == ItemKind.Song ? Manager.FocusedItem.Info : null,
                    Manager.IsOptionPanelOpen ? 0 : 1, GameTimeline.FrameTime);
        }

        // ---------------------------------------------------------------- manager events

        void OnSound(SongSelectManager.Sound sound)
            => sfx.PlayAudioOneShot(sound == SongSelectManager.Sound.Don ? don : sound == SongSelectManager.Sound.Ka ? ka : uraSwitch);

        void OnVoice(SongSelectManager.Voice clip)
            => PlayVoice(clip == SongSelectManager.Voice.StartSong ? voiceStartSong : optionArt.voice);

        void PlayVoice(AudioClip clip)
        {
            voice.StopAudio();
            voice.clip = clip;
            if (clip != null) voice.PlayAudio();
        }

        // Boards leaving the wheel release their views; new ones start on the anchor's slot, songs
        // fading in as they slide to their rows (もどる and the folder board appear at once).
        void OnFolderChanged(SongSelectManager.FolderChange change)
        {
            var anchor = BoardOf(change.Anchor);
            foreach (var item in change.Removed)
            {
                ReleaseView(BoardOf(item));
                visuals.Remove(item);
            }
            foreach (var item in change.Added)
            {
                double fadeFrom = item.Kind == ItemKind.Song ? 0 : 1;
                visuals[item] = new Board
                {
                    Item = item, Position = anchor.Position, Cross = anchor.Cross,
                    FadeFrom = fadeFrom, FadeTo = 1, FadeStart = fadeFrom < 1 ? Now : -1,
                };
            }
        }

        void OnSearchRequested() => view.search?.Open();

        void OnWheelRebuilt()
        {
            StopPreview();
            foreach (var board in visuals.Values) ReleaseView(board);
            visuals.Clear();
            foreach (var item in Manager.Items) visuals[item] = new Board { Item = item };
            SetPositions(true, 0); OpenFocused(0); ChangeGenre(Manager.FocusedItem.Genre);
            if (view.search != null) view.search.RefreshSummary();
        }

        void OnFocusChanged(WheelItem previous, SongSelectManager.FocusReason reason)
        {
            if (reason == SongSelectManager.FocusReason.Navigate && visuals.TryGetValue(previous, out var left) && left.OpenStart >= 0)
            {
                left.CloseStart = Now; left.OpenStart = -1;
            }
            SetPositions(false, MoveMs);
            // A folder enter or exit snaps the new focus open (no 508 ms hold).
            OpenFocused(reason == SongSelectManager.FocusReason.Navigate ? OpenHoldMs : 0);
            if (reason != SongSelectManager.FocusReason.FolderClosed) ChangeGenre(Manager.FocusedItem.Genre);
            StopPreview();
        }

        void OnPhaseChanged(State previous)
        {
            if (Manager.Phase == State.CourseSelect) EnterCourseSelect();
            else if (Manager.Phase == State.Browsing && previous != State.Browsing) ExitCourseSelect();
        }

        void OnUraToggled()
        {
            uraChangedAt = Now; uraChangeToUraSide = Manager.Cursor.IsUra;
        }

        void OnOptionsOpened(OptionMenu menu) => optionPanel.Open(menu, Now);

        void OnOptionChanged(int direction)
        {
            optionPanel.Changed(direction, Now);
            // step_neiro previews the new set's don; 無音 plays nothing.
            var menu = Manager.OptionMenu;
            if (menu.Current == OptionRow.Neiro && optionArt.hitSounds != null
                && optionArt.hitSounds.TryGet(menu.Options.neiro, out var neiro, out _))
                sfx.PlayAudioOneShot(neiro, AudioGroup.Drum);
        }

        void OnOptionsClosing() => optionPanel.Close(Now);

        // ---------------------------------------------------------------- wheel animation

        void ChangeGenre(int genre)
        {
            previousGenre = currentGenre; currentGenre = genre; genreChangedAt = Now;
        }

        void OpenFocused(double holdMs)
        {
            var board = FocusedBoard;
            board.OpenStart = Now; board.Hold = holdMs; board.CloseStart = -1;
        }

        // Navigator::set_positions (vertical gallery): pitch 135, +-120 around the open board,
        // each row 40 px further right; a jump of a whole screen snaps instead of sliding.
        void SetPositions(bool snap, double duration)
        {
            var items = Manager.Items;
            int count = items.Count, focused = Manager.Focused;
            for (int i = 0; i < count; i++)
            {
                double offset = i - focused;
                if (offset > count / 2.0) offset -= count;
                else if (offset < -count / 2.0) offset += count;
                double position = view.wheelCentre.y + offset * view.rowPitch + Math.Sign(offset) * view.expandGap;
                double cross = view.wheelCentre.x + offset * view.rowCurve;
                var board = BoardOf(items[i]);
                if (snap || Math.Abs(position - board.Position) >= 1080) { board.Position = position; board.Cross = cross; board.MoveStart = -1; }
                else MoveBoard(board, position, cross, duration);
            }
        }

        void MoveBoard(Board board, double position, double cross, double duration)
        {
            board.MoveFrom = board.Position; board.MoveTo = position;
            board.CrossFrom = board.Cross; board.CrossTo = cross;
            board.MoveStart = Now; board.MoveDuration = duration;
        }

        void FadeBoard(Board board, double to)
        {
            board.FadeFrom = BoardFade(board, Now); board.FadeTo = to; board.FadeStart = Now;
        }

        static double BoardFade(Board board, double now)
            => board.FadeStart < 0 ? board.FadeTo : board.FadeFrom + (board.FadeTo - board.FadeFrom) * Clamp01((now - board.FadeStart) / BoardFadeMs);

        // ---------------------------------------------------------------- course select

        void EnterCourseSelect()
        {
            var board = FocusedBoard;
            courseEnteredAt = Now;
            uraChangedAt = -1;
            // Navigator::enter_diff_select: the other on-screen boards leave by 150 px and fade out.
            var items = Manager.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var other = BoardOf(items[i]);
                if (i == Manager.Focused || other.Position < -100 || other.Position > 1180) continue;
                MoveBoard(other, other.Position < view.wheelCentre.y ? -150 : 1080 + 150, other.Cross, CourseEnterMoveMs);
                FadeBoard(other, 0);
            }
            FillCoursePanel(board);
            coursePanel.gameObject.SetActive(true);
        }

        void ExitCourseSelect()
        {
            coursePanel.gameObject.SetActive(false);
            SetPositions(false, CourseExitMoveMs);
            foreach (var board in visuals.Values) FadeBoard(board, 1);
            // Course back replays select_on immediately on the focused board.
            OpenFocused(holdMs: 0);
        }

        // ---------------------------------------------------------------- preview / bgm

        void UpdatePreview(double now)
        {
            var board = FocusedBoard;
            bool open = Manager.Phase == State.Browsing && board.Song != null && board.OpenStart >= 0 && now - board.OpenStart >= board.Hold + OpenGrowMs;
            bool remote = board.Song != null && Online.OnlineManager.Instance?.IsOnline(board.Song) == true;
            if (open && !previewStarted && (remote || board.Song.music != null || !string.IsNullOrEmpty(board.Song.audioPath)))
            {
                previewStarted = true;
                StartCoroutine(LoadPreview(board.Song, board.Info.DemoStart, ++previewGeneration));
            }
        }

        int previewGeneration;
        System.Threading.CancellationTokenSource previewCancellation;
        SongDefinition onlinePreviewSong;
        void ReleaseOnlinePreview()
        {
            if (onlinePreviewSong == null) return;
            if (preview != null) preview.GetComponent<AudioBus>()?.Release();
            if (preview != null) preview.clip = null;
            Destroy(onlinePreviewSong); onlinePreviewSong = null;
        }
        IEnumerator LoadOnlinePreview(SongDefinition song, int generation)
        {
            var online = Online.OnlineManager.Instance;
            var cancellation = previewCancellation = new System.Threading.CancellationTokenSource();
            NativeAudioSample sample = null;
            AudioClip clip = null;
            Task<NativeAudioSample> decode = null;
            bool claimed = false;
            bool Stale() => generation != previewGeneration || cancellation.IsCancellationRequested;
            var task = online.Client.PreparePreviewAsync(online.ChartOf(song), cancellation.Token);
            try
            {
                while (!task.IsCompleted) yield return null;
                if (Stale()) yield break;
                if (!task.IsCompletedSuccessfully)
                {
                    Debug.LogWarning("Online preview: " + task.Exception?.GetBaseException().Message);
                    yield break;
                }
                var engine = AudioEngine.EnsureInstance();
                int audioGeneration = engine.Generation;
                string path = task.Result;
                if (engine.Native)
                {
                    decode = Task.Run(() => new NativeAudioSample(System.IO.File.ReadAllBytes(path), engine, true, false, audioGeneration), cancellation.Token);
                    while (!decode.IsCompleted) yield return null;
                    if (Stale()) yield break;
                    if (!decode.IsCompletedSuccessfully) { Debug.LogWarning("Preview audio decode failed"); yield break; }
                    sample = decode.Result;
                }
                else
                {
                    // The preview API supplies Ogg audio; cache objects have no extension.
                    string url = new Uri(path).AbsoluteUri;
#if UNITY_WEBGL && !UNITY_EDITOR
                    string mime = "audio/ogg";
                    url = "data:" + mime + ";base64," + Convert.ToBase64String(System.IO.File.ReadAllBytes(path));
#endif
                    using var request = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.OGGVORBIS);
                    ((DownloadHandlerAudioClip)request.downloadHandler).streamAudio = false;
                    var operation = request.SendWebRequest();
                    while (!operation.isDone) { if (Stale()) { request.Abort(); yield break; } yield return null; }
                    if (request.result != UnityWebRequest.Result.Success) { Debug.LogWarning("Preview audio decode failed"); yield break; }
                    clip = DownloadHandlerAudioClip.GetContent(request);
                }
                if (Stale() || engine.Generation != audioGeneration) yield break;
                ReleaseOnlinePreview();
                onlinePreviewSong = ScriptableObject.CreateInstance<SongDefinition>();
                onlinePreviewSong.hideFlags = HideFlags.DontSave;
                onlinePreviewSong.music = clip;
                if (sample != null) onlinePreviewSong.SetPreparedAudio(sample);
                claimed = true;
                preview.SetAudioSong(onlinePreviewSong, false);
                preview.SeekAudio(0);
                preview.PlayAudio();
            }
            finally
            {
                cancellation.Cancel();
                if (previewCancellation == cancellation) previewCancellation = null;
                if (!claimed)
                {
                    if (clip != null) Destroy(clip);
                    if (decode != null) _ = decode.ContinueWith(t => { if (t.IsCompletedSuccessfully) t.Result.Dispose(); });
                }
                cancellation.Dispose();
            }
        }
        IEnumerator LoadPreview(SongDefinition song, double demoStart, int generation)
        {
            var engine = AudioEngine.EnsureInstance();
            var online = Online.OnlineManager.Instance;
            if (online?.IsOnline(song) == true)
            {
                yield return LoadOnlinePreview(song, generation);
                yield break;
            }
            if (engine.Native)
            {
                byte[] bytes = null;
                Exception readError = null;
                try { if (string.IsNullOrEmpty(song.audioPath)) bytes = AudioAssetCatalog.Read(song.music); }
                catch (Exception error) { readError = error; }
                if (readError != null) { Debug.LogWarning("Preview audio: " + readError.Message); StopPreview(); yield break; }
                string path = song.audioPath;
                int audioGeneration = engine.Generation;
                var task = Task.Run(() => new NativeAudioSample(bytes ?? System.IO.File.ReadAllBytes(path), engine, true, false, audioGeneration));
                bool claimed = false;
                try
                {
                    while (!task.IsCompleted) yield return null;
                    if (generation != previewGeneration) yield break;
                    if (!task.IsCompletedSuccessfully)
                    {
                        Debug.LogWarning("Preview audio: " + task.Exception?.GetBaseException().Message);
                        StopPreview(); yield break;
                    }
                    song.SetPreparedAudio(task.Result); claimed = true;
                }
                finally
                {
                    if (!claimed) _ = task.ContinueWith(t => { if (t.IsCompletedSuccessfully) t.Result.Dispose(); });
                }
            }
            if (generation != previewGeneration) yield break;
            preview.SetAudioSong(song, false);
            preview.SeekAudio(Math.Clamp(demoStart, 0, Math.Max(0, preview.AudioLength() - 0.1)));
            preview.PlayAudio();
        }

        // Selection changes stop only the preview. The selection BGM keeps playing.
        void StopPreview()
        {
            previewCancellation?.Cancel();
            previewGeneration++;
            ReleaseOnlinePreview();
            if (!previewStarted) return;
            previewStarted = false;
            preview.StopAudio();
        }

        // ---------------------------------------------------------------- drawing

        void DrawBackground(double now)
        {
            // navigator background: 2880-px genre strip, 1920 px per 15 s; the new genre fades in over 200 ms.
            float move = (float)(now % BackgroundLoopMs / BackgroundLoopMs * 1920);
            float change = (float)Clamp01((now - genreChangedAt) / GenreFadeMs);
            for (int i = 0; i < 2; i++)
            {
                backgroundTiles[i].sprite = genreBackgrounds[previousGenre];
                backgroundTiles[i + 2].sprite = genreBackgrounds[currentGenre];
                backgroundTiles[i + 2].Alpha(change);
                backgroundTiles[i].rectTransform.anchoredPosition = backgroundPositions[i] + Vector2.left * move;
                backgroundTiles[i + 2].rectTransform.anchoredPosition = backgroundPositions[i + 2] + Vector2.left * move;
            }
        }

        void DrawBoard(Board board, bool focused, double now)
        {
            if (board.MoveStart >= 0)
            {
                double p = SkinUi.CubicOut((now - board.MoveStart) / board.MoveDuration);
                board.Position = board.MoveFrom + (board.MoveTo - board.MoveFrom) * p;
                board.Cross = board.CrossFrom + (board.CrossTo - board.CrossFrom) * p;
                if (now - board.MoveStart >= board.MoveDuration) board.MoveStart = -1;
            }
            if (board.Kind != ItemKind.Song) { DrawFolderBoard(board, focused, now); return; }
            bool hidden = focused && Manager.Phase != State.Browsing;
            if (hidden || board.Position <= -400 || board.Position >= 1480) { ReleaseView(board); return; }
            AcquireView(board);
            board.Root.gameObject.SetActive(true);
            board.Group.alpha = (float)BoardFade(board, now);
            board.Root.anchoredPosition = new Vector2((float)board.Cross, -(float)board.Position) + board.RootOffset;

            // select_on / select_off of the song board clip (anim/song_board): board centre sy and
            // the song_kanban_info alpha, after the 508 ms pre-growth hold.
            double frame = -1;
            if (focused && board.OpenStart >= 0)
            {
                double t = now - board.OpenStart - board.Hold;
                if (t >= 0) frame = songBoard.Label("select_on").GetValueOrDefault(5) + Math.Min(t, OpenGrowMs) * 0.06;
            }
            else if (board.CloseStart >= 0)
            {
                double e = now - board.CloseStart;
                if (e < CloseMs) frame = songBoard.Label("select_off").GetValueOrDefault(30) + e * 0.06;
                else board.CloseStart = -1;
            }
            double pb = 0, ia = 0;
            if (frame >= 0)
            {
                double sy = songBoard.Get("instance_board_center", frame, "sy", 0.2168);
                pb = Clamp01((sy - 0.2168) / (1 - 0.2168));
                ia = Clamp01(songBoard.Get("song_kanban_info", frame, "a", pb >= 1 ? 1 : 0));
            }
            var expansion = Vector2.up * (float)(board.View.expansionHeight * pb);
            board.Panel.rectTransform.sizeDelta = board.PanelSize + expansion;
            board.Glow.rectTransform.sizeDelta = board.GlowSize + expansion;
            double pulse = glowClip.Get("#12@0", now * 0.06 % Math.Max(1, glowClip.Last - glowClip.First + 1), "a", 1);
            board.Glow.Alpha((float)(pb * pulse));
            board.Title.rectTransform.anchoredPosition = board.TitlePosition + board.View.titleOpenOffset * (float)pb;
            board.Contents.alpha = (float)ia;
            board.Subtitle.Alpha((float)ia);
            DrawPlates(board, now);
            DrawCrown(board, (float)pb, (float)(pb > 0 ? ia : 1));
            DrawScoreRank(board, (float)pb, (float)(pb > 0 ? ia : 1));
        }

        // draw_folder_board / draw_back_board: the genre folder grows into folder_graphic with its two
        // characters and song count (anim/folder_board, labels select_on 5 / select_off 30); もどる
        // only grows and glows.
        void DrawFolderBoard(Board board, bool focused, double now)
        {
            if (board.Position <= -400 || board.Position >= 1480) { ReleaseView(board); return; }
            AcquireView(board);
            var slot = board.FolderSlot;
            var item = slot.View;
            board.Root.gameObject.SetActive(true);
            board.Group.alpha = (float)BoardFade(board, now);
            board.Root.anchoredPosition = new Vector2((float)board.Cross, -(float)board.Position) + board.RootOffset;
            double frame = -1;
            if (focused && board.OpenStart >= 0)
            {
                double t = now - board.OpenStart - board.Hold;
                if (t >= 0) frame = folderClip.Label("select_on").GetValueOrDefault(5) + Math.Min(t, OpenGrowMs) * 0.06;
            }
            else if (board.CloseStart >= 0)
            {
                double e = now - board.CloseStart;
                if (e < FolderCloseMs) frame = folderClip.Label("select_off").GetValueOrDefault(30) + e * 0.06;
                else board.CloseStart = -1;
            }
            double pb = 0, chAlpha = 0, chOffset = 340, introAlpha = 0;
            if (frame >= 0)
            {
                pb = Clamp01((folderClip.Get("instance_board_center", frame, "sy", 0.2168) - 0.2168) / (1 - 0.2168));
                chAlpha = Clamp01(folderClip.Get("instance_character_l", frame, "a", 0));
                chOffset = -folderClip.Get("instance_character_l", frame, "tx", -340);
                introAlpha = Clamp01(folderClip.Get("text_kanban_intro_genre", frame, "a", 0));
            }
            var expansion = Vector2.up * (float)(item.expansionHeight * pb);
            item.panelOpen.rectTransform.sizeDelta = item.panelClosed.rectTransform.sizeDelta = slot.PanelSize + expansion;
            item.glow.rectTransform.sizeDelta = slot.GlowSize + expansion;
            double pulse = glowClip.Get("#12@0", now * 0.06 % Math.Max(1, glowClip.Last - glowClip.First + 1), "a", 1);
            item.glow.Alpha((float)(pb * pulse));
            if (board.Kind == ItemKind.Back)
            {
                item.title.rectTransform.anchoredPosition = slot.TitlePosition;
                return;
            }
            // Closed: bar_genre; growing: folder_graphic with the closed look fading out over it.
            item.panelOpen.Alpha(pb > 0 ? 1 : 0);
            item.panelClosed.Alpha((float)(1 - pb));
            float slide = (float)(450 - chOffset);
            item.charaLeft.Alpha((float)chAlpha);
            item.charaRight.Alpha((float)chAlpha);
            item.charaLeft.rectTransform.anchoredPosition = new Vector2(-480 + slide, item.charaLeft.rectTransform.anchoredPosition.y);
            item.charaRight.rectTransform.anchoredPosition = new Vector2(480 - slide, item.charaRight.rectTransform.anchoredPosition.y);
            item.title.rectTransform.anchoredPosition = slot.TitlePosition + item.titleOpenOffset * (float)pb;
            item.count.rectTransform.anchoredPosition = slot.CountPosition + item.countOpenOffset * (float)pb;
            item.count.Alpha((float)introAlpha);
        }

        void DrawPlates(Board board, double now)
        {
            if (board.Contents.alpha <= 0) return;
            // song_kanban_info 'ura': a 180-frame loop cross-fading the oni and ura chips.
            double f = uraLoop.First + now * 0.06 % Math.Max(1, uraLoop.Last - uraLoop.First + 1);
            float ura = (float)uraLoop.Get("ura/text_course_ura", f, "a", 0);
            float oni = (float)uraLoop.Get("oni/text_course_oni", f, "a", 1);
            bool both = board.Info.Has(Difficulty.Oni) && board.Info.Has(Difficulty.Ura);
            foreach (var plate in board.Plates)
                plate.Group.alpha = !both ? 1 : plate.Difficulty == Difficulty.Ura ? ura : plate.Difficulty == Difficulty.Oni ? oni : 1;
        }

        // SetCrown / SearchAnyCrown: the highest crowned course; 72 px at (-425,-114) open,
        // 0.75 scale at (-425,-27) closed.
        void DrawCrown(Board board, float p, float fade)
        {
            Crown best = Crown.None; Difficulty course = Difficulty.Easy;
            foreach (var info in board.Info.Courses)
            {
                var record = SongScores.Get(board.Song, info.Difficulty);
                if (record != null && record.crown != Crown.None) { best = record.crown; course = info.Difficulty; }
            }
            if (best == Crown.None) { board.Crown.enabled = false; return; }
            var set = best == Crown.DonderfulCombo ? crownDonderful : best == Crown.FullCombo ? crownFullCombo : crownClear;
            board.Crown.sprite = set[(int)course];
            board.Crown.rectTransform.sizeDelta = board.CrownSize * (0.75f + 0.25f * p);
            board.Crown.rectTransform.anchoredPosition = board.CrownPosition + board.View.crownOpenOffset * p;
            board.Crown.Alpha(fade);
        }

        void DrawCoursePanel(double now)
        {
            float fade = (float)CourseFade;
            var group = coursePanel.GetComponent<CanvasGroup>();
            group.alpha = fade;
            var board = FocusedBoard;
            var cursor = Manager.Cursor;
            var selected = cursor.Selected;
            int column = selected >= Difficulty.Easy ? Math.Min((int)Difficulty.Oni, (int)selected) : -1;
            mark.enabled = column >= 0;
            if (column >= 0) mark.sprite = courseMarks[(int)selected];

            double ura = uraChangedAt < 0 ? -1 : now - uraChangedAt;
            if (ura >= UraChangeMs) { uraChangedAt = -1; ura = -1; }
            bool oniShowsUra = cursor.IsUra;
            if (ura >= 0 && ura < UraSwapMs) oniShowsUra = !oniShowsUra;
            for (int i = 0; i < 4; i++)
                FillCard(cards[i], board, i == 3 && oniShowsUra ? Difficulty.Ura : (Difficulty)i, i == 3 && ura >= 0);
            uraChange.enabled = ura >= 0;
            if (ura >= 0)
            {
                var cells = uraChangeToUraSide ? UraFrames(ref uraToUraCells, uraChangeToUra) : UraFrames(ref uraToOniCells, uraChangeToOni);
                uraChange.sprite = cells[Math.Min(UraCells - 1, (int)(ura / UraChangeMs * UraCells))];
            }
            auto.enabled = Manager.AutoPlay;

            // draw_selector: the course frame / button glow under the boards, the 1P bubble above.
            float x = column >= 0 ? cards[column].Board.rectTransform.anchoredPosition.x
                : (selected == Difficulty.Modifier ? option : back).rectTransform.anchoredPosition.x;
            bool hideForUra = column == 3 && ura >= 0;
            frame.enabled = column >= 0 && !hideForUra;
            glow.enabled = column < 0;
            balloon.enabled = !hideForUra;
            float cardX = cards[0].Board.rectTransform.anchoredPosition.x;
            frame.rectTransform.anchoredPosition = framePosition + Vector2.right * (x - cardX);
            glow.rectTransform.anchoredPosition = glowPosition + Vector2.right * (x - back.rectTransform.anchoredPosition.x);
            balloon.rectTransform.anchoredPosition = balloonPosition + Vector2.right * (x - cardX);
        }

        Sprite[] UraFrames(ref Sprite[] cells, Texture2D sheet)
        {
            if (cells != null) return cells;
            cells = new Sprite[UraCells];
            for (int i = 0; i < UraCells; i++)
            {
                var rect = new Rect(i % 10 * 340, sheet.height - (i / 10 + 1) * 400, 340, 400);
                cells[i] = Sprite.Create(sheet, rect, new Vector2(0.5f, 0.5f), 100);
            }
            return cells;
        }

        void FillCoursePanel(Board board)
        {
            backboard.sprite = backboards[board.Genre];
            header.text = board.Info.Title;
            header.Squeeze(1000);
            headerSub.text = board.Info.Subtitle;
            headerSub.Squeeze(1000);
        }

        void DrawScoreRank(Board board, float p, float fade)
        {
            var view = board.View.scoreRank;
            if (view == null) return;
            int rank = 0;
            Difficulty course = Difficulty.Easy;
            // SearchAnyCrown walks upward in difficulty; rank and crown are independent.
            foreach (var info in board.Info.Courses)
            {
                int candidate = ScoreRank.FromScore(SongScores.Get(board.Song, info.Difficulty)?.score ?? 0);
                if (candidate > 0 && (rank == 0 || info.Difficulty > course)) { rank = candidate; course = info.Difficulty; }
            }
            // While selecting a course, prefer its record if it has an earned rank.
            if (board.Song == Manager.FocusedSong && Manager.Phase != State.Browsing && Manager.Cursor != null)
            {
                var selected = Manager.Cursor.Selected;
                if ((int)selected >= 0 && (int)selected <= 4 && board.Info.Has(selected))
                {
                    int candidate = ScoreRank.FromScore(SongScores.Get(board.Song, selected)?.score ?? 0);
                    if (candidate > 0) { rank = candidate; course = selected; }
                }
            }
            view.Show(rank, course);
            view.group.alpha = fade;
            view.transform.localScale = Vector3.one * (0.75f + 0.25f * p);
            ((RectTransform)view.transform).anchoredPosition = board.RankPosition + board.View.rankOpenOffset * p;
        }

        void FillCard(CourseCard card, Board board, Difficulty difficulty, bool changing)
        {
            var info = board.Info.Course(difficulty);
            bool has = info != null;
            card.Board.sprite = courseBoards[(int)difficulty];
            card.Board.color = new Color(1, 1, 1, has ? 1 : 0.4f);
            bool details = has && !changing;
            card.Name.enabled = has;
            card.Name.text = ChipNames[(int)difficulty];
            foreach (var image in new[] { card.Crown, card.Star, card.Level, card.Bar }) image.enabled = details;
            card.Branch.enabled = details && info.IsBranching;
            for (int k = 0; k < card.Dots.Length; k++) card.Dots[k].enabled = details && k < Math.Min(10, info.Level);
            if (!details) { card.Rank?.Show(0); return; }
            var record = SongScores.Get(board.Song, difficulty);
            card.Rank?.Show(ScoreRank.FromScore(record?.score ?? 0), difficulty);
            card.Crown.sprite = smallCrowns[(int)(record?.crown ?? Crown.None)];
            card.Level.sprite = smallStars[Mathf.Clamp(info.Level, 1, 11)];
        }

        // ---------------------------------------------------------------- saved view binding

        // Every wheel item gets a board; the authored boards stay bound to the first local songs and
        // later songs borrow pooled prefabs on screen.
        void BindBoards()
        {
            var saved = view.songBoards ?? Array.Empty<SongBoardView>();
            int next = 0;
            foreach (var item in Manager.Items)
            {
                var board = new Board { Item = item };
                visuals[item] = board;
                if (item.Kind == ItemKind.Song && item.Folder < 0 && next < saved.Length) Bind(board, NewSlot(saved[next++], true));
            }
            for (int i = 0; i < saved.Length; i++) saved[i].gameObject.SetActive(false);
        }

        // Later songs draw over earlier ones, as when every song had its own board: the bound views
        // trade their sibling slots so they follow song order (other wheel children keep theirs).
        void RestackBoards()
        {
            restackBoards = false;
            var bound = Manager.Items.Select(BoardOf).Where(b => b.Slot != null || b.FolderSlot != null).ToList();
            var indices = bound.Select(b => b.Root.GetSiblingIndex()).OrderBy(i => i).ToList();
            for (int i = 0; i < bound.Count; i++) bound[i].Root.SetSiblingIndex(indices[i]);
        }

        static Slot NewSlot(SongBoardView item, bool saved) => new Slot
        {
            View = item, Saved = saved,
            PanelSize = item.panel.rectTransform.sizeDelta, GlowSize = item.glow.rectTransform.sizeDelta,
            TitlePosition = item.title.rectTransform.anchoredPosition,
            CrownPosition = item.crown.rectTransform.anchoredPosition, CrownSize = item.crown.rectTransform.sizeDelta,
            RankPosition = item.scoreRank != null ? ((RectTransform)item.scoreRank.transform).anchoredPosition : Vector2.zero,
            RootOffset = saved ? item.Root.anchoredPosition - item.authoredWheelPosition : Vector2.zero,
            PlateBase = item.plates.Select(plate => ((RectTransform)plate.group.transform).anchoredPosition).ToArray(),
        };

        void AcquireView(Board board)
        {
            if (board.Kind != ItemKind.Song)
            {
                if (board.FolderSlot != null) return;
                FolderSlot folderSlot;
                if (folderPool.Count > 0) folderSlot = folderPool.Pop();
                else
                {
                    var item = Instantiate(view.folderPrefab, wheel);
                    folderSlot = new FolderSlot
                    {
                        View = item, PanelSize = item.panelClosed.rectTransform.sizeDelta, GlowSize = item.glow.rectTransform.sizeDelta,
                        TitlePosition = item.title.rectTransform.anchoredPosition, CountPosition = item.count.rectTransform.anchoredPosition,
                    };
                }
                BindFolder(board, folderSlot);
                return;
            }
            if (board.Slot != null) return;
            var slot = boardPool.Count > 0 ? boardPool.Pop() : NewSlot(Instantiate(view.boardPrefab, wheel), false);
            Bind(board, slot);
        }

        // Saved boards stay bound to their song; pooled ones go back for the next board on screen.
        void ReleaseView(Board board)
        {
            if (board.FolderSlot != null)
            {
                board.Root.gameObject.SetActive(false);
                folderPool.Push(board.FolderSlot);
                board.FolderSlot = null; board.Root = null; board.Group = null;
                return;
            }
            if (board.Slot == null) return;
            board.Root.gameObject.SetActive(false);
            if (board.Slot.Saved) return;
            boardPool.Push(board.Slot);
            board.Slot = null; board.View = null;
        }

        // A folder board shows its category and server; a もどる board only its label.
        void BindFolder(Board board, FolderSlot slot)
        {
            var item = slot.View;
            restackBoards = true;
            board.FolderSlot = slot; board.Root = item.Root; board.Group = item.group; board.RootOffset = Vector2.zero;
            item.click.Clicked = () => Manager.SelectItem(board.Item);
            bool back = board.Kind == ItemKind.Back, search = board.Kind == ItemKind.Search;
            var folder = board.Folder >= 0 ? Manager.Folders[board.Folder] : null;
            item.panelClosed.sprite = back ? backBoard : boards[board.Genre];
            item.panelClosed.Alpha(1);
            item.panelOpen.enabled = false;
            item.charaLeft.enabled = item.charaRight.enabled = false;
            item.title.text = back ? SongSelectManager.BackLabel : search ? Manager.SearchFolderTitle : folder.Title;
            item.title.Squeeze(860);
            item.count.text = back ? "" : search ? Manager.SearchFolderDescription : $"{folder.Songs.Length} songs　{folder.ServerName}";
            if (!back)
            {
                item.panelOpen.sprite = folderBoards[board.Genre];
                int chara = Mathf.Clamp(board.Genre, 0, charaLeft.Length - 1);
                item.charaLeft.sprite = charaLeft[chara];
                item.charaRight.sprite = charaRight[chara];
            }
            item.count.Squeeze(860);
            item.count.enabled = false;
        }

        void Bind(Board board, Slot slot)
        {
            var item = slot.View;
            restackBoards = true;
            board.Slot = slot; board.View = item; board.Root = item.Root;
            board.Group = item.group; board.Glow = item.glow; board.Panel = item.panel; board.Crown = item.crown;
            board.Title = item.title; board.Subtitle = item.subtitle; board.Contents = item.contents;
            board.PanelSize = slot.PanelSize; board.GlowSize = slot.GlowSize; board.TitlePosition = slot.TitlePosition;
            board.RankPosition = slot.RankPosition; board.CrownPosition = slot.CrownPosition; board.CrownSize = slot.CrownSize; board.RootOffset = slot.RootOffset;
            item.click.Clicked = () => Manager.SelectItem(board.Item);
            board.Panel.sprite = boards[board.Genre];
            board.Title.text = board.Info.Title;
            board.Title.Squeeze(860);
            board.Subtitle.text = board.Info.Subtitle;
            board.Subtitle.Squeeze(860, 0.75f);
            board.Plates.Clear();
            for (int i = 0; i < item.plates.Length; i++)
            {
                var plate = item.plates[i];
                var info = board.Info.Course(plate.difficulty);
                plate.group.gameObject.SetActive(info != null);
                if (info == null) continue;
                ((RectTransform)plate.group.transform).anchoredPosition = slot.PlateBase[i] + Vector2.right *
                    (PlatePosition(board.Info, plate.difficulty, item.platePitch) - plate.authoredContentX);
                plate.level.sprite = levels[(int)plate.difficulty * 11 + Mathf.Clamp(info.Level, 1, 11) - 1];
                plate.branch.enabled = info.IsBranching;
                board.Plates.Add(new Plate { Difficulty = plate.difficulty, Group = plate.group });
            }
        }

        static float PlatePosition(SongInfo info, Difficulty difficulty, float pitch)
        {
            var columns = new List<Difficulty>();
            for (var d = Difficulty.Easy; d <= Difficulty.Hard; d++)
                if (info.Has(d)) columns.Add(d);
            if (info.Has(Difficulty.Oni) || info.Has(Difficulty.Ura)) columns.Add(Difficulty.Oni);
            int index = columns.IndexOf(difficulty == Difficulty.Ura ? Difficulty.Oni : difficulty);
            return (index - (columns.Count - 1) / 2f) * pitch;
        }

        void BindCoursePanel()
        {
            mark = view.mark; backboard = view.backboard; back = view.back; option = view.option;
            auto = view.auto; frame = view.frame; glow = view.glow; balloon = view.balloon; uraChange = view.uraChange;
            header = view.header; headerSub = view.headerSub;
            framePosition = frame.rectTransform.anchoredPosition;
            glowPosition = glow.rectTransform.anchoredPosition;
            balloonPosition = balloon.rectTransform.anchoredPosition;
            AddClick(back, Difficulty.Back);
            AddClick(option, Difficulty.Modifier);
            for (int i = 0; i < cards.Length; i++)
            {
                var saved = view.cards[i];
                cards[i] = new CourseCard { Rank = saved.scoreRank, Board = saved.board, Crown = saved.crown, Star = saved.star,
                    Level = saved.level, Bar = saved.bar, Branch = saved.branch, Dots = saved.dots, Name = saved.name };
                AddClick(saved.board, (Difficulty)i);
            }
            optionPanel = new OptionPanel(view.options, optionArt);
            optionPanel.RowTapped += Manager.TapOptionRow;
            optionPanel.OutsideTapped += Manager.CloseOptions;
        }

        void DrawOverlays(double now)
        {
            if (TimerView == null) return;
            TimerView.Show(Manager.Phase == State.Browsing ? ListTimerSeconds : CourseTimerSeconds);
            // coin_overlay: the invite shows while a 2P join would still be allowed (songs played < 2).
            Coins.ShowInvite(switcher.SongsPlayed < 2, now);
        }

        void AddClick(Image image, Difficulty difficulty)
        {
            image.raycastTarget = true;
            var pointer = image.GetComponent<PointerRelay>();
            if (difficulty == Difficulty.Oni)
            {
                pointer.CanLongPress = Manager.CanLongPressOni;
                pointer.LongPressed = Manager.LongPressOni;
            }
            pointer.Clicked = () => Manager.SelectCourse(difficulty);
        }
    }

}
