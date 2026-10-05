using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // Nijiiro animated 1P result backdrop (Scripts/result/result_bg_layers.lua): two sky halves,
    // Mt Fuji and eleven drifting cloud bars on the exported 720-frame loop, the header band on top,
    // and the clear variant cross-faded in by `clear` (0..1). The images are saved in the scene
    // (ResultView.BackgroundView) at their frame-0 places; the timelines move them from there.
    public sealed class ResultBackground
    {
        const double Loop = 720, MsToFrame = 0.06;
        public const float FujiX = 583, FujiY = 280, FujiClearX = 584.65f, FujiHeight = 800;
        public const string FujiTrack = "huji_1p_l_mc/#84@0";

        // cloud index, y, scale, mirrored, track, frame-0 tx, frame-0 alpha. A negative x-scale
        // makes the arcade tx the sprite's right edge, which with the top-left pivot is its x.
        public static readonly (int Cloud, float Y, float Scale, bool Mirror, string Track, float X, float A)[] Layers = {
            (0, 162.60f, 1.0f, false, "#67@3", 1705.70f, 0),
            (1, 905.25f, 1.0f, false, "#69@4", 1240.00f, 0),
            (1, 934.00f, 1.2f, true, "#69@5", 1106.30f, 0),
            (2, 622.00f, 1.0f, false, "#71@6", 1382.90f, 1),
            (3, 873.25f, 1.0f, false, "#73@7", -272.10f, 1),
            (4, 873.25f, 1.0f, false, "#75@8", 668.65f, 0),
            (5, 595.25f, 1.0f, false, "#77@9", 468.00f, 0),
            (6, -17.70f, 1.0f, false, "#79@10", 1645.00f, 1),
            (2, 75.10f, 0.6f, true, "#71@11", 346.40f, 0),
            (6, 214.30f, 1.0f, false, "#79@12", 767.80f, 1),
            (7, 400.75f, 1.0f, false, "#81@13", -190.35f, 0),
        };

        readonly LumenClip clip, fuji;
        readonly Image[] sky, skyClear, clouds, cloudsClear;
        readonly Image fujiBase, fujiClear;
        readonly Vector2[] cloudBase;
        readonly Vector2 fujiClearBase;
        readonly float fujiClearHeight, fujiTy0, fujiSy0;
        double? fujiStart;
        public CanvasGroup Group { get; }

        public ResultBackground(ResultView.BackgroundView view, LumenClip clip, LumenClip fuji)
        {
            this.clip = clip; this.fuji = fuji;
            Group = view.group;
            Group.gameObject.SetActive(true);
            sky = view.sky; skyClear = view.skyClear;
            fujiBase = view.fuji; fujiClear = view.fujiClear;
            clouds = view.clouds; cloudsClear = view.cloudsClear;
            cloudBase = new Vector2[clouds.Length];
            for (int i = 0; i < clouds.Length; i++)
                cloudBase[i] = clouds[i].rectTransform.anchoredPosition - Default(i);
            fujiTy0 = (float)fuji.Get(FujiTrack, 0, "ty", FujiY);
            fujiSy0 = (float)fuji.Get(FujiTrack, 0, "sy", 1);
            fujiClearBase = fujiClear.rectTransform.anchoredPosition;
            fujiClearHeight = fujiClear.rectTransform.sizeDelta.y;
        }

        // A cloud's frame-0 place, where the scene saves it.
        public static Vector2 Default(int layer) => new Vector2(Layers[layer].X, -Layers[layer].Y);

        public void Draw(double nowMs, float clear)
        {
            double frame = nowMs * MsToFrame % Loop;
            for (int i = 0; i < sky.Length; i++) sky[i].Alpha(1);
            for (int i = 0; i < skyClear.Length; i++) skyClear[i].Alpha(clear);
            fujiBase.Alpha(1 - clear);
            // huji_1p_l_mc: a one-shot squash-and-stretch from the frame the clear state starts,
            // then parked on its last row.
            if (clear <= 0) fujiStart = null;
            else fujiStart ??= nowMs;
            double fujiFrame = fujiStart.HasValue ? (nowMs - fujiStart.Value) * MsToFrame : 0;
            float ty = (float)fuji.Get(FujiTrack, fujiFrame, "ty", FujiY), sy = (float)fuji.Get(FujiTrack, fujiFrame, "sy", 1);
            fujiClear.rectTransform.sizeDelta = new Vector2(fujiClear.rectTransform.sizeDelta.x, fujiClearHeight * sy / fujiSy0);
            fujiClear.rectTransform.anchoredPosition = fujiClearBase - new Vector2(0, ty - fujiTy0);
            fujiClear.Alpha(clear);
            for (int i = 0; i < clouds.Length; i++)
            {
                var layer = Layers[i];
                float x = (float)clip.Get(layer.Track, frame, "tx", layer.X), a = (float)clip.Get(layer.Track, frame, "a", layer.A);
                var position = cloudBase[i] + new Vector2(x, -layer.Y);
                Place(clouds[i], position, a * (1 - clear));
                Place(cloudsClear[i], position, a * clear);
            }
        }

        static void Place(Image image, Vector2 position, float alpha)
        {
            image.rectTransform.anchoredPosition = position;
            image.Alpha(alpha > 0.004f ? alpha : 0);
        }
    }
}
