using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace OurTaiko
{
    public sealed class PointerRelay : MonoBehaviour, IPointerClickHandler, IPointerDownHandler,
        IPointerUpHandler, IPointerExitHandler
    {
        public Action Clicked;
        public Action LongPressed;
        public Func<bool> CanLongPress;
        bool holding, suppressClick;
        int pointerId;
        double pressedAt;
        PointerEventData press;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (LongPressed == null || holding || eventData.button != PointerEventData.InputButton.Left) return;
            pointerId = eventData.pointerId;
            suppressClick = false;
            if (CanLongPress != null && !CanLongPress()) return;
            holding = true;
            press = eventData;
            pressedAt = GameTimeline.FrameTime;
        }

        void Update()
        {
            if (!holding) return;
            if (CanLongPress != null && !CanLongPress()) { CancelHold(); return; }
            if (GameTimeline.FrameTime - pressedAt <= 1) return;
            CancelHold(); // Consume the release click before invoking scene code.
            LongPressed?.Invoke();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId != pointerId) return;
            holding = false;
            press = null;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (holding && eventData.pointerId == pointerId) CancelHold();
        }

        void CancelHold()
        {
            holding = false;
            suppressClick = true;
            if (press != null) press.eligibleForClick = false;
            press = null;
        }

        void OnDisable() => CancelHold();
        void OnApplicationFocus(bool focused) { if (!focused && holding) CancelHold(); }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (LongPressed != null && (suppressClick || eventData.pointerId != pointerId)) return;
            Clicked?.Invoke();
        }
    }
}
