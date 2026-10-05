using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // The SongSelect scene owns the global chrome. Separate saved digit rows keep the 60 and
    // 100 placeholders centred without changing any Inspector-authored layout at runtime.
    public sealed class SongSelectOverlayView : MonoBehaviour
    {
        public Image timerBackground;
        public Image[] timerTwoDigits;
        public Image[] timerThreeDigits;
        public Image qrChip;
        public Image inviteBubble;
        public TextMeshProUGUI invitePlayer;
        public TextMeshProUGUI inviteMessage;
    }
}
