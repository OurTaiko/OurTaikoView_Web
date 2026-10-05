using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // Runtime helpers for the 1920x1080 Nijiiro stage: positions are the skin's top-left pixel
    // coordinates, every element is anchored to the stage's top-left corner and the Canvas scales it.
    public static class SkinUi
    {
        static TMP_FontAsset font;
        static Material outlineMaterial;

        public static RectTransform Rect(string name, Transform parent, float width = 0, float height = 0)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        public static Image Image(string name, Transform parent, Sprite sprite, float width = -1, float height = -1)
        {
            var rect = Rect(name, parent,
                width >= 0 ? width : sprite != null ? sprite.rect.width : 0,
                height >= 0 ? height : sprite != null ? sprite.rect.height : 0);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            if (sprite != null && sprite.border != Vector4.zero) image.type = UnityEngine.UI.Image.Type.Sliced;
            return image;
        }

        // Centre of a child of a stage-anchored parent, in skin pixels (y grows downwards).
        public static void Center(this RectTransform rect, float x, float y) => rect.anchoredPosition = new Vector2(x, -y);
        public static void TopLeft(this RectTransform rect, float x, float y)
            => rect.anchoredPosition = new Vector2(x + rect.sizeDelta.x * rect.pivot.x, -y - rect.sizeDelta.y * (1 - rect.pivot.y));

        public static void Alpha(this Graphic graphic, float alpha)
        {
            var color = graphic.color;
            color.a = alpha;
            graphic.color = color;
            graphic.enabled = alpha > 0.001f;
        }

        // All text uses one SDF font. Light text gets an opaque black outline; text that is itself
        // black (or near-black) gets none. TMP outline widths are a fraction of the em, so the
        // border follows each text's font size and scale.
        public const string FontName = "Nijiiro UI SDF", OutlineMaterialName = "Nijiiro UI SDF Outline";
        public const float OutlineWidth = 0.125f;

        public static TMP_FontAsset Font => font != null ? font : font = Resources.Load<TMP_FontAsset>(FontName)
            ?? throw new System.InvalidOperationException("Missing " + FontName + " font asset.");
        public static Material OutlineMaterial => outlineMaterial != null ? outlineMaterial
            : outlineMaterial = Resources.Load<Material>(OutlineMaterialName)
            ?? throw new System.InvalidOperationException("Missing " + OutlineMaterialName + " material.");

        public static TextMeshProUGUI Text(string name, Transform parent, float size)
        {
            var rect = Rect(name, parent, 1200, size * 1.6f);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.fontSize = size;
            text.UseUiFont();
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            text.color = Color.white;
            return text;
        }

        public static bool IsDark(Color color) => 0.2126f * color.r + 0.7152f * color.g + 0.0722f * color.b < 0.3f;

        // Binds the shared font and, by the text's colour, the outline or plain shared material
        // (never a per-text instance). Call again after changing a text between light and dark.
        public static void UseUiFont(this TMP_Text text)
        {
            text.font = Font;
            text.fontSharedMaterial = IsDark(text.color) ? Font.material : OutlineMaterial;
            text.UpdateMeshPadding();
        }

        // Arcade EditText boxes squeeze long text horizontally down to the box width.
        public static void Squeeze(this TMP_Text text, float maxWidth, float minScale = 0)
        {
            text.rectTransform.localScale = Vector3.one;
            float width = text.preferredWidth;
            float scale = width > maxWidth && width > 0 ? Mathf.Max(minScale, maxWidth / width) : 1;
            text.rectTransform.localScale = new Vector3(scale, 1, 1);
        }

        public static double CubicOut(double t) => 1 - System.Math.Pow(1 - System.Math.Min(1, System.Math.Max(0, t)), 3);
    }
}
