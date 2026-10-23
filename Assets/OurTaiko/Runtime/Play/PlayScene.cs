using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

namespace OurTaiko
{
    // The play scenes' driver: the song clock, music, pause, title and ending around one PlayerLane per
    // seat. The 1P lane's views are this component's own (LaneViews); DoublePlayScene adds secondLane.
    public sealed partial class PlayScene : LaneViews
    {
        public AudioSource music, hitAudio;
        public AudioClip don, ka, balloonPop, kusudamaPop;
        public HitSoundLibrary hitSounds;
        public Sprite[] laneDifficultySprites;
        public Sprite[] noteSprites;
        [Tooltip("Nijiiro expression frame 1, indexed by NoteKind like noteSprites; alternates every eighth note at 50 combo.")]
        public Sprite[] alternateNoteSprites;
        public Sprite[] rollBodySprites, rollTailSprites;
        public Sprite balloonTailSprite;
        public Sprite[] mojiSprites;
        public Sprite mojiRollSprite;
        public Sprite[] judgmentSprites;
        [Tooltip("The touch drum; Settings > Play > Enable Drumpad for Single Player Mode turns it on or off. It strikes 1P.")]
        public DrumPad drumPad;
        public TMP_Text title, subtitle, resultText;
        [Tooltip("DoublePlayScene: 2P's lane. Empty in the single-player scenes.")]
        public LaneViews secondLane;
        public GameObject pausePanel, resultPanel;
        public PauseMenuView pauseMenu;
        public UnityEngine.UI.Button pauseButton, restartButton, backButton, resumeButton, resultRestart, resultBack;
        [Tooltip("The backdrop's dancers, on the chart's tempo and the soul gauge. DoublePlayScene has none.")]
        public DancerTroupeView dancers;
        [Tooltip("The kusudama ball over the whole screen, shared by both seats.")]
        public KusudamaView kusudama;

        // One lane per seat: 1P, then 2P in DoublePlayScene.
        public IReadOnlyList<PlayerLane> Lanes => lanes;
        readonly List<PlayerLane> lanes = new List<PlayerLane>();
        public PlayerLane Lane1 => lanes.Count > 0 ? lanes[0] : null;
        public bool IsTwoPlayer => secondLane != null;
        public PlaySession Session
        {
            get => Lane1?.Session;
            private set => Lane1.Replace(value);
        }
        // A drum press still waiting out the input delay must not land on the other side of a pause.
        public bool IsPaused
        {
            get => paused;
            private set
            {
                if (paused != value) { InputManager.DropDelayed(); foreach (var lane in lanes) lane.ForgetHeard(); }
                paused = value;
            }
        }
        bool paused;
        // 1P's drum sounds started so far.
        public int DrumSounds => Lane1?.DrumSounds ?? 0;
        public bool IsFinished { get; private set; }
        public PlayResult Result => Lane1?.Result;
        public Online.PlayRecord Record => Lane1?.Record;
        readonly SongClock songClock = new SongClock();
        public double SongTime => songClock.Time;
        public double RenderedTime => Lane1?.RenderedTime ?? 0;
        // Views come from pools while a note is on the lane; null when it is not drawn.
        public RectTransform NoteRoot(int index) => Lane1.NoteRoot(index);
        public RectTransform MojiRoot(int index) => Lane1.MojiRoot(index);
        public RectTransform BarRoot(int index) => Lane1.BarRoot(index);
        SongDefinition song;
        double audioOffset, visualOffset, judgeOffset;
        // OpenTaiko judges and draws at a whole millisecond of the play clock (cut toward zero).
        double ChartTime => TjaTime.Truncate(SongTime - audioOffset);
        internal double ChartTimeNow => ChartTime;
        internal double JudgeOffset => judgeOffset;
        internal bool AcceptsHits => songClock.Started && !switcher.IsInputBlocked && !IsPaused && !IsFinished;
        SongInfo displayInfo;
        // The subtitle line shows the SUBTITLE and, when the chart names one, the MAKER in turn.
        const float SubtitleRotationSeconds = 4f;
        string[] subtitleLines = Array.Empty<string>();
        int subtitleIndex;
        float subtitleShownAt;
        bool autoPlay;
        SceneSwitcher switcher;
        readonly List<DrumPad> pausedPads = new List<DrumPad>();
        bool closingPauseMenu;
        bool resuming, resumeLostFocus;
        int resumeFrame = -1;
        int pauseOpenedFrame = -1;

