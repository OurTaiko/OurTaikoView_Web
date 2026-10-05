using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // The Nijiiro song-loading rainbow curtain (Scripts/global/transition.lua over
    // anim/loading_song.lua, arcade loading/loading_song.nulm @ 60 fps). The close runs over
    // the song select (frames 5..55), SongLoadingScene parks it on frame 55, and the open
    // runs over the play scene (frames 60..109); each half lasts song_info_fade's 532 ms.
    public sealed class SongTransition : MonoBehaviour
    {
        public const float SceneSeconds = 0.532f, FadeDelay = 0.266f, FadeSeconds = 0.266f, FadeOutSeconds = 0.133f;
        const double FrameIn = 5, FrameStop = 55, FrameOut = 60, FrameEnd = 109, FullFrom = 35, FullTo = 78;

        // Sprite 3's eleven star placements in screen pixels: centre x, centre y, scale.
        static readonly float[,] Stars =
        {
            { 209.75f, 542.55f, 0.59998f }, { 92.35f, 283.00f, 0.47829f },
            { 154.20f, 330.75f, 0.26784f }, { 292.35f, 246.35f, 0.44998f },
            { 1259.00f, 201.85f, 0.78998f }, { 1845.70f, 228.45f, 0.37422f },
            { 1791.70f, 268.40f, 0.24998f }, { 1388.50f, 664.80f, 0.44713f },
            { 1821.20f, 423.20f, 0.56000f }, { 1472.00f, 607.70f, 0.25038f },
            { 552.60f, 628.95f, 0.74998f },
        };

        public TextAsset timeline;
        public Image rainbow, curtainLeft, curtainRight, glow, don, katsu, band, hint;
        public Image[] stars;
        public TMP_Text title, subtitle;
        [Tooltip("Online download progress and errors under the title; empty for local songs.")]
        public TMP_Text status;

        public bool IsClosed { get; private set; }
        public bool IsVisible => gameObject.activeSelf;
        public double Frame { get; private set; }
        public float InfoAlpha { get; private set; }

        LumenClip clip;
        string pendingTitle = "", pendingSubtitle = "";
        LumenClip Clip => clip ??= timeline != null ? LumenClip.Parse(timeline.text) : LumenClip.Empty;

        public void SetSong(string songTitle, string songSubtitle)
        {
            pendingTitle = songTitle ?? "";
            pendingSubtitle = songSubtitle ?? "";
            SetStatus("");
        }

        public void SetStatus(string text) => SetStatus(text, Color.white);
        public void SetStatus(string text, Color color)
        {
            if (status == null) return;
            status.text = text ?? "";
            status.color = color;
        }

        // Text layout needs an active object, so the song is applied as the curtain appears.
        void Appear()
        {
            if (gameObject.activeSelf) return;
            gameObject.SetActive(true);
            title.text = pendingTitle;
            subtitle.text = pendingSubtitle;
            // The arcade EditText boxes span the whole 1920-px stage.
            title.Squeeze(1920);
            subtitle.Squeeze(1920);
        }

        // Frame 55 with the title fully shown: what SongLoadingScene displays while it loads.
        public void Park()
        {
            Appear();
            Show(FrameStop, 1, false);
            IsClosed = true;
        }

        public void Hide()
        {
            IsClosed = false;
            SetStatus("");
            gameObject.SetActive(false);
        }

        public async Task PlayAsync(bool closing, CancellationToken cancellation)
        {
            Appear();
            IsClosed = false;
            double started = GameTimeline.FrameTime;
            while (true)
            {
                float t = Mathf.Min(SceneSeconds, (float)(GameTimeline.FrameTime - started));
                ShowTime(t, closing);
                if (t >= SceneSeconds) break;
                await Awaitable.NextFrameAsync(cancellation);
            }
            if (closing) IsClosed = true;
            else Hide();
        }

        // SongTransition:update — scene 1 maps 0..532 ms onto frames 5..55 and fades the title
        // in on song_info_fade (delay 266 + 266 ms); scene 2 maps it onto 60..109 and fades out in 133 ms.
        public void ShowTime(float seconds, bool closing)
        {
            float t = Mathf.Clamp(seconds, 0, SceneSeconds);
            if (closing) Show(FrameIn + (FrameStop - FrameIn) * t / SceneSeconds, Mathf.Clamp01((t - FadeDelay) / FadeSeconds), false);
            else Show(FrameOut + (FrameEnd - FrameOut) * t / SceneSeconds, Mathf.Clamp01(1 - t / FadeOutSeconds), true);
        }

        // SongTransition:draw_bg plus the engine's draw_song_info, in their draw order.
        public void Show(double f, float info, bool second)
        {
            Frame = f; InfoAlpha = info;
            var c = Clip;

            // 1. Two curved curtains, replaced by the flat full-screen rainbow while they meet.
            bool full = f >= FullFrom && f <= FullTo;
            rainbow.enabled = full;
            curtainLeft.enabled = curtainRight.enabled = !full;
            if (!full)
            {
                Curtain(curtainLeft, c.Get("#47@0", f, "tx", -240), c.Get("#47@0", f, "sx", 0.5));
                Curtain(curtainRight, c.Get("#49@1", f, "tx", 2160), c.Get("#49@1", f, "sx", 0.5));
            }

            // 2. The additive glow along the bottom.
            glow.Alpha(Visible(c.Get("#55@3", f, "a", 0)));

            // 3. The eleven sparkles share one group alpha.
            float starAlpha = Visible(c.Get("#3@4", f, "a", 0));
            for (int i = 0; i < stars.Length; i++)
            {
                stars[i].Alpha(starAlpha);
                float size = 128 * Stars[i, 2];
                stars[i].rectTransform.sizeDelta = new Vector2(size, size);
                stars[i].rectTransform.Center(Stars[i, 0], Stars[i, 1]);
            }

            // 4. Don / katsu on their arc, 560x560 around the track position.
            Character(don, "don", f, 160);
            Character(katsu, "katsu", f, 1760);

            // 5. The title band opens (sx 0.25 -> 1) in scene 1 and squashes shut (sx -> 1.5, sy -> 0.1) in scene 2.
            float bandWidth = 1600 * (second ? 1.5f - 0.5f * info : 0.25f + 0.75f * info);
            float bandHeight = 256 * (second ? 0.1f + 0.9f * info : 1);
            band.rectTransform.sizeDelta = new Vector2(bandWidth, bandHeight);
            band.rectTransform.Center(960, 400);
            band.Alpha(Visible(info));

            // 6. The baked 「ゲームのヒント」 block on the same curve. The 演奏スキップON badge only
            // shows when the skip feature armed, which it never does here (the option is greyed out).
            hint.Alpha(Visible(info));

            // draw_song_info: title / subtitle over everything, centred at skin y - rainbow_up.
            title.alpha = subtitle.alpha = Visible(info);
            title.enabled = subtitle.enabled = info > 0.002f;
        }

        static float Visible(double alpha) => alpha > 0.002 ? (float)alpha : 0;

        static void Curtain(Image image, double x, double scale)
        {
            image.rectTransform.sizeDelta = new Vector2((float)(960 * scale), 1080);
            image.rectTransform.Center((float)x, 540);
        }

        void Character(Image image, string track, double f, double x)
        {
            var c = Clip;
            image.Alpha(Visible(c.Get(track, f, "a", 1)));
            image.rectTransform.Center((float)c.Get(track, f, "tx", x), (float)c.Get(track, f, "ty", 786));
        }
    }
}
