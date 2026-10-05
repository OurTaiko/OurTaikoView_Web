using System.Collections.Generic;
using UnityEngine;

namespace OurTaiko
{
    // draw_arc_list: flying notes above the soul gauge, later arcs drawn over earlier ones.
    public sealed class NoteArcView : MonoBehaviour
    {
        // The note lane whose top-left corner is the lane-local origin of NoteArcPath.
        public RectTransform lane;
        // Takes over each note as it reaches the badge.
        public GaugeHitEffectView gaugeHitEffect;

        const float NoteSize = 192;
        readonly List<Arc> active = new List<Arc>(), pool = new List<Arc>();

        sealed class Arc
        {
            public RectTransform Root;
            public UnityEngine.UI.Image Image;
            public double Start;
            public bool Big;
        }

        public int ActiveCount => active.Count;
        public RectTransform ArcRoot(int index) => active[index].Root;

        public void Spawn(Sprite sprite, bool big, double time)
        {
            Arc arc;
            if (pool.Count > 0) { arc = pool[pool.Count - 1]; pool.RemoveAt(pool.Count - 1); }
            else
            {
                var root = new GameObject("NoteArc", typeof(RectTransform)).GetComponent<RectTransform>();
                root.SetParent(transform, false);
                root.anchorMin = root.anchorMax = new Vector2(0, 1);
                root.pivot = new Vector2(0.5f, 0.5f);
                root.sizeDelta = new Vector2(NoteSize, NoteSize);
                var image = root.gameObject.AddComponent<UnityEngine.UI.Image>();
                image.raycastTarget = false;
                arc = new Arc { Root = root, Image = image };
            }
            arc.Image.sprite = sprite;
            arc.Start = time;
            arc.Big = big;
            arc.Root.SetAsLastSibling();
            arc.Root.gameObject.SetActive(true);
            active.Add(arc);
            Place(arc, 0);
        }

        public void ResetDisplay()
        {
            foreach (var arc in active) { arc.Root.gameObject.SetActive(false); pool.Add(arc); }
            active.Clear();
            if (gaugeHitEffect != null) gaugeHitEffect.ResetDisplay();
        }

        public void ShowTime(double time)
        {
            for (int i = 0; i < active.Count; i++)
            {
                var arc = active[i];
                double elapsed = time - arc.Start;
                if (!NoteArcPath.IsFinished(elapsed)) { Place(arc, NoteArcPath.Progress(elapsed)); continue; }
                arc.Root.gameObject.SetActive(false);
                pool.Add(arc); active.RemoveAt(i--);
                if (gaugeHitEffect != null) gaugeHitEffect.Play(arc.Image.sprite, arc.Big, arc.Start + NoteArcPath.Duration);
            }
            if (gaugeHitEffect != null) gaugeHitEffect.ShowTime(time);
        }

        void Place(Arc arc, double progress)
        {
            NoteArcPath.Position(progress, out double x, out double y);
            arc.Root.anchoredPosition = LaneToLocal(lane, (RectTransform)transform, x, y);
        }

        // Map lane-local (Y down) through the lane's transform to a top-left anchored position
        // in the layer, so the path follows the lane instead of fixed screen pixels.
        public static Vector2 LaneToLocal(RectTransform lane, RectTransform layer, double x, double y)
        {
            var corner = (Vector2)layer.InverseTransformPoint(lane.TransformPoint(new Vector3(lane.rect.xMin, lane.rect.yMax)));
            var own = layer.rect;
            return corner - new Vector2(own.xMin, own.yMax) + new Vector2((float)x, -(float)y);
        }
    }
}
