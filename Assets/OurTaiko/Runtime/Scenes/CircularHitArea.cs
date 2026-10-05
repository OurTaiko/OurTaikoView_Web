using UnityEngine;

namespace OurTaiko
{
    // Keep the whole circle clickable, including the transparent centre of an outlined icon.
    public sealed class CircularHitArea : MonoBehaviour, ICanvasRaycastFilter
    {
        public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
        {
            var rect = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screenPoint, eventCamera, out var local)) return false;
            float radius = Mathf.Min(rect.rect.width, rect.rect.height) * .5f;
            return (local - rect.rect.center).sqrMagnitude <= radius * radius;
        }
    }
}
