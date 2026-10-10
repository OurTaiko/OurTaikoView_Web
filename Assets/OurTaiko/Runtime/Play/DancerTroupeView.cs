using System.Linq;
using UnityEngine;

namespace OurTaiko
{
    // The play backdrop's dancers (Nijiiro ArcadeDancerGroup): three hop in at the start, the
    // fourth joins from half a soul gauge and the fifth with the clear state, all on one playhead
    // that follows the chart's tempo. Each dancer's clip holds its whole rig timeline; this view
    // only picks the rig frame.
    public sealed class DancerTroupeView : MonoBehaviour
    {
        [Tooltip("Stand points in spawn order: centre, left, right, far left, far right.")]
        public RectTransform[] slots;
        [Tooltip("One dancer saved under each slot; play deals them over the slots at random.")]
        public DancerView[] dancers;

        public DancerTroupe Troupe { get; private set; }
        double shownTime = double.NaN;

        void Awake()
        {
            // Fisher-Yates over the saved dancers, as the rig shuffles its variants.
            for (int i = dancers.Length - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (dancers[i], dancers[j]) = (dancers[j], dancers[i]);
            }
            for (int i = 0; i < dancers.Length; i++) dancers[i].transform.SetParent(slots[i], false);
            Troupe = new DancerTroupe(dancers.Select(d => d.Rig).ToArray());
            foreach (var dancer in dancers) dancer.Show(-1);
        }

        // gauge is the soul gauge's fill from 0 to 1. A time already shown (a still pause) is left as it is.
        public void ShowTime(double time, double bpm, double gauge, bool clear)
        {
            if (time == shownTime) return;
            shownTime = time;
            Troupe.Advance(time, bpm);
            Troupe.SetCount(DancerTroupe.CountFor(gauge, clear));
            for (int i = 0; i < dancers.Length; i++) dancers[i].Show(Troupe.FrameOf(i));
        }
    }
}
