using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace OurTaiko
{
    // Black bars over everything outside the play scene's 1920x1080 viewport (issue #43). The canvas
    // expands with windows of another shape, so notes passing the lane's left edge, the header art and
    // the soul gauge would otherwise show beside the letterbox. The bars sit on their own overlay
    // canvas, above the scene's camera canvas and its SpriteRenderer notes, below the settings overlay
    // and SceneSwitcher's transitions.
    [DisallowMultipleComponent]
    public sealed class PlayfieldMask : MonoBehaviour
    {
        public const string ViewportName = "Viewport1920x1080";
        public const int SortingOrder = 31000;

        RectTransform viewport;
        Camera viewportCamera;
        readonly RectTransform[] bars = new RectTransform[4];
        readonly Vector3[] corners = new Vector3[4];
        Rect previous, previousScreen;

        public RectTransform Viewport => viewport;

        // Covers the scene of the given component; does nothing when the scene has no viewport.
        public static PlayfieldMask Cover(Component owner)
        {
            var viewport = FindViewport(owner.gameObject.scene);
            if (viewport == null) return null;
            var go = new GameObject(nameof(PlayfieldMask), typeof(RectTransform));
            SceneManager.MoveGameObjectToScene(go, owner.gameObject.scene);
            var mask = go.AddComponent<PlayfieldMask>();
            mask.Initialize(viewport);
            return mask;
        }

        static RectTransform FindViewport(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (!root.TryGetComponent<Canvas>(out _)) continue;
                if (root.transform.Find(ViewportName) is RectTransform found) return found;
            }
            return null;
        }

        void Initialize(RectTransform target)
        {
            viewport = target;
            var root = target.GetComponentInParent<Canvas>().rootCanvas;
            viewportCamera = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            for (int i = 0; i < bars.Length; i++)
            {
                var bar = new GameObject("Bar" + i, typeof(RectTransform)).GetComponent<RectTransform>();
                bar.SetParent(transform, false);
                bar.anchorMin = bar.anchorMax = bar.pivot = Vector2.zero;
                var image = bar.gameObject.AddComponent<Image>();
                image.color = Color.black;
                image.raycastTarget = false;
                bars[i] = bar;
            }
            Fit();
        }

        void LateUpdate() => Fit();

        // In screen pixels, which the unscaled overlay canvas uses as its units.
        void Fit()
        {
            if (viewport == null) return;
            viewport.GetWorldCorners(corners);
            Vector2 min = RectTransformUtility.WorldToScreenPoint(viewportCamera, corners[0]);
            Vector2 max = RectTransformUtility.WorldToScreenPoint(viewportCamera, corners[2]);
            var shown = Rect.MinMaxRect(Mathf.Round(min.x), Mathf.Round(min.y), Mathf.Round(max.x), Mathf.Round(max.y));
            var screen = new Rect(0, 0, Screen.width, Screen.height);
            if (shown == previous && screen == previousScreen) return;
            previous = shown;
            previousScreen = screen;
            // Left and right run the full height; bottom and top fill between them.
            Place(bars[0], 0, 0, shown.xMin, screen.height);
            Place(bars[1], shown.xMax, 0, screen.width - shown.xMax, screen.height);
            Place(bars[2], shown.xMin, 0, shown.width, shown.yMin);
            Place(bars[3], shown.xMin, shown.yMax, shown.width, screen.height - shown.yMax);
        }

        static void Place(RectTransform bar, float x, float y, float width, float height)
        {
            bar.anchoredPosition = new Vector2(x, y);
            bar.sizeDelta = new Vector2(Mathf.Max(0, width), Mathf.Max(0, height));
        }
    }
}
