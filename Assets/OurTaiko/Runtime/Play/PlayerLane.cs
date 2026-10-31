using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OurTaiko
{
    // One seat's play (YataiDON's Player): its session, drum, judgment effects and HUD on its own
    // LaneViews. PlayScene owns the shared song clock, music, pause and title, and drives one lane
    // (single play, practice) or two (DoublePlayScene). Each lane reads only its seat's drum, so the
    // input lock (one judged press per frame) holds per player and the players never block each other.
    public sealed class PlayerLane
    {
        public int Seat { get; }
        public LaneViews Views { get; }
        public PlayerInput Input => InputManager.Players[Seat];
        public PlaySession Session { get; internal set; }
        public bool AutoPlay { get; }
        public PlayOptions Options { get; }
        // Every judged drum press of this play, sent with an online score (scoreReplayVersion 1).
        public Online.PlayRecord Record { get; }
        public PlayResult Result { get; internal set; }
        // Drum sounds started so far.
        public int DrumSounds { get; private set; }
        public double RenderedTime => renderer?.RenderedTime ?? 0;
        // 音色: hit_sounds/<neiro>/don.ogg and ka.ogg; 無音 leaves both empty.
        public AudioClip Don, Ka;

        readonly PlayScene scene;
        NoteLaneRenderer renderer;
        // Drum presses whose sound already played while they wait out the input delay.
        readonly List<InputPress> heard = new List<InputPress>();
        bool hitKa, nextAutoRight = true;
        double feedbackTime = -10;
        readonly double[] flashedAt = { -10, -10, -10, -10 };
        ClipSampler[] flashClips;
        ClipSampler judgmentFade;
        int lastCombo;

        public PlayerLane(PlayScene scene, int seat, LaneViews views, PlayOptions options, bool autoPlay, Online.PlayRecord record)
        {
            this.scene = scene; Seat = seat; Views = views; Options = options; AutoPlay = autoPlay; Record = record;
        }

        public RectTransform NoteRoot(int index) => renderer.NoteRoot(index);
        public RectTransform MojiRoot(int index) => renderer.MojiRoot(index);
        public RectTransform BarRoot(int index) => renderer.BarRoot(index);

        double ChartTime => scene.ChartTimeNow;

        public void Prepare(PlaySession session)
        {
            Session = session;
            var v = Views;
            v.combo.gameObject.SetActive(false); v.comboAnnounce.Hide();
            if (v.modifierBadges != null) v.modifierBadges.Show(Options, AutoPlay);
            ShowDifficulty(session.Chart.Course);
            Don = scene.don; Ka = scene.ka;
            if (scene.hitSounds != null) scene.hitSounds.TryGet(Options.neiro, out Don, out Ka);
            v.balloonCounter.ResetDisplay();
            v.drumrollCounter.ResetDisplay();
            v.soulGauge.Initialize(Session.ClearThreshold);
            PutSoulGaugeUnderNotes();
            Session.Judged += OnJudged;
            if (v.branchLane != null) v.branchLane.Initialize(Session.Chart.Branches.Count > 0);
            renderer = new NoteLaneRenderer(new NoteLaneRenderer.Assets
            {
                NoteLayer = v.noteLayer, BarLayer = v.barLayer, MojiLayer = v.mojiLayer,
                NoteSprites = scene.noteSprites, AlternateNoteSprites = scene.alternateNoteSprites,
                RollBodySprites = scene.rollBodySprites, RollTailSprites = scene.rollTailSprites, MojiSprites = scene.mojiSprites,
                BalloonTailSprite = scene.balloonTailSprite, MojiRollSprite = scene.mojiRollSprite,
            }, v.balloonCounter);
            renderer.Prepare(Session);
            scene.hitAudio.PrepareAudioEffects(Don, Ka);
            scene.hitAudio.PrepareAudioEffects(v.comboAnnounce.voices);
            UpdateHud();
        }

        public void Release()
        {
            if (Session != null) Session.Judged -= OnJudged;
        }

        // Swaps in another session for the same lane (practice attempts).
        public void Replace(PlaySession session)
        {
            if (Session != null) Session.Judged -= OnJudged;
            Session = session;
            if (Session != null) Session.Judged += OnJudged;
        }

        void ShowDifficulty(string course)
        {
            var image = Views.laneDifficulty;
            var sprites = scene.laneDifficultySprites;
            if (image == null || sprites == null || sprites.Length == 0) return;
            int index = (int)(SongInfo.DifficultyOf(course) ?? Difficulty.Oni);
            image.sprite = sprites[Math.Min(index, sprites.Length - 1)];
        }

        // Judges this frame and draws the branch that is shown.
        public void Advance(double time)
        {
            Session.Advance(time, AutoPlay);
            UpdateBranchLane(time);
            if (!AutoPlay) { SoundDrumLeads(); HitFirstDrumPress(); }
        }

        // Presentation can keep running after the result is captured, without advancing judgment.
        public void ShowVisuals(double time, double renderTime, bool preview, bool hideSkipped)
        {
            var v = Views;
            v.balloonCounter.ShowTime(time);
            v.drumrollCounter.ShowTime(time);
            RenderNotes(renderTime, preview, hideSkipped);
            v.soulGauge.ShowTime(time);
            if (v.noteArcs != null) v.noteArcs.ShowTime(time);
            v.combo.ShowTime(time);
            v.comboAnnounce.ShowTime(time);
            judgmentFade ??= v.judgment.GetComponent<ClipSampler>();
            judgmentFade.Sample(Math.Min(GameTimeline.FrameTime - feedbackTime, judgmentFade.clip.length));
            v.hitFace.ShowTime(time);
            v.hitRing.ShowTime(time);
            for (int i = 0; i < v.drumFlashes.Length; i++) ShowFlash(i);
        }

        // A paused practice previews its routes; while it plays (or rewinds) it hides what the
        // attempt skipped, and browsing measures keeps past notes.
        public void RenderNotes(double time, bool preview, bool hideSkipped)
        {
            ShiftJudge(time);
            if (renderer.Render(Session, time, preview, hideSkipped) && Views.gogo != null) Views.gogo.ShowTime(Session, time, preview);
        }

        // Input mutex: a frame judges only this seat's earliest drum press; later ones in the same frame are
        // dropped, except one of the other colour when the first left a Kadon waiting for it.
        void HitFirstDrumPress()
        {
            bool first = true, second = false;
            foreach (var press in Input.PressesThisFrame)
            {
                bool wasHeard = Forget(press);
                bool ka = press.Key.IsKa();
                if (first) first = false;
                else if (second || Session == null || !Session.AwaitsKadon(ka, ChartTime)) continue;
                else second = true;
                Hit(ka, press.Key.IsRight(), !wasHeard);
            }
        }
        // The drum sound does not wait for the input delay: a press that will be judged is heard in the
        // frame it arrives, and only its judgment and effects come later.
        void SoundDrumLeads()
        {
            foreach (var press in Input.DrumLeadsThisFrame)
            {
                PlayDrum(press.Key.IsKa());
                heard.Add(press);
            }
        }
        bool Forget(InputPress press)
        {
            for (int i = 0; i < heard.Count; i++)
            {
                if (heard[i].Key != press.Key || heard[i].Time != press.Time) continue;
                heard.RemoveAt(i);
                return true;
            }
            return false;
        }
        // A pause drops the presses still waiting out the input delay, so their sounds are forgotten too.
        public void ForgetHeard() => heard.Clear();

        void PlayDrum(bool isKa)
        {
            var clip = isKa ? Ka : Don;
            if (clip != null) scene.hitAudio.PlayAudioOneShot(clip, AudioGroup.Drum);
            DrumSounds++;
        }

        public void Hit(bool isKa, bool right, bool sound = true)
        {
            if (Session == null || !scene.AcceptsHits || AutoPlay) return;
            if (sound) PlayDrum(isKa);
            Feedback(isKa, right);
            hitKa = isKa;
            double time = ChartTime;
            Record.Inputs.Add(((time - scene.JudgeOffset) * 1000, Online.PlayRecord.TypeOf(isKa, right)));
            Session.Hit(isKa, time);
        }
        void Feedback(bool isKa, bool right)
        {
            int flash = (isKa ? 2 : 0) + (right ? 1 : 0);
            flashedAt[flash] = GameTimeline.FrameTime;
            ShowFlash(flash);
        }
        // Real time since that drum's last hit; the clip ends switched off.
        void ShowFlash(int index)
        {
            flashClips ??= Array.ConvertAll(Views.drumFlashes, flash => flash.GetComponent<ClipSampler>());
            var sampler = flashClips[index];
            sampler.Sample(Math.Min(GameTimeline.FrameTime - flashedAt[index], sampler.clip.length));
        }

        void OnJudged(int index, Judgment result)
        {
            var v = Views;
            if (result != Judgment.Roll)
                v.soulGauge.SetPoints(Session.GaugePoints, ChartTime);
            if (AutoPlay)
            {
                // Alternate actual strikes, including every hit within the same long note. A Kadon
                // strikes don and ka together with opposite hands.
                var struck = Session.Chart.Notes[index];
                PlayDrum(struck.IsKa);
                Feedback(struck.IsKa, nextAutoRight);
                if (struck.IsKadon) { PlayDrum(true); Feedback(true, !nextAutoRight); }
                nextAutoRight = !nextAutoRight;
            }
            SpawnArc(index, result);
            var judged = Session.Chart.Notes[index];
            bool big = judged.IsBigNote || judged.IsKadon;
            double judgedAt = ChartTime;
            v.hitFace.Play(result, big, judgedAt);
            v.hitRing.Play(result, big, judgedAt);
            if (result != Judgment.Roll)
            {
                // Long-note hits must not restart the previous normal judgment's text animation.
                feedbackTime = GameTimeline.FrameTime;
                v.judgment.sprite = scene.judgmentSprites[(int)result - 1];
            }
            else if (judged.Kind == NoteKind.Balloon)
            {
                v.balloonCounter.RecordHit(index, judged.BalloonHits, Session.LongHits[index], judged.EndTime + scene.JudgeOffset,
                    ChartTime);
                if (Session.LongHits[index] == judged.BalloonHits) scene.hitAudio.PlayAudioOneShot(scene.balloonPop);
            }
            else if (judged.Kind == NoteKind.Kusudama && Session.KusudamaOf(index) is Kusudama ball)
            {
                if (scene.kusudama != null) scene.kusudama.RecordHit(ball);
                if (ball.IsBroken && ball.BrokenBy == Session) scene.hitAudio.PlayAudioOneShot(scene.kusudamaPop);
            }
            else if (judged.IsDrumroll)
                v.drumrollCounter.RecordHit(index, Session.LongHits[index], judgedAt);
            UpdateHud();
        }

        // Good/ok notes 1-4 and Kadon fly as themselves; popped balloons fly as big don. Roll hits use the roll's size
        // and the struck drum's colour (autoplay rolls with don). Kusudama never flies.
        void SpawnArc(int index, Judgment result)
        {
            if (Views.noteArcs == null) return;
            var note = Session.Chart.Notes[index];
            NoteKind kind;
            if (result == Judgment.Good || result == Judgment.Ok) kind = note.Kind;
            else if (result != Judgment.Roll || note.Kind == NoteKind.Kusudama) return;
            else if (note.Kind == NoteKind.Balloon)
            {
                if (Session.LongHits[index] != note.BalloonHits) return;
                kind = NoteKind.BigDon;
            }
            else if (note.Kind == NoteKind.BigRoll)
                kind = hitKa && !AutoPlay ? NoteKind.BigKa : NoteKind.BigDon;
            else kind = hitKa && !AutoPlay ? NoteKind.Ka : NoteKind.Don;
            // NoteArc's is_big picks the gauge burst's circle; popped balloons fly as big don.
            bool big = kind == NoteKind.BigDon || kind == NoteKind.BigKa || kind == NoteKind.Kadon;
            // The note leaves the judge where #JPOSSCROLL has it now (user request; OpenTaiko's
            // FlyingNotes start from the fixed skin point).
            var shift = JudgeShift.At(Session.Chart.JudgeMoves, ChartTime);
            Views.noteArcs.Spawn(scene.noteSprites[(int)kind], big, ChartTime, shift.X, shift.Y);
        }

        public void UpdateHud()
        {
            var v = Views;
            v.scoreCounter.Show(Session.Score, Session.KiwamiThreshold);
            if (v.judgeCounter != null) v.judgeCounter.Show(Session.Good, Session.Ok, Session.Bad, Session.Rolls);
            v.combo.Show(Session.Combo);
            // Announce at 50, then at each 100th combo, once per combo change.
            if (Session.Combo != lastCombo && (Session.Combo == 50 || Session.Combo > 0 && Session.Combo % 100 == 0))
            {
                var voice = v.comboAnnounce.Announce(Session.Combo, ChartTime);
                if (voice != null) scene.hitAudio.PlayAudioOneShot(voice, AudioGroup.Voice);
            }
            lastCombo = Session.Combo;
        }

        void UpdateBranchLane(double time)
        {
            if (Views.branchLane == null) return;
            Views.branchLane.Select(Session.DisplayBranchAt(time), time);
            Views.branchLane.ShowTime(time);
        }
        public void ShowBranchTime(double time)
        {
            if (Views.branchLane != null) Views.branchLane.ShowTime(time);
        }

        // Practice: everything this lane shows starts over at a new attempt.
        public void ResetAttempt()
        {
            var v = Views;
            Record.Inputs.Clear();
            nextAutoRight = true;
            lastCombo = 0;
            v.balloonCounter.ResetDisplay(); v.comboAnnounce.Hide();
            v.drumrollCounter.ResetDisplay();
            if (scene.kusudama != null) scene.kusudama.ResetDisplay();
            v.soulGauge.Initialize(Session.ClearThreshold);
            v.hitFace.ResetDisplay(); v.hitRing.ResetDisplay();
            if (v.noteArcs != null) v.noteArcs.ResetDisplay();
            feedbackTime = double.NegativeInfinity;
            judgmentFade ??= v.judgment.GetComponent<ClipSampler>();
            judgmentFade.Sample(judgmentFade.clip.length);
            for (int i = 0; i < flashedAt.Length; i++) { flashedAt[i] = double.NegativeInfinity; ShowFlash(i); }
            UpdateHud();
        }

        public void ResetBranchLane(double time)
        {
            if (Views.branchLane == null) return;
            Views.branchLane.Initialize(Session.Chart.Branches.Count > 0);
            Views.branchLane.SetImmediate(Session.DisplayBranchAt(time));
        }

        // OpenTaiko draws the soul gauge after the lane and before the notes (t進行描画_ゲージ, then
        // t進行描画_チップ), so notes a vertical SCROLL or #JPOSSCROLL carries up pass over it, and
        // clips the notes only at the lane's ends. The web scene draws one canvas in hierarchy order,
        // so the gauge moves into the lane before the GOGO fire (both share the lane's top-left
        // anchor), and the lane masks open vertically.
        const float Unbounded = 1e6f;
        void PutSoulGaugeUnderNotes()
        {
            var v = Views;
            var lane = v.noteLayer.parent.parent;
            if (v.soulGauge != null && v.soulGauge.transform.parent != lane)
            {
                var before = lane.Find("GogoEffects");
                if (before == null) before = v.noteLayer.parent;
                v.soulGauge.transform.SetParent(lane, true);
                v.soulGauge.transform.SetSiblingIndex(before.GetSiblingIndex());
            }
            foreach (var layer in new[] { v.noteLayer, v.barLayer, v.mojiLayer })
            {
                var mask = layer != null ? layer.GetComponentInParent<UnityEngine.UI.RectMask2D>() : null;
                if (mask != null) mask.padding = new Vector4(0, -Unbounded, 0, -Unbounded);
            }
        }

        // What #JPOSSCROLL moves with the note origin, as OpenTaiko adds GetJPOSCROLLX/Y to them:
        // the judge frame, judgment text, hit effects, GOGO fire and balloon counter, and the notes
        // flying to the soul gauge leave from the moved judge (SpawnArc). The lane and its GOGO tint stay.
        RectTransform[] judgeFollowers;
        Vector2[] judgeHomes;
        void ShiftJudge(double time)
        {
            var v = Views;
            if (judgeFollowers == null)
            {
                var lane = v.noteLayer.parent.parent;
                judgeFollowers = new[] { lane.Find("JudgeCircle") as RectTransform, v.judgment != null ? v.judgment.rectTransform : null,
                        v.hitFace != null ? (RectTransform)v.hitFace.transform : null, v.hitRing != null ? (RectTransform)v.hitRing.transform : null,
                        v.gogo != null && v.gogo.fire != null ? v.gogo.fire.rectTransform : null,
                        v.balloonCounter != null ? (RectTransform)v.balloonCounter.transform : null }
                    .Where(t => t != null).ToArray();
                judgeHomes = judgeFollowers.Select(t => t.anchoredPosition).ToArray();
            }
            var shift = JudgeShift.At(Session.Chart.JudgeMoves, time);
            var offset = new Vector2((float)shift.X, -(float)shift.Y);
            for (int i = 0; i < judgeFollowers.Length; i++) judgeFollowers[i].anchoredPosition = judgeHomes[i] + offset;
        }
    }
}