        IEnumerator Start()
        {
            while (WebPlayerBridge.Instance == null || WebPlayerBridge.Instance.Song == null) yield return null;
            switcher = SceneSwitcher.EnsureInstance();
            // Direct Editor runs must return to the same mode after Back and another song.
            switcher.PracticeMode = IsPractice;
            switcher.SceneChanging += PrepareToLeave;
            song = switcher.SelectedSong;
            // Play scenes start from the song list; a direct run without a song goes back to Entry.
            if (song == null)
            {
                Debug.LogWarning("No song is selected; returning to Entry.", this);
                switcher.SwitchScene(SceneSwitcher.MenuScene);
                yield break;
            }
            autoPlay = switcher.AutoPlay;
            var playSettings = SettingManager.EnsureInstance().Settings.play;
            audioOffset = (song.audioOffsetMs + (double)playSettings.audioOffsetMs) / 1000.0;
            visualOffset = song.visualOffsetMs / 1000.0;
            judgeOffset = autoPlay ? 0 : playSettings.judgeOffsetMs / 1000.0;
            pauseButton.onClick.AddListener(TogglePause);
            resumeButton.onClick.AddListener(Resume);
            restartButton.onClick.AddListener(Restart);
            backButton.onClick.AddListener(Back);
            resultRestart.onClick.AddListener(Restart);
            resultBack.onClick.AddListener(Back);
            pausePanel.SetActive(false); resultPanel.SetActive(false);
            // Disabled and hidden together: an inactive pad neither draws nor registers with InputManager,
            // and pause/resume only re-enables the pads it disabled itself.
            // The touch drum is a single-player setting; two players use the keyboard.
            if (drumPad != null) drumPad.gameObject.SetActive(SettingManager.EnsureInstance().Settings.play.singlePlayerDrumPad && !IsTwoPlayer);
            try
            {
                // Practice plays a fixed route, chosen only in its menu; a normal play evaluates branches.
                practiceBranch = BranchRoute.Normal;
                if (kusudama != null) kusudama.ResetDisplay();
                var sessions = new List<PlaySession> { AddLane(0, this) };
                if (secondLane != null) sessions.Add(AddLane(1, secondLane));
                // Both seats' kusudama at the same time are one ball; a lone one is a balloon.
                if (sessions.Count > 1) Kusudama.Share(sessions[0], sessions[1]);
                for (int i = 0; i < lanes.Count; i++) lanes[i].Prepare(sessions[i]);
                // The single-player scenes' don/ka are 1P's neiro (tests read them).
                don = Lane1.Don; ka = Lane1.Ka;
                displayInfo = song.ReadDisplayInfo();
                title.text = displayInfo.Title;
                ShowSubtitleLines(displayInfo);
                music.SetAudioSong(song);
                hitAudio.PrepareAudioEffects(balloonPop, kusudamaPop);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                IsFinished = true; resultPanel.SetActive(true);
                resultText.text = "CHART COULD NOT LOAD\n<size=22>" + error.Message + "</size>";
            }
            if (IsFinished) yield break;
            while (switcher.IsInputBlocked) yield return null;
            // The full countdown starts only after the global cover has opened. Both seats play the same
            // file, so one OFFSET leads the song in.
            songClock.Start(GameTimeline.AudioNow, Math.Max(2, Session.Chart.Offset + 2 - audioOffset - Math.Min(0, judgeOffset)));
            if (IsPractice) InitializePractice();
            else StartDueMusic();
            WebPlayerBridge.Instance.Attach(this);
        }

        // SongLoadingScene parsed each seat's chart behind the curtain; restarts and direct runs parse
        // here. 2P reads its own block of the course (ChartSide) with its own options. The lane is
        // prepared with the returned session once every seat has one.
        PlaySession AddLane(int seat, LaneViews views)
        {
            string course = switcher.CourseOf(seat);
            var options = PlayOptions.ForSeat(seat);
            var chart = switcher.TakePreparedChart(song, course, seat) ?? PrepareChart(song, course, seat);
            var lane = new PlayerLane(this, seat, views, options, seat == 0 ? autoPlay : switcher.AutoPlayOf(seat), new Online.PlayRecord
            {
                // Replay v1 stores already corrected judgment time. Keep its two-offset convention:
                // visual time = recorded judgment time - VisualOffsetMs (B cancels here).
                AudioOffsetMs = (int)Math.Round((audioOffset + judgeOffset) * 1000),
                VisualOffsetMs = (int)Math.Round((visualOffset - judgeOffset) * 1000),
            });
            lanes.Add(lane);
            var session = new PlaySession(chart, judgeOffset, IsPractice ? practiceBranch : (BranchRoute?)null);
            foreach (string warning in session.Chart.Warnings) Debug.LogWarning("Ignored TJA command: " + warning);
            return session;
        }

        void ShowSubtitleLines(SongInfo info)
        {
            subtitleLines = new[] { info.Subtitle, info.Maker }.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
            subtitleIndex = 0;
            subtitleShownAt = Time.unscaledTime;
            subtitle.text = subtitleLines.Length > 0 ? subtitleLines[0] : "";
        }

        // Alternates the subtitle and the maker on a fixed cadence, through pauses too.
        void RotateSubtitle()
        {
            if (subtitleLines.Length < 2 || Time.unscaledTime - subtitleShownAt < SubtitleRotationSeconds) return;
            subtitleIndex = (subtitleIndex + 1) % subtitleLines.Length;
            subtitleShownAt = Time.unscaledTime;
            subtitle.text = subtitleLines[subtitleIndex];
        }

