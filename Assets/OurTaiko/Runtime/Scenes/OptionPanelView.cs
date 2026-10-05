using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // The editable, saved hierarchy. OptionPanel binds input and animates these objects at runtime;
    // the resting board and arrow positions come from their RectTransforms, not fixed coordinates.
    [RequireComponent(typeof(RectTransform))]
    public sealed class OptionPanelView : MonoBehaviour
    {
        [Serializable]
        public sealed class RowView
        {
            public Image row, highlight, box, icon, leftArrow, rightArrow, scrim;
            public TextMeshProUGUI name, value;
            public PointerRelay select, previous, next;
        }

        public RectTransform board;
        public Image outside, top, player;
        public PointerRelay outsideClick;
        public TextMeshProUGUI title;
        public RowView[] rows = new RowView[7];

        public RectTransform Root => (RectTransform)transform;
    }
}
