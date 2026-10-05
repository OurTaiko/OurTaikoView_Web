using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // Player::draw_modifiers with Nijiiro's mod_badge_grid (x 170, y 77, 44 x 44 cells, 3 columns),
    // in cabinet order: speed, doron, abekobe, random. Rows use whole-number slot / 3; the original's
    // float division slid the second and third badges down. Autoplay adds the song select's mod_auto
    // badge first (modifier.cpp lists auto first) instead of replacing the nameplate with auto_icon.
    [RequireComponent(typeof(RectTransform))]
    public sealed class ModifierBadgeView : MonoBehaviour
    {
        const float GridX = 170, GridY = 77, Cell = 44;
        const int Columns = 3;

        [Tooltip("mod_speed_x1_1 .. mod_speed_x4, in PlayOptions.SpeedBadgeValues order.")]
        public Sprite[] speed;
        public Sprite auto, doron, abekobe, kimagure, detarame;

        readonly List<Image> badges = new List<Image>();

        public int Count { get; private set; }

        public void Show(PlayOptions options, bool autoPlay)
        {
            var sprites = new List<Sprite>();
            if (autoPlay) sprites.Add(auto);
            int badge = PlayOptions.SpeedBadge(options.speed);
            if (badge >= 0 && speed != null && badge < speed.Length) sprites.Add(speed[badge]);
            if (options.display) sprites.Add(doron);
            if (options.inverse) sprites.Add(abekobe);
            if (options.random == RandomMode.Detarame) sprites.Add(detarame);
            else if (options.random == RandomMode.Kimagure) sprites.Add(kimagure);
            sprites.RemoveAll(s => s == null);
            Count = sprites.Count;
            while (badges.Count < sprites.Count) badges.Add(SkinUi.Image("Badge" + badges.Count, transform, null));
            for (int i = 0; i < badges.Count; i++)
            {
                bool shown = i < sprites.Count;
                badges[i].enabled = shown;
                if (!shown) continue;
                badges[i].sprite = sprites[i];
                badges[i].rectTransform.sizeDelta = sprites[i].rect.size;
                badges[i].rectTransform.TopLeft(GridX + i % Columns * Cell, GridY + i / Columns * Cell);
            }
        }
    }
}
