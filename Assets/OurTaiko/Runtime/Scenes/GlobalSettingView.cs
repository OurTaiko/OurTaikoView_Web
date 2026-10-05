using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // The saved hierarchy of GlobalSettingScene (PyTaikoGreen settings art at 1.5x): the type list
    // on the left and the current type's item list on the right. Confirming an item opens the
    // choice popup (the item's name, description and choice buttons) over a dimming shade; the
    // popup is hidden while types and items are being chosen, and a tap on the shade closes it.
    // Row 0 of each list is the authored base; row i sits `pitch` below row i - 1, and missing
    // rows are copied from row 0 at runtime.
    public sealed class GlobalSettingView : MonoBehaviour
    {
        [Serializable]
        public sealed class Row
        {
            public RectTransform root;
            public Image box;
            public TMP_Text label;
            public TMP_Text value;     // items only: the current choice
            public PointerRelay click;
        }

        public List<Row> typeRows = new List<Row>();
        public List<Row> itemRows = new List<Row>();
        public List<Row> choiceRows = new List<Row>();
        [Tooltip("Vertical distance between type rows / item rows; horizontal between choice buttons " +
            "(wide enough for the arrow between two buttons).")]
        public float typePitch = 142, itemPitch = 150, choicePitch = 380;
        public SwipeRelay typeSwipe, itemSwipe;
        public Sprite typeBox, typeBoxSelected, itemBox, itemBoxSelected, choiceOff, choiceOn;
        [Tooltip("blue_arrow: points at the focused row or choice from its right.")]
        public RectTransform cursor;
        public float cursorGap = 12;
        [Tooltip("The choice popup, shown only while a setting's choices are open.")]
        public CanvasGroup detail;
        public TMP_Text detailTitle, description;
        [Tooltip("Dims the lists behind the popup and closes it when tapped.")]
        public Image shade;
        public PointerRelay shadeClick;

        public PointerRelay previousItems, nextItems, previousChoice, nextChoice;
        public TMP_Text itemPage, outputStatus;
        public int visibleItems = 4;
        public int FirstItem { get; private set; }
        public int FirstChoice { get; private set; }
        int lastType = -1;
        Vector2 typeBase, itemBase, choiceBase;
        bool bound;

        void Bind()
        {
            if (bound) return;
            bound = true;
            typeBase = typeRows[0].root.anchoredPosition;
            itemBase = itemRows[0].root.anchoredPosition;
            choiceBase = choiceRows[0].root.anchoredPosition;
        }

        static void Ensure(List<Row> rows, int count)
        {
            while (rows.Count < count)
            {
                var source = rows[0];
                var copy = Instantiate(source.root.gameObject, source.root.parent);
                copy.name = source.root.name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9') + rows.Count;
                var t = copy.transform;
                rows.Add(new Row
                {
                    root = (RectTransform)t,
                    box = copy.GetComponent<Image>(),
                    label = source.label != null ? t.Find(source.label.name).GetComponent<TMP_Text>() : null,
                    value = source.value != null ? t.Find(source.value.name).GetComponent<TMP_Text>() : null,
                    click = copy.GetComponent<PointerRelay>(),
                });
            }
        }

        // Hooks the rows' taps; extra rows created later are hooked as they appear.
        public void Bind(Action<int> tapType, Action<int> tapItem, Action<int> tapChoice, Action<int> swipeTypes, Action<int> swipeItems, Action tapShade, Action<int> moveChoice = null)
        {
            Bind();
            shadeClick.Clicked = tapShade;
            tapTypeHandler = tapType; tapItemHandler = tapItem; tapChoiceHandler = tapChoice;
            typeSwipe.Swiped = swipeTypes;
            itemSwipe.Swiped = swipeItems;
            if (previousItems != null) previousItems.Clicked = () => swipeItems(-visibleItems);
            if (nextItems != null) nextItems.Clicked = () => swipeItems(visibleItems);
            if (previousChoice != null) previousChoice.Clicked = () => moveChoice?.Invoke(-1);
            if (nextChoice != null) nextChoice.Clicked = () => moveChoice?.Invoke(1);
            Hook();
        }

        Action<int> tapTypeHandler, tapItemHandler, tapChoiceHandler;

        void Hook()
        {
            for (int i = 0; i < typeRows.Count; i++) { int index = i; typeRows[i].click.Clicked = () => tapTypeHandler?.Invoke(index); }
            for (int i = 0; i < itemRows.Count; i++) { int index = i; itemRows[i].click.Clicked = () => tapItemHandler?.Invoke(index); }
            for (int i = 0; i < choiceRows.Count; i++) { int index = i; choiceRows[i].click.Clicked = () => tapChoiceHandler?.Invoke(index + FirstChoice); }
        }

        public void Show(SettingsMenu menu)
        {
            Bind();
            int types = menu.TypeCount;
            int items = menu.CurrentType != null ? menu.ItemCount : 0;
            var item = menu.CurrentItem;
            bool numeric = item?.IsNumber == true;
            int choices = numeric ? 1 : item?.Choices.Count ?? 0;
            int before = typeRows.Count + itemRows.Count + choiceRows.Count;
            Ensure(typeRows, types);
            Ensure(itemRows, Math.Max(1, items));
            Ensure(choiceRows, Math.Max(1, Math.Min(3, choices)));
            if (typeRows.Count + itemRows.Count + choiceRows.Count != before) Hook();

            for (int i = 0; i < typeRows.Count; i++)
            {
                var row = typeRows[i];
                bool shown = i < types;
                row.root.gameObject.SetActive(shown);
                if (!shown) continue;
                row.root.anchoredPosition = typeBase + new Vector2(0, -i * typePitch);
                row.label.text = i < menu.Types.Count ? menu.Types[i].Label : "Return";
                row.box.sprite = i == menu.TypeIndex ? typeBoxSelected : typeBox;
            }

            if (lastType != menu.TypeIndex) { FirstItem = 0; lastType = menu.TypeIndex; }
            FirstItem = Mathf.Clamp(FirstItem, Math.Max(0, menu.ItemIndex - visibleItems + 1), menu.ItemIndex);
            FirstItem = Mathf.Clamp(FirstItem, 0, Math.Max(0, items - visibleItems));
            bool paged = items > visibleItems;
            if (previousItems != null) previousItems.gameObject.SetActive(paged);
            if (nextItems != null) nextItems.gameObject.SetActive(paged);
            if (itemPage != null) itemPage.text = paged ? $"{FirstItem + 1}–{Math.Min(items, FirstItem + visibleItems)} / {items}" : "";
            if (outputStatus != null)
            {
                var engine = AudioEngine.Instance;
                outputStatus.text = menu.CurrentType?.Label == "Sound" && engine != null
                    ? $"Current output: {engine.Backend}" + (engine.HasPendingDeviceChanges ? "   •   Applies on exit" : "") : "";
            }
            bool inItems = menu.Focus != SettingsFocus.Types;
            for (int i = 0; i < itemRows.Count; i++)
            {
                var row = itemRows[i];
                bool shown = i >= FirstItem && i < Math.Min(items, FirstItem + visibleItems);
                row.root.gameObject.SetActive(shown);
                if (!shown) continue;
                row.root.anchoredPosition = itemBase + new Vector2(0, -(i - FirstItem) * itemPitch);
                bool isReturn = i == menu.CurrentType.Items.Count;
                var rowItem = isReturn ? null : menu.CurrentType.Items[i];
                row.label.text = isReturn ? "Return" : rowItem.Label;
                row.value.enableAutoSizing = true; row.value.fontSizeMin = 18; row.value.fontSizeMax = 40;
                row.value.overflowMode = TextOverflowModes.Ellipsis;
                row.value.text = isReturn ? "" : rowItem.Format(rowItem.Get(menu.Settings));
                row.box.sprite = inItems && i == menu.ItemIndex ? itemBoxSelected : itemBox;
            }

            // The popup belongs to the choice focus only.
            bool open = menu.Focus == SettingsFocus.Choice && item != null;
            var shownItem = open ? item : null;
            detail.alpha = open ? 1 : 0;
            detail.blocksRaycasts = open;
            shade.gameObject.SetActive(open);
            if (previousChoice != null) previousChoice.gameObject.SetActive(open && (numeric || choices > 3));
            if (nextChoice != null) nextChoice.gameObject.SetActive(open && (numeric || choices > 3));
            if (open)
            {
                detailTitle.text = shownItem.Label;
                description.enableAutoSizing = true; description.fontSizeMin = 22; description.fontSizeMax = 32;
                description.text = shownItem.Description;
                if (numeric) description.text += $"\nDefault: {shownItem.Format(shownItem.DefaultValue)}  /  Step: {shownItem.Step} {shownItem.Unit}  /  Ka: adjust  /  Don or tap: save";
                int lit = numeric ? 0 : menu.ChoiceIndex;
                FirstChoice = Mathf.Clamp(lit - 1, 0, Math.Max(0, choices - 3));
                int count = Math.Min(3, choices);
                // Centre the buttons together with the arrow's room right of the last one.
                float shift = -(cursorGap + cursor.rect.width) / 2;
                for (int i = 0; i < choiceRows.Count; i++)
                {
                    var row = choiceRows[i];
                    bool shown = i < count;
                    row.root.gameObject.SetActive(shown);
                    if (!shown) continue;
                    row.root.anchoredPosition = choiceBase + new Vector2((i - (count - 1) / 2f) * choicePitch + shift, 0);
                    row.label.enableAutoSizing = true; row.label.fontSizeMin = 18; row.label.fontSizeMax = 40;
                    row.label.overflowMode = TextOverflowModes.Ellipsis;
                    row.label.text = numeric ? shownItem.Format(menu.ChoiceIndex) : shownItem.Choices[i + FirstChoice];
                    row.box.sprite = i + FirstChoice == lit ? choiceOn : choiceOff;
                }
            }

            RectTransform target = menu.Focus switch
            {
                SettingsFocus.Types => typeRows[menu.TypeIndex].root,
                SettingsFocus.Items => itemRows[menu.ItemIndex].root,
                _ => choiceRows[numeric ? 0 : menu.ChoiceIndex - FirstChoice].root,
            };
            PlaceCursor(target);
        }

        void PlaceCursor(RectTransform target)
        {
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);
            var parent = (RectTransform)cursor.parent;
            // corners: 0 bottom-left, 2 top-right; the arrow sits right of the target, centred on it.
            Vector2 right = parent.InverseTransformPoint((corners[2] + corners[3]) / 2);
            Vector2 middle = parent.InverseTransformPoint((corners[0] + corners[2]) / 2);
            cursor.pivot = new Vector2(0, 0.5f);
            cursor.anchorMin = cursor.anchorMax = new Vector2(0.5f, 0.5f);
            cursor.anchoredPosition = new Vector2(right.x + cursorGap, middle.y) - Center(parent);
        }

        static Vector2 Center(RectTransform rect) => rect.rect.center;
    }
}
