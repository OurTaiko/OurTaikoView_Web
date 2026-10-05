using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // ServerLogin's saved hierarchy (built by ProjectBuilder.CreateServerLoginScene): the server
    // panel with its name and address, the account fields, a message line and the action buttons.
    // ServerLoginScene only fills texts, swaps the focus sprites and wires the clicks.
    public sealed class ServerLoginView : MonoBehaviour
    {
        public enum Item { Username, Password, Login, Guest, Skip, Back }

        [Serializable]
        public sealed class Button
        {
            public Item item;
            public Image box;
            public TMP_Text label;
            public PointerRelay click;
        }

        public TMP_Text header, serverName, serverUrl, progress, message;
        public TMP_InputField username, password;
        public Image usernameBox, passwordBox;
        public Button[] buttons = Array.Empty<Button>();
        public Sprite fieldOff, fieldOn, buttonOff, buttonOn;
        public Color messageColor = Color.black, errorColor = new Color(1, 0.42f, 0.42f), successColor = new Color(0.55f, 1, 0.55f);

        public static readonly Item[] Order = { Item.Username, Item.Password, Item.Login, Item.Guest, Item.Skip, Item.Back };

        public void Bind(Action<Item> clicked)
        {
            foreach (var button in buttons)
            {
                var item = button.item;
                button.click.Clicked = () => clicked(item);
            }
            // Clicking a field focuses it through TMP_InputField itself; the scene follows the selection.
            username.onSelect.AddListener(_ => clicked(Item.Username));
            password.onSelect.AddListener(_ => clicked(Item.Password));
        }

        public void ShowFocus(Item focus, bool busy)
        {
            usernameBox.sprite = focus == Item.Username ? fieldOn : fieldOff;
            passwordBox.sprite = focus == Item.Password ? fieldOn : fieldOff;
            foreach (var button in buttons)
            {
                button.box.sprite = button.item == focus ? buttonOn : buttonOff;
                // Only Back stays usable while a request runs (it cancels the request).
                var color = button.box.color;
                color.a = busy && button.item != Item.Back ? 0.45f : 1;
                button.box.color = color;
            }
            username.interactable = password.interactable = !busy;
        }

        public void ShowMessage(string text, bool error = false, bool success = false)
        {
            message.text = text ?? "";
            message.color = error ? errorColor : success ? successColor : messageColor;
            message.UseUiFont();
        }
    }
}
