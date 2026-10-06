using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace OurTaiko
{
    public sealed partial class PlayScene : MonoBehaviour
    {
        public SongDefinition defaultSong;
        public AudioSource music, hitAudio;
        public AudioClip don, ka, balloonPop;
        public HitSoundLibrary hitSounds;
        public ModifierBadgeView modifierBadges;
        public RectTransform noteLayer, barLayer, mojiLayer;
        public Sprite[] noteSprites;
        public Sprite[] rollBodySprites, rollTailSprites;
        public Sprite balloonTailSprite;
        public Sprite[] mojiSprites;
        public Sprite mojiRollSprite;
        public BalloonCounterView balloonCounter;
        public Sprite[] judgmentSprites;
        public UnityEngine.UI.Image judgment;
        public HitFaceView hitFace;
        public HitRingView hitRing;
        public SoulGaugeView soulGauge;
        public NoteArcView noteArcs;
        [Tooltip("drum_don_l/r, drum_kat_l/r; each plays DrumFlash.anim from its own hit.")]
        public UnityEngine.UI.Image[] drumFlashes;
        public ScoreCounterView scoreCounter;
        public ComboView combo;
        public ComboAnnounceView comboAnnounce;
        public JudgeCounterView judgeCounter;
        [Tooltip("The touch drum; Settings > Play > Enable Drumpad for Single Player Mode turns it on or off.")]
        public DrumPad drumPad;
        public TMP_Text title, subtitle, resultText;
        public BranchLaneView branchLane;
        public GameObject pausePanel, resultPanel;
        public PauseMenuView pauseMenu;
        public UnityEngine.UI.Button pauseButton, restartButton, backButton, resumeButton, resultRestart, resultBack;
        [Tooltip("Dancer.anim on each dancer: the 0_loop frames at 8 fps on the song clock.")]
        public ClipSampler[] dancers;
        public CanvasGroup gogoTint;

        public PlaySession Session { get; private set; }
        public bool IsPaused { get; private set; }
        public bool IsFinished { get; private set; }
        public PlayResult Result { get; private set; }
        // Every judged drum press of this play, sent with an online score (scoreReplayVersion 1).
        public Online.PlayRecord Record { get; private set; }
        readonly SongClock songClock = new SongClock();
        public double SongTime => songClock.Time;
        public double RenderedTime { get; private set; }
        // Views come from pools while a note is on the lane; null when it is not drawn.
        public RectTransform NoteRoot(int index) => shownNotes[index]?.Root;
        public RectTransform MojiRoot(int index) => shownMoji[index]?.Root;
        public RectTransform BarRoot(int index) => shownBars[index];
        SongDefinition song;
        double audioOffset, visualOffset, judgeOffset;
        double ChartTime => SongTime - audioOffset;
        SongInfo displayInfo;
        bool autoPlay, hitKa;
        SceneSwitcher switcher;
        double feedbackTime = -10;
        readonly double[] flashedAt = { -10, -10, -10, -10 };
        ClipSampler[] flashClips;
        ClipSampler judgmentFade, gogoPulse;
        NoteView[] shownNotes = new NoteView[0];
        MojiView[] shownMoji = new MojiView[0];
        RectTransform[] shownBars = new RectTransform[0];
        readonly Stack<NoteView>[] notePool = { new Stack<NoteView>(), new Stack<NoteView>(), new Stack<NoteView>() };
        readonly Stack<MojiView>[] mojiPool = { new Stack<MojiView>(), new Stack<MojiView>() };
        readonly Stack<RectTransform> barPool = new Stack<RectTransform>();
        // Candidates for drawing: notes (with their text) and bar lines whose precomputed lane
        // interval holds the render time. Built for the current lane width.
        LaneWindow noteWindow, barWindow;
        float windowWidth = float.NaN;
        readonly List<int> leftLane = new List<int>(), stackOrder = new List<int>();
        // What the last RenderNotes drew from; an identical frame (a still pause) is skipped.
        PlaySession renderedSession;
        int renderedVersion, renderedBalloon;
        bool renderedPreview, rendered;
        double dancerTime = double.NaN;
        readonly List<DrumPad> pausedPads = new List<DrumPad>();
        bool closingPauseMenu;
        bool resuming, resumeLostFocus;
        int resumeFrame = -1;
        int lastCombo;
        int pauseOpenedFrame = -1;

        // Pool keys: the child objects a note or its text needs.
        const int PlainShape = 0, BalloonShape = 1, RollShape = 2;
        sealed class NoteView
        {
            public int Shape;
            public RectTransform Root, Body, Tail;
            public UnityEngine.UI.Image Head, BodyImage, TailImage, BalloonTail;
            public float TailAspect;
        }
        sealed class MojiView
        {
            public int Shape;
            public RectTransform Root, Mid, Tail;
            public UnityEngine.UI.Image Head;
            public float MidAspect;
        }

        IEnumerator Start()
        {
            while (WebPlayerBridge.Instance == null || WebPlayerBridge.Instance.Song == null) yield return null;
            switcher = SceneSwitcher.EnsureInstance();
            // Direct Editor runs must return to the same mode after Back and another song.
            switcher.PracticeMode = IsPractice;
            switcher.SceneChanging += PrepareToLeave;
            song = switcher.SelectedSong != null ? switcher.SelectedSong : defaultSong;
            autoPlay = switcher.AutoPlay;
            var playSettings = SettingManager.EnsureInstance().Settings.play;
            audioOffset = (song.audioOffsetMs + (double)playSettings.audioOffsetMs) / 1000.0;
            visualOffset = song.visualOffsetMs / 1000.0;
            judgeOffset = autoPlay ? 0 : playSettings.judgeOffsetMs / 1000.0;
            Record = new Online.PlayRecord
            {
                // Replay v1 stores already corrected judgment time. Keep its two-offset convention:
                // visual time = recorded judgment time - VisualOffsetMs (B cancels here).
                AudioOffsetMs = (int)Math.Round((audioOffset + judgeOffset) * 1000),
                VisualOffsetMs = (int)Math.Round((visualOffset - judgeOffset) * 1000),
            };
            pauseButton.onClick.AddListener(TogglePause);
            resumeButton.onClick.AddListener(Resume);
            restartButton.onClick.AddListener(Restart);
            backButton.onClick.AddListener(Back);
            resultRestart.onClick.AddListener(Restart);
            resultBack.onClick.AddListener(Back);
            pausePanel.SetActive(false); resultPanel.SetActive(false); combo.gameObject.SetActive(false); comboAnnounce.Hide();
            // Disabled and hidden together: an inactive pad neither draws nor registers with InputManager,
            // and pause/resume only re-enables the pads it disabled itself.
            if (drumPad != null) drumPad.gameObject.SetActive(SettingManager.EnsureInstance().Settings.play.singlePlayerDrumPad);
            try
            {
                // SongLoadingScene parsed the chart behind the curtain; restarts and direct runs parse here.
                string course = switcher.SelectedSong != null ? switcher.SelectedCourse : null;
                var options = PlayOptions.Shared;
                var chart = switcher.TakePreparedChart(song, course) ?? PrepareChart(song, course);
                // Practice plays a fixed route, chosen only in its menu; a normal play evaluates branches.
                practiceBranch = BranchRoute.Normal;
                Session = new PlaySession(chart, judgeOffset, IsPractice ? practiceBranch : (BranchRoute?)null);
                if (modifierBadges != null) modifierBadges.Show(options, autoPlay);
                // 音色: hit_sounds/<neiro>/don.ogg and ka.ogg; 無音 leaves both empty.
                if (hitSounds != null) hitSounds.TryGet(options.neiro, out don, out ka);
                balloonCounter.ResetDisplay();
                soulGauge.Initialize(Session.ClearThreshold);
                foreach (string warning in Session.Chart.Warnings) Debug.LogWarning("Ignored TJA command: " + warning);
                Session.Judged += OnJudged;
                Session.BranchSelected += OnBranchSelected;
                if (branchLane != null) branchLane.Initialize(Session.Chart.Branches.Count > 0);
                displayInfo = song.ReadDisplayInfo();
                title.text = displayInfo.Title;
                subtitle.text = $"{displayInfo.Subtitle}    {Session.Chart.Course.ToUpperInvariant()}  LV.{Session.Chart.Level}";
                CreateNotes();
                music.SetAudioSong(song);
                hitAudio.PrepareAudioEffects(don, ka, balloonPop);
                hitAudio.PrepareAudioEffects(comboAnnounce.voices);
                UpdateHud();
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                IsFinished = true; resultPanel.SetActive(true);
                resultText.text = "CHART COULD NOT LOAD\n<size=22>" + error.Message + "</size>";
            }
            if (IsFinished) yield break;
            while (switcher.IsInputBlocked) yield return null;
            // The full countdown starts only after the global cover has opened.
            songClock.Start(GameTimeline.AudioNow, Math.Max(2, Session.Chart.Offset + 2 - audioOffset - Math.Min(0, judgeOffset)));
            if (IsPractice) InitializePractice();
            else ScheduleMusic();
            WebPlayerBridge.Instance.Attach(this);
        }

        // Player::reset_chart: the play options change the chart before load times are taken.
        public static TaikoChart PrepareChart(SongDefinition song, string course)
        {
            var chart = song.Parse(course);
            ChartModifiers.Apply(chart, PlayOptions.Shared, new System.Random());
            return chart;
        }

        void ScheduleMusic()
        {
            music.StopAudio();
            var schedule = songClock.Schedule(GameTimeline.AudioNow, music.AudioLength());
            if (!schedule.HasValue) return;
            music.SeekAudio(schedule.Value.Position);
            music.PlayAudioScheduled(schedule.Value.At);
        }

        void Update()
        {
            if (switcher == null || switcher.IsInputBlocked || !songClock.Started) return;
            // Keep the intentional frame-based judgment; input event timestamps only sort hits.
            double? playback = AudioEngine.EnsureInstance().Backend == AudioBackend.Bass && music.IsAudioPlaying()
                ? music.AudioPosition() : null;
            songClock.Update(GameTimeline.AudioFrameTime, playback);
            if (closingPauseMenu || Time.frameCount == resumeFrame || Time.frameCount == pauseOpenedFrame) return;
            if (InputManager.GetKeyDown(InputKey.Back) || InputManager.GetKeyDown(InputKey.Pause))
            { TogglePause(); return; }
            if (!IsPractice && InputManager.GetKeyDown(InputKey.Restart)) { Restart(); return; }
            if (IsPaused)
            {
                if (IsPractice && !pausePanel.activeSelf) UpdatePracticePause();
                else pauseMenu.HandleInput();
                return;
            }
            if (Session == null || IsFinished) return;
            double time = ChartTime;
            Session.Advance(time, autoPlay);
            if (branchLane != null) branchLane.ShowTime(time);
            if (!autoPlay) HitFirstDrumPress();
            balloonCounter.ShowTime(time);
            RenderNotes(time - visualOffset);
            soulGauge.ShowTime(time);
            if (noteArcs != null) noteArcs.ShowTime(time);
            SampleDancers(time);
            combo.ShowTime(time);
            comboAnnounce.ShowTime(time);
            judgmentFade ??= judgment.GetComponent<ClipSampler>();
            judgmentFade.Sample(Math.Min(GameTimeline.FrameTime - feedbackTime, judgmentFade.clip.length));
            hitFace.ShowTime(time);
            hitRing.ShowTime(time);
            for (int i = 0; i < drumFlashes.Length; i++) ShowFlash(i);
            if (time > Session.Chart.Duration + Math.Max(0, judgeOffset) + 1 && SongTime > music.AudioLength() + 1) Finish();
        }

        // Input mutex: a frame judges only its earliest drum press; later ones in the same frame are dropped.
        void HitFirstDrumPress()
        {
            foreach (var press in InputManager.PressesThisFrame)
            {
                if (!press.Key.IsDrum()) continue;
                Hit(press.Key.IsKa(), press.Key.IsRight());
                return;
            }
        }
        public void Hit(bool isKa, bool right)
        {
            if (Session == null || !songClock.Started || switcher.IsInputBlocked || IsPaused || IsFinished || autoPlay) return;
            Feedback(isKa, right);
            hitKa = isKa;
            double time = ChartTime;
            Record.Inputs.Add(((time - judgeOffset) * 1000, Online.PlayRecord.TypeOf(isKa, right)));
            Session.Hit(isKa, time);
        }
        void Feedback(bool isKa, bool right)
        {
            var clip = isKa ? ka : don;
            if (clip != null) hitAudio.PlayAudioOneShot(clip, AudioGroup.Drum);
            int flash = (isKa ? 2 : 0) + (right ? 1 : 0);
            flashedAt[flash] = GameTimeline.FrameTime;
            ShowFlash(flash);
        }
        // Real time since that drum's last hit; the clip ends switched off.
        void ShowFlash(int index)
        {
            flashClips ??= Array.ConvertAll(drumFlashes, flash => flash.GetComponent<ClipSampler>());
            var sampler = flashClips[index];
            sampler.Sample(Math.Min(GameTimeline.FrameTime - flashedAt[index], sampler.clip.length));
        }
        void OnJudged(int index, Judgment result)
        {
            if (result != Judgment.Roll)
                soulGauge.SetPoints(Session.GaugePoints, ChartTime);
            if (autoPlay) Feedback(Session.Chart.Notes[index].IsKa, (index & 1) != 0);
            SpawnArc(index, result);
            var judged = Session.Chart.Notes[index];
            bool big = judged.Kind == NoteKind.BigDon || judged.Kind == NoteKind.BigKa;
            double judgedAt = ChartTime;
            hitFace.Play(result, big, judgedAt);
            hitRing.Play(result, big, judgedAt);
            if (result != Judgment.Roll)
            {
                // Long-note hits must not restart the previous normal judgment's text fade.
                feedbackTime = GameTimeline.FrameTime;
                judgment.sprite = judgmentSprites[(int)result - 1];
            }
            else if (Session.Chart.Notes[index].Kind == NoteKind.Balloon)
            {
                var note = Session.Chart.Notes[index];
                balloonCounter.RecordHit(index, note.BalloonHits, Session.LongHits[index], note.EndTime + judgeOffset,
                    ChartTime);
                if (Session.LongHits[index] == note.BalloonHits) hitAudio.PlayAudioOneShot(balloonPop);
            }
            UpdateHud();
        }
        // note_correct sends good/ok notes 1-4 and a popped balloon; check_drumroll sends one small
        // note per roll hit, coloured by the drum (autoplay rolls with don). Kusudama never flies.
        void SpawnArc(int index, Judgment result)
        {
            if (noteArcs == null) return;
            var note = Session.Chart.Notes[index];
            NoteKind kind;
            if (result == Judgment.Good || result == Judgment.Ok) kind = note.Kind;
            else if (result != Judgment.Roll || note.Kind == NoteKind.Kusudama) return;
            else if (note.Kind == NoteKind.Balloon)
            {
                if (Session.LongHits[index] != note.BalloonHits) return;
                kind = NoteKind.Balloon;
            }
            else kind = hitKa && !autoPlay ? NoteKind.Ka : NoteKind.Don;
            // NoteArc's is_big picks the gauge burst's circle: big don/ka and the balloon.
            bool big = kind == NoteKind.BigDon || kind == NoteKind.BigKa || kind == NoteKind.Balloon;
            noteArcs.Spawn(noteSprites[(int)kind], big, ChartTime);
        }
        void UpdateHud()
        {
            scoreCounter.Show(Session.Score);
            judgeCounter.Show(Session.Good, Session.Ok, Session.Bad, Session.Rolls);
            combo.Show(Session.Combo);
            // Player::check_note: each 100th combo starts a ComboAnnounce and its voice.
            if (Session.Combo != lastCombo && Session.Combo > 0 && Session.Combo % 100 == 0)
            {
                var voice = comboAnnounce.Announce(Session.Combo, ChartTime);
                if (voice != null) hitAudio.PlayAudioOneShot(voice, AudioGroup.Voice);
            }
            lastCombo = Session.Combo;
        }
        void OnBranchSelected(ChartBranch branch, BranchRoute route)
        {
            if (branchLane != null) branchLane.Select(route, ChartTime);
        }
        public void TogglePause()
        {
            if (IsFinished || Session == null || !songClock.Started || switcher.IsInputBlocked || closingPauseMenu) return;
            if (IsPractice) { TogglePracticePause(); return; }
            if (IsPaused) { Resume(); return; }
            songClock.Pause();
            IsPaused = true;
            pauseOpenedFrame = Time.frameCount;
            music.StopAudio(); hitAudio.StopAudio();
            DisableDrumPads();
            pauseButton.interactable = false;
            pauseMenu.Show();
        }

        public void Resume()
        {
            if (!IsPaused || closingPauseMenu || switcher.IsInputBlocked || IsFinished) return;
            if (IsPractice) { ClosePracticeMenu(); return; }
            resuming = true;
            resumeLostFocus = false;
            StartCoroutine(ClosePauseMenu(() =>
            {
                resuming = false;
                if (resumeLostFocus)
                {
                    pauseOpenedFrame = Time.frameCount;
                    pauseMenu.Show();
                    return;
                }
                songClock.Resume(GameTimeline.AudioNow);
                IsPaused = false;
                resumeFrame = Time.frameCount;
                ScheduleMusic();
                foreach (var pad in pausedPads) if (pad != null) pad.enabled = true;
                pausedPads.Clear();
                pauseButton.interactable = true;
            }));
        }

        IEnumerator ClosePauseMenu(Action completed)
        {
            closingPauseMenu = true;
            yield return pauseMenu.Hide();
            completed();
            closingPauseMenu = false;
        }

        void DisableDrumPads()
        {
            foreach (var pad in FindObjectsByType<DrumPad>(FindObjectsSortMode.None))
            {
                if (pad.gameObject.scene != gameObject.scene || !pad.enabled) continue;
                pausedPads.Add(pad);
                pad.enabled = false;
            }
        }
        void OnApplicationFocus(bool focused)
        {
            if (focused) return;
            if (resuming) resumeLostFocus = true;
            if (Session != null && !IsPaused && !IsFinished) TogglePause();
        }
        // Practice loops here; completed plays hand their data to ResultScene for persistence.
        void Finish()
        {
            if (IsPractice) { WebPlayerBridge.Instance.Finished(PlayResult.From(Session, song.name, autoPlay)); PausePractice(true); return; }
            songClock.Pause(); IsFinished = true; music.StopAudio();
            Result = PlayResult.From(Session, song.name, autoPlay);
            Result.Title = displayInfo.Title;
            Result.Subtitle = displayInfo.Subtitle;
            switcher.ShowResult(Result, song, Record);
        }
        public void Restart()
        {
            if (IsPractice && !IsFinished)
            {
                if (pausePanel.activeSelf && !closingPauseMenu)
                    StartCoroutine(ClosePauseMenu(() => { RestorePracticePads(); PausePractice(true); }));
                return;
            }
            LeavePlay(() => SceneSwitcher.EnsureInstance().Restart());
        }
        public void Back()
        {
            if (IsPractice && !pausePanel.activeSelf && !IsFinished) return;
            WebPlayerBridge.Instance.Exit();
        }

        void LeavePlay(Action action)
        {
            if (closingPauseMenu || switcher.IsInputBlocked) return;
            if (IsPaused && !IsFinished) StartCoroutine(ClosePauseMenu(action));
            else action();
        }
        void PrepareToLeave(string scene)
        {
            songClock.Pause(); IsPaused = true;
            music.StopAudio(); hitAudio.StopAudio();
            DisableDrumPads();
        }
        void OnDestroy()
        {
            music.StopAudio(); hitAudio.StopAudio();
            if (switcher != null) switcher.SceneChanging -= PrepareToLeave;
            if (Session != null) { Session.Judged -= OnJudged; Session.BranchSelected -= OnBranchSelected; }
        }

        static RectTransform Rect(string name, Transform parent, float width, float height)
        {
            var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            r.SetParent(parent, false); r.anchorMin = r.anchorMax = new Vector2(0, 1);
            r.pivot = new Vector2(0.5f, 0.5f); r.sizeDelta = new Vector2(width, height); return r;
        }
        static UnityEngine.UI.Image Image(RectTransform r, Sprite sprite)
        {
            var image = r.gameObject.AddComponent<UnityEngine.UI.Image>(); image.sprite = sprite; image.raycastTarget = false; return image;
        }
        static int ShapeOf(ChartNote note) => note.Kind == NoteKind.Balloon ? BalloonShape : note.IsLong && !note.IsBalloon ? RollShape : PlainShape;
        static int MojiShapeOf(ChartNote note) => ShapeOf(note) == RollShape ? RollShape - 1 : PlainShape;

        // Only the notes on the lane own views; prewarm enough that the first screens do not allocate.
        void CreateNotes()
        {
            shownNotes = new NoteView[Session.Chart.Notes.Count];
            shownMoji = new MojiView[Session.Chart.Notes.Count];
            shownBars = new RectTransform[Session.Chart.Bars.Count];
            for (int i = 0; i < 32; i++) notePool[PlainShape].Push(NewNote(PlainShape));
            for (int i = 0; i < 2; i++) notePool[RollShape].Push(NewNote(RollShape));
            notePool[BalloonShape].Push(NewNote(BalloonShape));
            if (mojiLayer != null)
            {
                for (int i = 0; i < 32; i++) mojiPool[0].Push(NewMoji(0));
                for (int i = 0; i < 2; i++) mojiPool[1].Push(NewMoji(1));
            }
            for (int i = 0; i < 8; i++) barPool.Push(NewBar());
        }
        NoteView NewNote(int shape)
        {
            var root = Rect("Note", noteLayer, NoteSize, NoteSize);
            var view = new NoteView { Shape = shape, Root = root };
            if (shape == RollShape)
            {
                view.Body = Rect("RollBody", root, 0, 0);
                view.Body.anchorMin = new Vector2(0.5f, 0);
                view.Body.anchorMax = new Vector2(0.5f, 1);
                view.Body.pivot = new Vector2(0, 0.5f);
                view.BodyImage = Image(view.Body, null);
                view.Tail = Rect("RollTail", root, 0, 0);
                view.Tail.anchorMin = view.Body.anchorMin;
                view.Tail.anchorMax = view.Body.anchorMax;
                view.Tail.pivot = view.Body.pivot;
                view.TailImage = Image(view.Tail, null);
            }
            var head = Rect("Head", root, 0, 0);
            // Match draw_balloon's balloon_offset as a fraction of the note width.
            // Stretch anchors keep the face aligned when the note or Canvas scales.
            float faceOffset = shape == BalloonShape ? BalloonFace : 0;
            head.anchorMin = new Vector2(-faceOffset, 0);
            head.anchorMax = new Vector2(1 - faceOffset, 1);
            view.Head = Image(head, null);
            if (shape == BalloonShape)
            {
                // notes/10 joins the right edge of notes/7 in draw_balloon.
                var tail = Rect("BalloonTail", root, 0, 0);
                tail.anchorMin = new Vector2(1 - faceOffset, 0);
                tail.anchorMax = new Vector2(2 - faceOffset, 1);
                view.BalloonTail = Image(tail, balloonTailSprite);
            }
            root.gameObject.SetActive(false);
            return view;
        }
        // draw_notes' second pass draws every text after every note, so the text sits in its
        // own layer above the notes; within it earlier notes again paint over later ones.
        MojiView NewMoji(int shape)
        {
            var root = Rect("Moji", mojiLayer, MojiWidth, MojiHeight);
            var view = new MojiView { Shape = shape, Root = root };
            if (shape == 1)
            {
                // draw_drumroll: moji_drumroll_mid from head to tail, then the head and tail text.
                view.Mid = Rect("Mid", root, 0, 0);
                view.Mid.anchorMin = new Vector2(0.5f, 0);
                view.Mid.anchorMax = new Vector2(0.5f, 1);
                view.Mid.pivot = new Vector2(0, 0.5f);
                Image(view.Mid, mojiRollSprite);
                view.MidAspect = mojiRollSprite.rect.width / mojiRollSprite.rect.height;
            }
            var head = Rect("Head", root, 0, 0);
            head.anchorMin = Vector2.zero; head.anchorMax = Vector2.one;
            view.Head = Image(head, null);
            if (view.Mid != null)
            {
                view.Tail = Rect("Tail", root, 0, 0);
                view.Tail.anchorMin = Vector2.zero; view.Tail.anchorMax = Vector2.one;
                Image(view.Tail, mojiSprites[NoteMoji.Tail]);
            }
            root.gameObject.SetActive(false);
            return view;
        }
        RectTransform NewBar()
        {
            var root = Rect("Measure", barLayer, 3, 200);
            Image(root, null);
            root.gameObject.SetActive(false);
            return root;
        }

        NoteView AcquireNote(int index, ChartNote note)
        {
            int shape = ShapeOf(note);
            var view = notePool[shape].Count > 0 ? notePool[shape].Pop() : NewNote(shape);
            view.Root.name = note.Kind.ToString();
            view.Head.sprite = noteSprites[(int)note.Kind];
            if (shape == RollShape)
            {
                int size = note.Kind == NoteKind.BigRoll ? 1 : 0;
                view.BodyImage.sprite = rollBodySprites[size];
                view.TailImage.sprite = rollTailSprites[size];
                view.TailAspect = RollTailAspect(note);
            }
            shownNotes[index] = view;
            view.Root.gameObject.SetActive(true);
            return view;
        }
        void ReleaseNote(int index)
        {
            var view = shownNotes[index];
            view.Root.gameObject.SetActive(false);
            notePool[view.Shape].Push(view);
            shownNotes[index] = null;
        }
        MojiView AcquireMoji(int index, ChartNote note)
        {
            int shape = MojiShapeOf(note);
            var view = mojiPool[shape].Count > 0 ? mojiPool[shape].Pop() : NewMoji(shape);
            view.Root.name = note.Kind + "Moji";
            view.Head.sprite = mojiSprites[note.Moji];
            shownMoji[index] = view;
            view.Root.gameObject.SetActive(true);
            return view;
        }
        void ReleaseMoji(int index)
        {
            var view = shownMoji[index];
            view.Root.gameObject.SetActive(false);
            mojiPool[view.Shape].Push(view);
            shownMoji[index] = null;
        }
        float RollTailAspect(ChartNote note)
        {
            var sprite = rollTailSprites[note.Kind == NoteKind.BigRoll ? 1 : 0];
            return sprite.rect.width / sprite.rect.height;
        }

        // draw_notes walks draw_note_buffer in reverse, so earlier notes paint over
        // later ones; uGUI draws later siblings on top. Pooled views are re-stacked
        // whenever a note enters, since a reused view keeps its old sibling slot.
        // Only candidates hold views, so walking them in descending index order is enough.
        static void Restack<T>(T[] shown, List<int> order, Func<T, Transform> root) where T : class
        {
            for (int k = order.Count - 1; k >= 0; k--)
                if (shown[order[k]] != null) root(shown[order[k]]).SetAsLastSibling();
        }
        void SortCandidates(LaneWindow window)
        {
            stackOrder.Clear();
            for (int k = 0; k < window.Count; k++) stackOrder.Add(window[k]);
            stackOrder.Sort();
        }
        void ReleaseBar(int index)
        {
            var root = shownBars[index];
            root.gameObject.SetActive(false); barPool.Push(root); shownBars[index] = null;
        }

        // Lane intervals depend on the chart and the lane width (the travel distance), so a
        // width change rebuilds them and starts again from an empty lane.
        bool EnsureLaneWindows()
        {
            float width = noteLayer.rect.width;
            if (noteWindow != null && width == windowWidth) return false;
            for (int i = 0; i < shownNotes.Length; i++) { if (shownNotes[i] != null) ReleaseNote(i); if (shownMoji[i] != null) ReleaseMoji(i); }
            for (int i = 0; i < shownBars.Length; i++) if (shownBars[i] != null) ReleaseBar(i);
            windowWidth = width;
            noteWindow = LaneCull.ForNotes(Session.Chart.Notes, width, JudgeLocalX, Math.Max(NoteSize, MojiWidth));
            barWindow = LaneCull.ForBars(Session.Chart.Bars, width, JudgeLocalX, NoteSize);
            return true;
        }

        // Nijiiro: lane x=498/y=276, judge x=618, note top=14 with 192-pixel sprites.
        const float JudgeLocalX = 120, JudgeLocalY = -110;
        // notes/moji frames are 256x48; skin moji.y=209 against notes.y=14, centre to centre.
        const float MojiWidth = 256, MojiHeight = 48, MojiDrop = 209 - 14 + MojiHeight / 2 - NoteSize / 2;
        // Nijiiro note frames are 192x192; draw_balloon shifts the face by 12/128 of the width.
        const float NoteSize = 192, BalloonFace = 12f / 128f;
        double TravelDistance => noteLayer.rect.width - JudgeLocalX;

        Vector2 Position(ChartNote note, double time)
        {
            double x = NoteScroll.DistanceFromJudge(note.Time, time, note.Bpm, note.ScrollX, TravelDistance);
            double y = NoteScroll.DistanceFromJudge(note.Time, time, note.Bpm, note.ScrollY, TravelDistance);
            return new Vector2(JudgeLocalX + (float)x, JudgeLocalY - (float)y);
        }
        void RenderNotes(double time)
        {
            bool preview = IsPractice && IsPaused;
            bool rebuilt = EnsureLaneWindows();
            if (!rebuilt && rendered && time == RenderedTime && Session == renderedSession && Session.Version == renderedVersion
                && balloonCounter.NoteIndex == renderedBalloon && preview == renderedPreview) return;
            rendered = true; renderedSession = Session; renderedVersion = Session.Version;
            renderedBalloon = balloonCounter.NoteIndex; renderedPreview = preview;
            RenderedTime = time;
            bool gogo = false, notesEntered = false, mojiEntered = false;
            var chartNotes = Session.Chart.Notes;
            // Outside its interval a note is off the lane, exactly as the full cull would find it.
            leftLane.Clear();
            noteWindow.Seek(time, leftLane);
            foreach (int i in leftLane)
            {
                if (shownNotes[i] != null) ReleaseNote(i);
                if (shownMoji[i] != null) ReleaseMoji(i);
            }
            for (int k = 0; k < noteWindow.Count; k++)
            {
                int i = noteWindow[k];
                var note = chartNotes[i]; var view = shownNotes[i]; var pos = Position(note, time);
                if (note.IsBalloon && time >= note.Time) pos = new Vector2(JudgeLocalX, JudgeLocalY);
                float length = note.IsLong && !note.IsBalloon ? (float)NoteScroll.RollLength(note, TravelDistance) : 0;
                // ドロン hides the notes; they are still judged. Like draw_note_buffer,
                // only a hit removes a note: a 5/6 roll resolved at its tail and a note
                // missed by timeout keep scrolling until they leave the lane.
                bool rolling = note.IsLong && !note.IsBalloon;
                bool alive = note.Display && (preview ? Session.IsPracticePreviewActive(note) : Session.IsActive(note)) && (rolling || Session.Missed[i] || !Session.Resolved[i]);
                bool visible = alive && InLane(pos.x, Reach(note, view, length));
                if (mojiLayer != null) mojiEntered |= RenderMoji(i, note, pos, length, alive);
                if (!visible)
                {
                    if (view != null) ReleaseNote(i);
                    continue;
                }
                if (view == null) { view = AcquireNote(i, note); notesEntered = true; }
                else if (!view.Root.gameObject.activeSelf) view.Root.gameObject.SetActive(true);
                view.Root.anchoredPosition = pos;
                if (view.BalloonTail != null) view.BalloonTail.enabled = balloonCounter.NoteIndex != i;
                if (view.Body != null)
                {
                    // draw_drumroll adds the strip's native width to length +
                    // drumroll_width_offset: (48 - 47) / 128 of the note height.
                    float overlap = view.Root.rect.height / 128f;
                    float direction = length < 0 ? -1 : 1;
                    view.Body.sizeDelta = new Vector2(Mathf.Abs(length) + overlap, 0);
                    view.Body.localScale = new Vector3(direction, 1, 1);
                    view.Tail.anchoredPosition = new Vector2(length, 0);
                    view.Tail.sizeDelta = new Vector2(view.Root.rect.height * view.TailAspect, 0);
                    view.Tail.localScale = view.Body.localScale;
                }
                if (note.Gogo && note.Time - time < 1) gogo = true;
            }
            if (notesEntered || mojiEntered) SortCandidates(noteWindow);
            if (notesEntered) Restack(shownNotes, stackOrder, v => v.Root);
            if (mojiEntered) Restack(shownMoji, stackOrder, v => v.Root);
            leftLane.Clear();
            barWindow.Seek(time, leftLane);
            foreach (int i in leftLane) if (shownBars[i] != null) ReleaseBar(i);
            for (int k = 0; k < barWindow.Count; k++)
            {
                int i = barWindow[k];
                var bar = Session.Chart.Bars[i];
                var pos = Position(bar, time); pos.y -= 4;
                float half = (bar.IsBranchStart ? 6 : 3) / 2f;
                bool visible = bar.Display && (preview ? Session.IsPracticePreviewActive(bar) : Session.IsActive(bar)) && InLane(pos.x, new Vector2(-half, half));
                var root = shownBars[i];
                if (!visible)
                {
                    if (root != null) ReleaseBar(i);
                    continue;
                }
                if (root == null)
                {
                    root = shownBars[i] = barPool.Count > 0 ? barPool.Pop() : NewBar();
                    root.sizeDelta = new Vector2(bar.IsBranchStart ? 6 : 3, 200);
                    root.GetComponent<UnityEngine.UI.Image>().color = bar.IsBranchStart ? new Color(1, 0.8f, 0.2f, 0.8f) : new Color(1, 1, 1, 0.35f);
                    root.gameObject.SetActive(true);
                }
                root.anchoredPosition = pos;
            }
            // GogoPulse.anim: 0.18 ± 0.05, one period every 2π/12 s of song time.
            gogoPulse ??= gogoTint.GetComponent<ClipSampler>();
            if (!gogo) gogoTint.alpha = 0;
            else
            {
                double period = gogoPulse.clip.length;
                gogoPulse.Sample((time % period + period) % period);
            }
        }

        // The text follows its note's lifetime and is culled by its own extent.
        // Returns whether a pooled view was taken for it this frame.
        bool RenderMoji(int index, ChartNote note, Vector2 pos, float length, bool alive)
        {
            // skip_note: a balloon whose counter is up draws neither the note nor its text.
            alive &= balloonCounter.NoteIndex != index;
            bool roll = MojiShapeOf(note) == 1;
            var view = shownMoji[index];
            // draw_drumroll places roll text at the lane height, ignoring the head's Y scroll.
            var at = new Vector2(pos.x, (roll ? JudgeLocalY : pos.y) - MojiDrop);
            float half = (view != null ? view.Root.rect.width : MojiWidth) / 2;
            LaneCull.MojiReach(roll, 2 * half, length, out double min, out double max);
            var reach = new Vector2((float)min, (float)max);
            if (!alive || !InLane(at.x, reach))
            {
                if (view != null) ReleaseMoji(index);
                return false;
            }
            bool entered = view == null;
            if (entered) view = AcquireMoji(index, note);
            else if (!view.Root.gameObject.activeSelf) view.Root.gameObject.SetActive(true);
            view.Root.anchoredPosition = at;
            if (!roll) return entered;
            // The strip is drawn native width + length wide from the head, like t_moji_drumroll_mid's x2.
            float width = view.Root.rect.height * view.MidAspect + length;
            view.Mid.sizeDelta = new Vector2(Mathf.Abs(width), 0);
            view.Mid.localScale = new Vector3(width < 0 ? -1 : 1, 1, 1);
            view.Tail.anchoredPosition = new Vector2(length, 0);
            return entered;
        }

        // The lane clip mask is the visible area, in Canvas units that follow the
        // window resolution, so cull against its live rect instead of fixed pixels.
        // Dancer.anim at a time already sampled (a still pause) is left as it is.
        void SampleDancers(double time)
        {
            if (time == dancerTime) return;
            dancerTime = time;
            foreach (var dancer in dancers) dancer.SampleLoop(time);
        }
        bool InLane(float x, Vector2 reach) => LaneCull.InLane(x, reach.x, reach.y, noteLayer.rect.width);

        // Horizontal extent of a note's sprites relative to its centre, from the current
        // size of its view, or the design size while it has none.
        Vector2 Reach(ChartNote note, NoteView view, float length)
        {
            Vector2 size = view != null ? view.Root.rect.size : new Vector2(NoteSize, NoteSize);
            float aspect = note.IsLong && !note.IsBalloon ? RollTailAspect(note) : 0;
            LaneCull.NoteReach(note, size.x, size.y, length, aspect, BalloonFace, out double min, out double max);
            return new Vector2((float)min, (float)max);
        }
    }
}
