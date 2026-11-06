using UnityEngine;

namespace OurTaiko
{
    // Keeps the skin's authored rectangle while drawing with SpriteRenderer, not CanvasRenderer.
    // Geometry changes only when the sprite or rectangle changes; moving the note just moves its transform.
    [DisallowMultipleComponent, RequireComponent(typeof(RectTransform))]
    public sealed class LaneSprite : MonoBehaviour
    {
        RectTransform rect;
        SpriteRenderer spriteRenderer;

        public Sprite sprite
        {
            get => spriteRenderer.sprite;
            set { if (spriteRenderer.sprite != value) { spriteRenderer.sprite = value; Fit(); } }
        }
        public Color color { get => spriteRenderer.color; set => spriteRenderer.color = value; }
        public SpriteRenderer Renderer => spriteRenderer;

        public void Initialize(LaneSpriteLayer owner, Sprite source, int order)
        {
            rect = (RectTransform)transform;
            var child = new GameObject("Sprite", typeof(SpriteRenderer));
            child.transform.SetParent(transform, false);
            spriteRenderer = child.GetComponent<SpriteRenderer>();
            spriteRenderer.sharedMaterial = owner.Material;
            spriteRenderer.sortingOrder = order;
            spriteRenderer.sprite = source != null ? source : owner.WhiteSprite;
            Fit();
        }

        void OnEnable() { if (spriteRenderer != null) { spriteRenderer.enabled = true; Fit(); } }
        void OnDisable() { if (spriteRenderer != null) spriteRenderer.enabled = false; }
        void OnRectTransformDimensionsChange() { if (spriteRenderer != null) Fit(); }

        void Fit()
        {
            var source = spriteRenderer.sprite;
            if (source == null) return;
            var size = rect.rect.size;
            var pixels = source.rect.size;
            var child = spriteRenderer.transform;
            child.localScale = new Vector3(size.x * source.pixelsPerUnit / pixels.x,
                size.y * source.pixelsPerUnit / pixels.y, 1);
            // Match Image's full sprite rectangle, including a non-centred sprite pivot.
            child.localPosition = new Vector3(rect.rect.xMin + size.x * source.pivot.x / pixels.x,
                rect.rect.yMin + size.y * source.pivot.y / pixels.y, 0);
        }
    }
}
