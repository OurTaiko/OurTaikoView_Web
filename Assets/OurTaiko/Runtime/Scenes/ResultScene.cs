using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // Nijiiro single-player result screen (scenes/result.cpp + Scripts/result/*.lua). The 3D Don,
    // score-rank clip and option icons are not ported. The screen is saved in the scene
    // (ResultView); Awake binds it and fills in the run, and the sprites below are the per-run art.
    public sealed class ResultScene : MonoBehaviour
    {
        public RectTransform stage;
        public ResultView view;
        public ArcadeOverlayArt overlay;

        [Header("Board")]
        public Sprite[] difficulties;
        public Sprite[] judgeDigits, scoreDigits;

        [Header("Gauge")]
        public Sprite[] unfilled;   // easy, normal, hard art
        public Sprite[] overlays;   // easy, normal, hard art
        public Sprite[] rainbow;    // 8 cells per art, easy / normal / hard
        public Sprite clearCaption, clearCaptionDark, soul, soulDark;
        public Sprite[] soulFire;

        [Header("Crown and message")]
        public Sprite[] crowns;     // clear, full combo, donderful combo
        public Sprite[] gleam, messages;

        [Header("High score")]
        public Sprite[] highScoreDigits;

        [Header("Timelines")]
        public TextAsset backgroundTimeline;
        public TextAsset fujiTimeline, successTimeline, crownTimeline, crownLoopTimeline, messageTimeline,
            judgeDigitTimeline, scoreDigitTimeline, fireTimeline, rainbowTimeline, highScoreTimeline;

        [Header("Audio")]
        public AudioSource bgm;
        public AudioSource loop, sfx, voice;
        public AudioClip rankSound;
        public AudioClip don, donBig, countStop, countLoop, achieve, atmosClear, crownSilver, crownGold, crownRainbow, highScoreVoice, fullComboVoice;
        public AudioClip[] messageVoices; // miss, near, success, perfect

        public const float GaugeScale = 0.7f, GaugeUnit = 21 * GaugeScale;
        public static readonly string[] MessageTexts = { "もう少し\nがんばるドン!", "おしかったドン", "ノリノリだドン", "よくできたドン！" };
        // per gauge art (easy, normal, hard): the first clear cell and the クリア caption's x
        public static readonly int[] ClearCell = { 30, 35, 40 };
        public static readonly float[] ClearCaptionX = { 518, 585, 663 };
        static readonly (string Track, int Frame)[] RainbowLeaves = {
            ("#15@0", 0), ("#17@1", 1), ("#19@2", 2), ("#21@3", 3), ("#23@4", 4), ("#25@5", 5), ("#27@6", 6), ("#29@7", 7), ("#15@8", 0) };
        static readonly (string Track, int Frame)[] FireLeaves = {
            ("#40@0", 0), ("#44@0", 1), ("#46@0", 2), ("#48@0", 3), ("#50@0", 4), ("#52@0", 5), ("#54@0", 6), ("#56@0", 7) };
        static readonly (string Track, int Frame)[] ShineLeaves = { ("#65@1", 0), ("#67@1", 1), ("#69@1", 2) };

        public PlayResult Result { get; private set; }
        public ResultSequence Sequence { get; private set; }
        public bool IsLeaving { get; private set; }
        public CoinOverlayView Coins { get; private set; }

        SceneSwitcher switcher;
        LumenClip bgClip, successClip, crownClip, crownLoop, messageClip, judgePop, scorePop, fireClip, rainbowClip, highScoreClip;
        ResultBackground background, fadeIn;
        double sceneStart, revealStart = -1, successAt = -1;
        int art;
        Image successImage, unfilledImage, barImage, transitionImage, topImage, bottomImage, overlayImage, captionImage, soulImage, fireImage, sheenImage;
        readonly Image[] rainbowImages = new Image[2];
        Image[][] judgeImages;
        Image[] scoreOutline, scoreFill, highScoreNumber;
        RectTransform highScoreGroup;
        CanvasGroup highScoreAlpha;
        Vector2 highScoreBase, crownBase;
        Image crownImage, crownGhost, burstA, burstB, stars, shine, messageImage;
        TextMeshProUGUI messageText;

        double Clock => GameTimeline.FrameTime * 1000;
        double Reveal => revealStart < 0 ? -1 : Clock - revealStart;

        void Awake()
        {
            switcher = SceneSwitcher.EnsureInstance();
            Result = switcher.LastResult ?? Placeholder();
            SaveScore();
            Sequence = new ResultSequence(Result);
            int difficulty = Math.Min(Math.Max((int)Result.Difficulty, 0), 3);
            art = difficulty == 0 ? 0 : difficulty == 3 ? 2 : 1;
            bgClip = Clip(backgroundTimeline); successClip = Clip(successTimeline); crownClip = Clip(crownTimeline);
            crownLoop = Clip(crownLoopTimeline); messageClip = Clip(messageTimeline); judgePop = Clip(judgeDigitTimeline);
            scorePop = Clip(scoreDigitTimeline); fireClip = Clip(fireTimeline); rainbowClip = Clip(rainbowTimeline);
            highScoreClip = Clip(highScoreTimeline);
            sceneStart = Clock;
            Bind();
            if (view.scoreRank != null) view.scoreRank.Show(0);
            switcher.SceneChanging += OnSceneChanging;
        }

        // Only entering ResultScene commits a completed play. Practice never enters this scene.
        // Consume before saving so scene reloads and Editor previews cannot submit a run twice.
        void SaveScore()
        {
            if (!switcher.TakeCompletedPlay(out var song, out var record) || Result.AutoPlay) return;
            if (!SongScores.IsOnline(song)) { ScoreStore.Shared.Save(Result); return; }
            var online = Online.OnlineManager.Instance;
            var chart = online != null ? online.ChartOf(song) : null;
            if (chart == null) return;
            int difficulty = (int)Result.Difficulty;
            var best = online.Client.Best(chart, difficulty);
            if (best != null) Result.PreviousBest = (int)Math.Min(int.MaxValue, Math.Max(Result.PreviousBest, best.Score));
            online.Client.Submit(chart, difficulty, new Online.FanmadeScore
            {
                Good = Result.Good, Ok = Result.Ok, Bad = Result.Bad, Score = Result.Score,
                Drumroll = Result.Rolls, MaxCombo = Result.MaxCombo, ClearStatus = (int)Result.StoredCrown,
            }, record);
        }

        static LumenClip Clip(TextAsset asset) => asset != null ? LumenClip.Parse(asset.text) : LumenClip.Empty;

        // Opening the Result scene on its own shows a sample failed run instead of an empty board.
        static PlayResult Placeholder() => new PlayResult
        {
            Title = "TRIPLE HELIX", Subtitle = "Yonokid", Course = "Oni", Difficulty = Difficulty.Oni, Level = 9,
            Score = 754320, Good = 612, Ok = 98, Bad = 47, MaxCombo = 233, Rolls = 41, GaugePoints = 6420, AutoPlay = true,
        };

        void Start()
        {
            bgm.SetAudioGroup(AudioGroup.Bgm);
            voice.SetAudioGroup(AudioGroup.Voice);
            sfx.PrepareAudioEffects(don, donBig, countStop, achieve, atmosClear, crownSilver, crownGold, crownRainbow, rankSound);
            voice.PrepareAudioTracks(highScoreVoice, fullComboVoice);
            voice.PrepareAudioTracks(messageVoices);
            loop.PrepareAudioTracks(countLoop);
            bgm.loop = true;
            bgm.PlayAudio();
        }

        void OnDestroy()
        {
            if (switcher != null) switcher.SceneChanging -= OnSceneChanging;
        }

        void OnSceneChanging(string scene)
        {
            IsLeaving = true;
            loop.StopAudio();
        }

        void Update()
        {
            double clock = Clock;
            // The reveal clock starts once the global cover has opened, like the play scene.
            if (revealStart < 0 && !switcher.IsInputBlocked) revealStart = clock;
            float clear = 0;
            if (Result.GaugeState != GaugeState.Failed && successAt >= 0)
                clear = (float)successClip.Get("crear_1P_l", successClip.Label("success").GetValueOrDefault(5) + (clock - successAt) * 0.06, "a", 1);
            background.Draw(clock - sceneStart, clear);
            successImage.Alpha(clear);
            if (revealStart < 0) { fadeIn.Group.alpha = 1; fadeIn.Draw(clock - sceneStart, 0); return; }

            double now = Reveal;
            foreach (var (cue, row) in Sequence.Update(now)) Play(cue, row, clock);
            HandleInput();
            if (!IsLeaving && Sequence.ShouldAutoAdvance) Leave();

            // result animation 15: the backdrop drawn on top, faded out after 100 ms over 316.67 ms.
            float cover = 1 - (float)Math.Min(1, Math.Max(0, (now - ResultSequence.FadeInDelayMs) / ResultSequence.FadeInMs));
            fadeIn.Group.alpha = cover;
            fadeIn.Group.gameObject.SetActive(cover > 0);
            if (cover > 0) fadeIn.Draw(clock - sceneStart, clear);
            DrawRows(now);
            DrawScore(now);
            DrawGauge(now);
            DrawHighScore(now);
            if (view.scoreRank != null)
                view.scoreRank.Show(Sequence.RankAtMs.HasValue && now >= Sequence.RankAtMs.Value
                    ? ScoreRank.FromScore(Result.Score) : 0, Result.Difficulty,
                    Sequence.Skipped ? -1 : (now - Sequence.RankAtMs.GetValueOrDefault()) / 1000);
            DrawCrown(now);
            DrawMessage(now);
        }

        void HandleInput()
        {
            bool don = InputManager.GetKeyDown(InputKey.LeftDon) || InputManager.GetKeyDown(InputKey.RightDon) || InputManager.GetKeyDown(InputKey.Confirm);
            if (don) Don();
        }

        // ResultScreen::handle_input: a don press skips the reveal, or leaves once it has settled.
        public void Don()
        {
            if (IsLeaving || switcher.IsInputBlocked || revealStart < 0) return;
            if (Sequence.RevealEndMs <= 0)
            {
                if (!Sequence.Skip()) return;
                sfx.PlayAudioOneShot(this.don);
                loop.StopAudio();
                sfx.PlayAudioOneShot(donBig);
                return;
            }
            if (!Sequence.CanAdvance) return;
            sfx.PlayAudioOneShot(this.don);
            Leave();
        }

        void Leave()
        {
            if (IsLeaving) return;
            IsLeaving = true;
            switcher.ReturnToMenu();
        }

        void Play(ResultCue cue, int row, double clock)
        {
            switch (cue)
            {
                case ResultCue.CountLoopStart: loop.clip = countLoop; loop.loop = true; loop.PlayAudio(); break;
                case ResultCue.CountLoopStop: loop.StopAudio(); break;
                case ResultCue.AchieveSoul: sfx.PlayAudioOneShot(achieve); break;
                case ResultCue.RowLanded: sfx.PlayAudioOneShot(countStop); break;
                case ResultCue.ScoreLanded: sfx.PlayAudioOneShot(donBig); break;
                case ResultCue.HighScore: PlayVoice(highScoreVoice); break;
                case ResultCue.ScoreRank: sfx.PlayAudioOneShot(rankSound); break;
                case ResultCue.Crown:
                    var crown = Result.ResultCrown;
                    sfx.PlayAudioOneShot(crown == Crown.DonderfulCombo ? crownRainbow : crown == Crown.FullCombo ? crownGold : crownSilver);
                    if (crown == Crown.FullCombo || crown == Crown.DonderfulCombo) PlayVoice(fullComboVoice);
                    break;
                case ResultCue.Message: PlayVoice(messageVoices[(int)Result.Message]); break;
                case ResultCue.SuccessBackground: successAt = clock; sfx.PlayAudioOneShot(atmosClear); break;
            }
        }

        void PlayVoice(AudioClip clip)
        {
            if (clip == null) return;
            voice.StopAudio();
            voice.clip = clip;
            voice.PlayAudio();
        }

        // ---------------------------------------------------------------- drawing

        static float Pop(LumenClip clip, string track, double? landMs, double now)
        {
            if (!landMs.HasValue) return 1;
            double f = clip.First + (now - landMs.Value) * 0.06;
            return f > clip.Last ? 1 : (float)clip.Get(track, f, "sx", 1);
        }

        void DrawRows(double now)
        {
            int[] values = { Result.Good, Result.Ok, Result.Bad, Result.Rolls, Result.MaxCombo };
            for (int row = 0; row < values.Length; row++)
            {
                bool landed = Sequence.RowsLanded > row;
                float scale = landed ? Pop(judgePop, "#147@0", Sequence.RowLandMs[row], now) : 1;
                Digits(judgeImages[row], judgeDigits, landed ? values[row] : -1, scale, 0);
            }
        }

        void DrawScore(double now)
        {
            bool shown = Sequence.FadeInFinished && Sequence.ScoreLandMs.HasValue;
            float scale = shown ? Pop(scorePop, "#225@0", Sequence.ScoreLandMs, now) : 1;
            // total_score_back_mc (outlines, frames 0..9) under total_score_mc (fills, 10..19).
            Digits(scoreOutline, scoreDigits, shown ? Result.Score : -1, scale, 0);
            Digits(scoreFill, scoreDigits, shown ? Result.Score : -1, scale, 10);
        }

        static void Digits(Image[] images, Sprite[] sprites, int value, float scale, int offset)
        {
            string text = value < 0 ? "" : value.ToString();
            for (int i = 0; i < images.Length; i++)
            {
                bool on = i < text.Length;
                images[i].enabled = on;
                if (!on) continue;
                images[i].sprite = sprites[text[text.Length - 1 - i] - '0' + offset];
                images[i].rectTransform.localScale = new Vector3(scale, scale, 1);
            }
        }

        void DrawGauge(double now)
        {
            bool started = Sequence.GaugeStarted;
            foreach (var image in new[] { unfilledImage, overlayImage, captionImage, soulImage })
                image.enabled = started;
            if (!started)
            {
                foreach (var image in new[] { barImage, transitionImage, topImage, bottomImage, fireImage, sheenImage, rainbowImages[0], rainbowImages[1] }) image.enabled = false;
                return;
            }
            int shown = Sequence.GaugeShown, clearCell = ClearCell[art];
            bool cleared = Result.GaugeState != GaugeState.Failed, full = Result.GaugeState == GaugeState.Full && shown >= PlayResult.GaugeCells;
            barImage.enabled = !full && shown > 0;
            if (barImage.enabled) Width(barImage, Math.Min(shown, clearCell - 1) * GaugeUnit);
            transitionImage.enabled = !full && shown >= clearCell - 1 && cleared;
            topImage.enabled = bottomImage.enabled = !full && shown > clearCell && cleared;
            if (topImage.enabled) { Width(topImage, (shown - clearCell) * GaugeUnit); Width(bottomImage, (shown - clearCell) * GaugeUnit); }
            DrawRainbow(full, now);

            bool lit = shown >= clearCell - 1 && cleared;
            captionImage.sprite = lit ? clearCaption : clearCaptionDark;
            soulImage.sprite = lit ? soul : soulDark;
            // tamashii_gage fever: flame cel, fade-in and sheen sampled from anim/fever_fire.
            fireImage.enabled = sheenImage.enabled = false;
            if (lit && full && Sequence.RainbowStartMs.HasValue)
            {
                double start = fireClip.Label("fever_start").GetValueOrDefault(1), loopAt = fireClip.Label("fever").GetValueOrDefault(10), end = fireClip.Label("fever_end").GetValueOrDefault(34);
                double f = start + (now - Sequence.RainbowStartMs.Value) * 0.06;
                if (f >= loopAt) f = loopAt + (f - loopAt) % (end - loopAt);
                int fi = (int)Math.Floor(f);
                foreach (var (track, frame) in FireLeaves)
                {
                    if (!fireClip.Window(track, out double first, out double last) || fi < first || fi > last) continue;
                    fireImage.sprite = soulFire[frame];
                    fireImage.Alpha((float)fireClip.Get(track, f, "a", 1));
                    double sheen = fi < loopAt ? fireClip.Get("#39@2", f, "a", 0)
                        : (fi - loopAt) % 4 < 2 ? fireClip.Get("#39@2", (loopAt + end) / 2, "a", 0.5) : 0;
                    sheenImage.Alpha((float)sheen);
                    break;
                }
            }
        }

        void DrawRainbow(bool full, double now)
        {
            rainbowImages[0].enabled = rainbowImages[1].enabled = false;
            if (!full || !Sequence.RainbowStartMs.HasValue) return;
            // anim/gauge_rainbow: each cel holds while the next fades in over 5 frames.
            double length = rainbowClip.Last - rainbowClip.First;
            double f = (now - Sequence.RainbowStartMs.Value) * 0.06 % Math.Max(1, length);
            int fi = (int)Math.Floor(f), used = 0;
            foreach (var (track, frame) in RainbowLeaves)
            {
                if (used == 2 || !rainbowClip.Window(track, out double first, out double last) || fi < first || fi > last) continue;
                float alpha = (float)rainbowClip.Get(track, f, "a", 0);
                if (alpha <= 0) continue;
                rainbowImages[used].sprite = rainbow[art * 8 + frame];
                rainbowImages[used].Alpha(alpha);
                used++;
            }
        }

        static void Width(Image image, float width)
            => image.rectTransform.sizeDelta = new Vector2(width, image.rectTransform.sizeDelta.y);

        void DrawHighScore(double now)
        {
            highScoreGroup.gameObject.SetActive(Sequence.HighScoreAtMs.HasValue);
            if (!Sequence.HighScoreAtMs.HasValue) return;
            // best_score_mc 'start': drop in, rebound, overshoot and settle (track #196@0).
            double start = highScoreClip.Label("start").GetValueOrDefault(5);
            double f = Math.Min(start + (now - Sequence.HighScoreAtMs.Value) * 0.06, Math.Max(start, highScoreClip.Last));
            highScoreGroup.anchoredPosition = highScoreBase - new Vector2(0, (float)highScoreClip.Get("#196@0", f, "ty", 0));
            highScoreAlpha.alpha = (float)highScoreClip.Get("#196@0", f, "a", 1);
        }

        void DrawCrown(double now)
        {
            var crown = Result.ResultCrown;
            bool show = Sequence.CrownAtMs.HasValue && now >= Sequence.CrownAtMs.Value && Result.GaugeState != GaugeState.Failed;
            foreach (var image in new[] { crownImage, crownGhost, burstA, burstB, stars, shine }) image.enabled = false;
            if (!show) return;
            // crown_mc, sampled from its 'start' label on the 60 fps container clock.
            double t = (now - Sequence.CrownAtMs.Value) * 0.06;
            double f = crownClip.Label("start").GetValueOrDefault(5) + t, end = crownClip.Label("end").GetValueOrDefault(120);
            crownImage.sprite = crowns[Math.Max(0, (int)crown - 1)];
            Show(crownImage, crownClip.Get("crown", f, "sx", 1), crownClip.Get("crown", f, "a", 1));
            if (t >= 13 && t <= 42) Show(crownGhost, crownClip.Get("crown_effect", f, "sx", 1), crownClip.Get("crown_effect", f, "a", 0));
            if (t >= 13 && t <= 49)
            {
                double a = crownClip.Get("#38@1", f, "a", 0);
                Show(burstA, crownClip.Get("#38@1", f, "sx", 0.1), a);
                Show(burstB, crownClip.Get("#38@0", f, "sx", 0.1), a);
            }
            if (t >= 17 && t <= 119)
            {
                Show(stars, crownClip.Get("#50@4", f, "sx", 0.5), Math.Min(1, Math.Max(0, crownClip.Get("#50@4", f, "a", 1))));
                stars.rectTransform.anchoredPosition = crownBase + new Vector2((float)crownClip.Get("#50@4", f, "tx", 30.2), -(float)crownClip.Get("#50@4", f, "ty", -2.2));
            }
            if (f >= end - 5)
            {
                // crown_loop: a three-step shine sweep, then 171 idle frames.
                double k = (f - (end - 5)) % Math.Max(1, crownLoop.Last - crownLoop.First + 1);
                foreach (var (track, frame) in ShineLeaves)
                    if (crownLoop.Window(track, out double first, out double last) && Math.Floor(k) >= first && Math.Floor(k) <= last)
                    {
                        shine.sprite = gleam[frame];
                        Show(shine, 1, 1);
                    }
            }
        }

        static void Show(Image image, double scale, double alpha)
        {
            image.rectTransform.localScale = new Vector3((float)scale, (float)scale, 1);
            image.Alpha((float)alpha);
        }

        void DrawMessage(double now)
        {
            bool show = Sequence.RevealEndMs > 0;
            messageImage.enabled = messageText.enabled = show;
            if (!show) return;
            // msg_L_mc: half size to a 1.2 overshoot in 66.7 ms, settled at 150 ms.
            double f = Math.Min(messageClip.Label("success").GetValueOrDefault(35) + (now - Sequence.RevealEndMs) * 0.06, messageClip.Last);
            float scale = (float)messageClip.Get("#277@0", f, "sx", 1), alpha = (float)messageClip.Get("#277@0", f, "a", 1);
            Show(messageImage, scale, alpha);
            messageText.rectTransform.localScale = new Vector3(scale, scale, 1);
            messageText.alpha = alpha;
        }

        // ---------------------------------------------------------------- binding

        // Fills the saved screen with this run. The clear marks are saved for view.gaugeArt and
        // shifted by the clear-cell difference for the run's gauge art.
        void Bind()
        {
            background = new ResultBackground(view.background, bgClip, Clip(fujiTimeline));
            fadeIn = new ResultBackground(view.fadeIn, bgClip, Clip(fujiTimeline));
            view.songTitle.text = Result.Title;
            view.songTitle.Squeeze(view.songTitle.rectTransform.sizeDelta.x);
            view.songNumber.text = Math.Max(1, switcher.SongsPlayed) + "曲目";

            successImage = view.success;
            view.difficulty.sprite = difficulties[Math.Min(Math.Max((int)Result.Difficulty, 0), difficulties.Length - 1)];
            judgeImages = new Image[view.judgeRows.Length][];
            for (int row = 0; row < judgeImages.Length; row++) judgeImages[row] = view.judgeRows[row].digits;
            scoreOutline = view.scoreOutline;
            scoreFill = view.scoreFill;

            highScoreGroup = view.highScore;
            highScoreAlpha = view.highScoreGroup;
            highScoreBase = highScoreGroup.anchoredPosition;
            highScoreNumber = view.highScoreDigits;
            string difference = Result.ScoreDifference.ToString();
            for (int i = 0; i < highScoreNumber.Length; i++)
            {
                highScoreNumber[i].enabled = i < difference.Length;
                if (i < difference.Length) highScoreNumber[i].sprite = highScoreDigits[difference[difference.Length - 1 - i] - '0'];
            }
            highScoreGroup.gameObject.SetActive(false);

            crownImage = view.crown; crownGhost = view.crownFade;
            burstA = view.burstA; burstB = view.burstB; stars = view.stars; shine = view.shine;
            crownBase = crownImage.rectTransform.anchoredPosition;
            messageImage = view.message;
            messageImage.sprite = messages[(int)Result.Message];
            messageText = view.messageText;
            messageText.text = MessageTexts[(int)Result.Message];

            unfilledImage = view.unfilled;
            unfilledImage.sprite = unfilled[art];
            overlayImage = view.overlay;
            overlayImage.sprite = overlays[art];
            barImage = view.bar;
            transitionImage = view.clearTransition;
            topImage = view.clearTop;
            bottomImage = view.clearBottom;
            int savedArt = Math.Min(Math.Max(view.gaugeArt, 0), 2);
            float shift = (ClearCell[art] - ClearCell[savedArt]) * GaugeUnit;
            foreach (var image in new[] { transitionImage, topImage, bottomImage })
                image.rectTransform.anchoredPosition += new Vector2(shift, 0);
            rainbowImages[0] = view.rainbow[0];
            rainbowImages[1] = view.rainbow[1];
            captionImage = view.clearCaption;
            captionImage.rectTransform.anchoredPosition += new Vector2(ClearCaptionX[art] - ClearCaptionX[savedArt], 0);
            fireImage = view.soulFire;
            soulImage = view.soul;
            sheenImage = view.soulSheen;

            // ResultScreen::draw_overlay: coin_overlay's credit line (result shows no chip or invite), over the wipe.
            if (overlay != null) Coins = new CoinOverlayView(overlay, view.freePlay, null, null, null, null);
            view.touchRelay.Clicked = Don;
        }
    }
}