        // Player::reset_chart: the play options change the chart before load times are taken.
        public static TaikoChart PrepareChart(SongDefinition song, string course, int seat = 0)
        {
            var chart = song.Parse(course, SeatBlocks.Seats[seat]);
            ChartModifiers.Apply(chart, PlayOptions.ForSeat(seat), new System.Random());
            return chart;
        }

        // MajdataPlay plays the track once the clock reaches it and restarts the clock from there.
        void StartDueMusic()
        {
            if (songClock.TrackDue(GameTimeline.AudioNow) is not double position) return;
            music.StopAudio();
            if (position >= music.AudioLength()) { songClock.SkipTrack(); return; }
            music.SeekAudio(position);
            music.PlayAudio();
            songClock.TrackStarted(GameTimeline.AudioNow);
        }

        void Update()
        {
            RotateSubtitle();
            if (switcher == null || switcher.IsInputBlocked || !songClock.Started) return;
            if (IsFinished) return;
            // Keep the intentional frame-based judgment; input event timestamps only sort hits.
            double? playback = AudioEngine.EnsureInstance().Backend == AudioBackend.Bass && music.IsAudioPlaying()
                ? music.AudioPosition() : null;
            songClock.Update(GameTimeline.AudioFrameTime, playback);
            StartDueMusic();
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
            foreach (var lane in lanes) lane.Advance(time);
            UpdatePlayVisuals(time);
            // The song ends a second after its audio; charts often stretch their last measure far
            // past it (a closing measure at a few BPM). Without audio, the longest chart decides.
            double audio = music.AudioLength();
            double duration = lanes.Max(lane => lane.Session.Chart.Duration);
            if (audio > 0 ? SongTime > audio + 1 : time > duration + Math.Max(0, judgeOffset) + 1) Finish();
        }

        // Presentation can keep running after the result is captured, without advancing judgment.
        void UpdatePlayVisuals(double time)
        {
            bool preview = IsPractice && IsPaused, hideSkipped = IsPractice && (!IsPaused || practiceRewinding);
            foreach (var lane in lanes) lane.ShowVisuals(time, time - visualOffset, preview, hideSkipped);
            if (kusudama != null) kusudama.ShowTime(time);
            SampleDancers(time);
        }

        // 1P's drum (touch drum, tests); see PlayerLane.Hit.
        public void Hit(bool isKa, bool right, bool sound = true) => Lane1?.Hit(isKa, right, sound);

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
                StartDueMusic();
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
            foreach (var lane in lanes)
            {
                lane.Result = PlayResult.From(lane.Session, song.name, lane.AutoPlay);
                lane.Result.Title = displayInfo.Title;
                lane.Result.Subtitle = displayInfo.Subtitle;
            }
            DisableDrumPads();
            pauseButton.interactable = false;
            StartCoroutine(ShowEnding());
        }

        // Each lane plays its own ending (Player::spawn_ending_anim) over its lane at the same time.
        IEnumerator ShowEnding()
        {
            var endings = lanes.Where(lane => lane.Views.ending != null).ToList();
            if (endings.Count > 0)
            {
                foreach (var lane in endings) lane.Views.ending.Begin(lane.Result);
                double duration = endings.Max(lane => lane.Views.ending.Duration);
                double started = GameTimeline.FrameTime;
                double chartTime = ChartTime;
                while (GameTimeline.FrameTime - started < duration)
                {
                    double elapsed = GameTimeline.FrameTime - started;
                    // Continue from the last song frame on the ending's real-time clock. The
                    // stopped SongClock and Session remain untouched; dancers, gauge loops and
                    // existing hit effects keep playing while input and scoring stay disabled.
                    UpdatePlayVisuals(chartTime + elapsed);
                    foreach (var lane in lanes) lane.ShowBranchTime(chartTime + elapsed);
                    foreach (var lane in endings) lane.Views.ending.ShowTime(elapsed);
                    yield return null;
                }
                UpdatePlayVisuals(chartTime + duration);
                foreach (var lane in endings) lane.Views.ending.ShowTime(duration);
            }
            var second = lanes.Count > 1 ? lanes[1] : null;
            switcher.ShowResult(Result, song, Record, second?.Result, second?.Record);
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
            foreach (var lane in lanes) lane.Release();
        }

        // A paused practice previews its routes; while it plays (or rewinds) it hides what the
        // attempt skipped, and browsing measures keeps past notes.
        void RenderNotes(double time)
        {
            bool preview = IsPractice && IsPaused;
            bool hideSkipped = IsPractice && (!IsPaused || practiceRewinding);
            foreach (var lane in lanes) lane.RenderNotes(time, preview, hideSkipped);
        }

        void SampleDancers(double time)
        {
            if (dancers != null) dancers.ShowTime(time, Session.BpmAt(time), Session.GaugePoints / SoulGauge.MaximumPoints, Session.IsClear);
        }
    }
}
