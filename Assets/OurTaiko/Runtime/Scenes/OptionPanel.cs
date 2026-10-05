using System;
using UnityEngine;

namespace OurTaiko
{
    [Serializable]
    public sealed class OptionPanelArt
    {
        public Sprite board, player, row, rowHighlight, box, arrow;
        public Sprite auto, doron, abekobe, kimagure, detarame;
        [Tooltip("mod_speed_x1_1 .. mod_speed_x4, in PlayOptions.SpeedBadgeValues order.")]
        public Sprite[] speed;
        public TextAsset cursorTimeline;
        public HitSoundLibrary hitSounds;
        public AudioClip voice;
    }

    // Nijiiro's 演奏オプション board (song_select.lua draw_option_board, which replaces ModifierSelector::draw):
    // modifier/top at (5, 532) when open, rows every 61 px from y 618, name at x 44, value centred at x 300.
    // Off-default values get a yellow box, the cursor row pulses (anim/option_cursor), the pressed
    // arrow nudges 5 px outward and the greyed row gets a flat grey plate and a 50 % black scrim.
    public sealed class OptionPanel
    {
        const float Nudge = 5;
        const double ChangeMs = 250;
        static readonly Color Grey = new Color32(166, 168, 171, 255), ChangedBox = new Color32(255, 255, 0, 255);

        readonly OptionPanelArt art;
        readonly RectTransform root, board;
        readonly OptionPanelView.RowView[] rows;
        readonly Vector2 boardRestPosition;
        readonly Vector2[] leftArrowRestPositions, rightArrowRestPositions;
        readonly LumenClip cursorClip;
        double openedAt, closedAt = -1, changedAt = -1;
        int changeDirection;

        public OptionMenu Menu { get; private set; }
        public bool IsOpen => Menu != null;
        public bool IsClosing => closedAt >= 0;
        // Raised by touch: (row, direction) with direction -1/+1 for the value arrows, 0 for the row itself.
        public event Action<int, int> RowTapped;
        public event Action OutsideTapped;

        public OptionPanel(OptionPanelView view, OptionPanelArt art)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (view.board == null || view.rows == null || view.rows.Length != OptionMenu.Rows.Length)
                throw new ArgumentException("The saved option panel must contain its board and seven rows.", nameof(view));
            this.art = art;
            cursorClip = art.cursorTimeline != null ? LumenClip.Parse(art.cursorTimeline.text) : LumenClip.Empty;
            root = view.Root;
            board = view.board;
            rows = view.rows;
            boardRestPosition = board.anchoredPosition;
            leftArrowRestPositions = new Vector2[rows.Length];
            rightArrowRestPositions = new Vector2[rows.Length];
            view.outsideClick.Clicked = () => OutsideTapped?.Invoke();
            for (int i = 0; i < rows.Length; i++)
            {
                int index = i;
                var row = rows[i];
                leftArrowRestPositions[i] = row.leftArrow.rectTransform.anchoredPosition;
                rightArrowRestPositions[i] = row.rightArrow.rectTransform.anchoredPosition;
                row.select.Clicked = () => RowTapped?.Invoke(index, 0);
                row.previous.Clicked = () => RowTapped?.Invoke(index, -1);
                row.next.Clicked = () => RowTapped?.Invoke(index, +1);
            }
            root.gameObject.SetActive(false);
        }

        public void Open(PlayOptions options, double now)
        {
            Menu = new OptionMenu(options, art.hitSounds != null ? art.hitSounds.Count : 0);
            openedAt = now; closedAt = -1; changedAt = -1;
            root.SetAsLastSibling();
            root.gameObject.SetActive(true);
        }

        // Starts the slide out once the menu is confirmed.
        public void Close(double now)
        {
            if (Menu == null || closedAt >= 0) return;
            Menu.ConfirmAll();
            closedAt = now;
        }

        public void Changed(int direction, double now)
        {
            changeDirection = direction;
            changedAt = now;
        }

        // Returns true on the frame the slide out ends; the panel is hidden from then on.
        public bool Draw(double now)
        {
            if (Menu == null) return false;
            if (Menu.IsConfirmed && closedAt < 0) closedAt = now;
            double drop = closedAt >= 0
                ? OptionMenu.SlideOut(now - closedAt)
                : OptionMenu.SlideDistance - OptionMenu.SlideIn(now - openedAt);
            board.anchoredPosition = boardRestPosition + Vector2.down * (float)drop;
            if (closedAt >= 0 && now - closedAt >= OptionMenu.SlideMs)
            {
                Menu = null;
                root.gameObject.SetActive(false);
                return true;
            }
            double pulse = cursorClip.IsEmpty ? 1
                : cursorClip.Get("#3@0", now * 0.06 % Math.Max(1, cursorClip.Last - cursorClip.First + 1), "a", 1);
            double change = changedAt < 0 ? 0 : Math.Min(1, (now - changedAt) / ChangeMs);
            float nudge = changedAt < 0 || change >= 1 ? 0 : Nudge * (float)(1 - change * (2 - change));
            for (int i = 0; i < rows.Length; i++) DrawRow(i, pulse, nudge);
            return false;
        }

        void DrawRow(int index, double pulse, float nudge)
        {
            var view = rows[index];
            var row = OptionMenu.Rows[index];
            bool greyed = OptionMenu.IsGreyed(row);
            bool current = !Menu.IsConfirmed && Menu.Index == index;
            view.highlight.enabled = greyed || current;
            view.highlight.color = greyed ? Grey : new Color(1, 1, 1, (float)pulse);
            view.box.color = Menu.IsChanged(row) ? ChangedBox : Color.white;
            view.value.text = Value(row);
            var icon = Icon(row);
            view.icon.enabled = icon != null;
            view.icon.sprite = icon;
            view.leftArrow.enabled = view.rightArrow.enabled = current;
            view.leftArrow.rectTransform.anchoredPosition = leftArrowRestPositions[index]
                + Vector2.left * (changeDirection < 0 ? nudge : 0);
            view.rightArrow.rectTransform.anchoredPosition = rightArrowRestPositions[index]
                + Vector2.right * (changeDirection > 0 ? nudge : 0);
        }

        string Value(OptionRow row)
        {
            var options = Menu.Options;
            switch (row)
            {
                case OptionRow.Speed: return (options.speed / 10.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
                case OptionRow.Random: return options.random == RandomMode.Kimagure ? "きまぐれ" : options.random == RandomMode.Detarame ? "でたらめ" : "しない";
                case OptionRow.Neiro:
                    int slot = Menu.NeiroSlot;
                    return art.hitSounds != null && slot < art.hitSounds.Count ? art.hitSounds.names[slot] : "無音";
                case OptionRow.Skip: return "しない";
                default: return Menu.IsChanged(row) ? "する" : "しない";
            }
        }

        Sprite Icon(OptionRow row)
        {
            var options = Menu.Options;
            switch (row)
            {
                case OptionRow.Speed:
                    int badge = PlayOptions.SpeedBadge(options.speed);
                    return badge >= 0 && art.speed != null && badge < art.speed.Length ? art.speed[badge] : null;
                case OptionRow.Random:
                    return options.random == RandomMode.Detarame ? art.detarame : options.random == RandomMode.Kimagure ? art.kimagure : null;
                case OptionRow.Auto: return options.auto ? art.auto : null;
                case OptionRow.Display: return options.display ? art.doron : null;
                case OptionRow.Inverse: return options.inverse ? art.abekobe : null;
                default: return null;
            }
        }
    }
}
