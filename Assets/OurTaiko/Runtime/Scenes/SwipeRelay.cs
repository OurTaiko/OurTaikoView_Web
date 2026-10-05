using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace OurTaiko
{
    // A vertical swipe over a list: every `step` design pixels dragged reports one row. Dragging
    // upwards (the list pulled up) is +1, the next row; downwards is -1. Taps still reach the rows'
    // PointerRelays, since the event system sends no click once a drag has started.
    public sealed class SwipeRelay : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler
    {
        [Tooltip("Design pixels of drag per row.")]
        public float step = 120;
        public Action<int> Swiped;

        float pending;

        public void OnScroll(PointerEventData eventData)
        {
            if (Mathf.Abs(eventData.scrollDelta.y) > 0.01f) Swiped?.Invoke(eventData.scrollDelta.y > 0 ? -1 : 1);
        }

        public void OnBeginDrag(PointerEventData eventData) => pending = 0;

        public void OnDrag(PointerEventData eventData)
        {
            var canvas = GetComponentInParent<Canvas>();
            float scale = canvas != null ? canvas.rootCanvas.scaleFactor : 1;
            pending += eventData.delta.y / Mathf.Max(0.0001f, scale);
            while (Mathf.Abs(pending) >= step)
            {
                int direction = pending > 0 ? 1 : -1;
                pending -= direction * step;
                Swiped?.Invoke(direction);
            }
        }

        public void OnEndDrag(PointerEventData eventData) => pending = 0;
    }
}
