using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OurTaiko
{
    // The original's touch drum (OurTaiko.cpp / input.cpp touch_quadrant_vkey): a half drum at the
    // bottom of the 1920x1080 design area. The top half of the screen is ka; in the bottom half the
    // drum ellipse is don and the rest ka, split left/right at the centre. InputManager hit-tests
    // touches and the mouse against every enabled pad in its per-frame update, alongside the keyboard.
    [RequireComponent(typeof(RectTransform))]
    public sealed class DrumPad : MonoBehaviour
    {
        // Ellipse radii as fractions of the design width, centred on the bottom edge.
        const float RadiusX = 0.262f, RadiusY = 0.242f;
        [Tooltip("The drum picture; DrumSqueeze.anim scales it about its bottom-centre pivot when pressed.")]
        public RectTransform drum;
        ClipSampler squeeze;
        RectTransform rect;
        Canvas canvas;
        double pressedAt = double.NegativeInfinity;
        static readonly List<RaycastResult> hits = new List<RaycastResult>();

        void OnEnable()
        {
            rect = (RectTransform)transform;
            canvas = GetComponentInParent<Canvas>();
            pressedAt = double.NegativeInfinity;
            if (drum != null) drum.localScale = Vector3.one;
            InputManager.RegisterPad(this);
        }
        void OnDisable()
        {
            InputManager.UnregisterPad(this);
            pressedAt = double.NegativeInfinity;
            if (drum != null) drum.localScale = Vector3.one;
        }

        // Real time, like the original's get_current_ms; disabled pads cannot animate or emit input.
        public void Press()
        {
            if (isActiveAndEnabled) pressedAt = GameTimeline.FrameTime;
        }

        void Update()
        {
            if (drum == null) return;
            if (squeeze == null) squeeze = drum.GetComponent<ClipSampler>();
            // Global animation 66, restarted per press; the clip ends back at full size.
            double elapsed = GameTimeline.FrameTime - pressedAt;
            squeeze.Sample(System.Math.Min(elapsed, squeeze.clip.length));
        }

        // Covers the whole screen like the original; points over a uGUI control are left to that control.
        public bool TryHit(Vector2 screenPoint, out InputKey key)
        {
            key = default;
            if (!isActiveAndEnabled) return false;
            var root = canvas != null ? canvas.rootCanvas : null;
            var camera = root == null || root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screenPoint, camera, out var local)) return false;
            if (OverControl(screenPoint)) return false;
            var area = rect.rect;
            float dx = local.x - area.center.x, dy = local.y - area.yMin;
            bool left = dx < 0, top = local.y > area.center.y;
            float nx = dx / (area.width * RadiusX), ny = dy / (area.width * RadiusY);
            bool don = !top && nx * nx + ny * ny <= 1;
            key = don ? left ? InputKey.LeftDon : InputKey.RightDon : left ? InputKey.LeftKa : InputKey.RightKa;
            return true;
        }

        static bool OverControl(Vector2 screenPoint)
        {
            var events = EventSystem.current;
            if (events == null) return false;
            hits.Clear();
            events.RaycastAll(new PointerEventData(events) { position = screenPoint }, hits);
            return hits.Count > 0 && hits[0].gameObject.GetComponentInParent<Selectable>() != null;
        }
    }
}
