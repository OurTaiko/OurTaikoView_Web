using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // The kusudama ball (YataiDON's KusudamaCounter with Nijiiro's game/kusudama art), drawn over the whole
    // screen once for every seat. Like the original it appears at the ball's first hit: it drops in,
    // 連打!! rises under it, and the remaining count shows once it has settled. Each hit stretches the
    // count and swells the ball; the breaking hit opens it (four frames), 連打!! fades and the ball
    // fades after 666 ms. A ball whose rolls all end unbroken disappears at once. Positions are the
    // texture.json points on the 1920x1080 stage (y down); the motion is KusudamaMotion on the song clock.
    [RequireComponent(typeof(RectTransform))]
    public sealed class KusudamaView : MonoBehaviour
    {
        // texture.json: kusudama (-60, -1527) 2040x1872, drawn centred for its scale; renda (780, 420); counter
        // (960, 219) 240x312 frames, kusudama_counter_margin 240 apart around x 960.
        const float BallX = -60, BallY = -1527, BallWidth = 2040, BallHeight = 1872;
        const float RendaX = 780, RendaY = 420, CounterX = 960, CounterY = 219, DigitWidth = 240, DigitHeight = 312, Margin = 240;

        [Tooltip("game/kusudama/kusudama frames 0 (whole) to 4 (open).")]
        public Sprite[] ballFrames;
        [Tooltip("game/kusudama/counter frames 0-9.")]
        public Sprite[] digits;
        public Image ball, renda;
        [Tooltip("The digits are laid out under this stage-sized rect.")]
        public RectTransform number;

        readonly List<Image> digitImages = new List<Image>();
        readonly List<PlaySession> sessions = new List<PlaySession>();

        public Kusudama Ball { get; private set; }
        public bool IsShowing => Ball != null;
        // Shown count: the hits left, 0 once broken.
        public int Remaining => Ball == null ? 0 : Ball.Remaining;

        public void ResetDisplay()
        {
            Ball = null;
            gameObject.SetActive(false);
        }

        // A seat's don on `kusudama`: its first hit brings the ball in. Drawn by the next ShowTime, timed by
        // the ball's own hit times.
        public void RecordHit(Kusudama kusudama)
        {
            if (kusudama == Ball) return;
            Ball = kusudama;
            gameObject.SetActive(true);
        }

        public void ShowTime(double time)
        {
            if (Ball == null) return;
            bool broken = Ball.IsBroken;
            double popped = broken ? (time - Ball.BrokenAt) * 1000 : double.NaN;
            if (broken ? popped >= KusudamaMotion.PoppedMs : !StillRolling(time)) { ResetDisplay(); return; }
            double shown = (time - Ball.FirstHitAt) * 1000, counted = (time - Ball.LastHitAt) * 1000;

            float scale = (float)KusudamaMotion.Breathing(counted);
            ball.sprite = ballFrames[KusudamaMotion.OpenFrame(popped)];
            var rect = ball.rectTransform;
            rect.sizeDelta = new Vector2(BallWidth * scale, BallHeight * scale);
            rect.TopLeft(BallX + BallWidth / 2 * (1 - scale), BallY + BallHeight / 2 * (1 - scale) + (float)KusudamaMotion.Drop(shown));
            ball.Alpha((float)KusudamaMotion.Alpha(popped));

            renda.rectTransform.TopLeft(RendaX, RendaY + (float)KusudamaMotion.RendaMove(shown));
            renda.Alpha((float)KusudamaMotion.RendaAlpha(shown, popped));

            int remaining = Ball.Remaining;
            bool counting = shown >= KusudamaMotion.DropMs && !broken && remaining > 0;
            ShowNumber(counting ? remaining.ToString(System.Globalization.CultureInfo.InvariantCulture) : "",
                (float)KusudamaMotion.Stretch(counted));
        }

        // Any seat still rolling on the ball; one seat ending its roll early does not take it away.
        bool StillRolling(double time)
        {
            sessions.Clear();
            foreach (var (session, _) in Ball.Members) if (!sessions.Contains(session)) sessions.Add(session);
            foreach (var session in sessions) if (Ball.IsRolling(session, time - session.JudgeOffset)) return true;
            return false;
        }

        // The digits grow `stretch` taller from their bottom edge (draw_texture y = -stretch, y2 = +stretch).
        void ShowNumber(string text, float stretch)
        {
            while (digitImages.Count < text.Length) digitImages.Add(SkinUi.Image("Digit" + digitImages.Count, number, null));
            float left = CounterX - text.Length * Margin / 2;
            for (int i = 0; i < digitImages.Count; i++)
            {
                var image = digitImages[i];
                image.enabled = i < text.Length;
                if (!image.enabled) continue;
                image.sprite = digits[text[i] - '0'];
                image.rectTransform.sizeDelta = new Vector2(DigitWidth, DigitHeight + stretch);
                image.rectTransform.TopLeft(left + i * Margin, CounterY - stretch);
            }
        }
    }
}
