using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine.InputSystem;

namespace OurTaiko.Tests
{
    public sealed class KeyboardBindingsTests
    {
        [TearDown] public void Restore() => InputManager.ResetBindings();

        [Test]
        public void OldSettingsKeepDefaultsAndEmptyBindingsSurviveReload()
        {
            var old = GameSettings.FromJson("{\"general\":{\"language\":\"ja\"}}");
            Assert.That(old.general.keyboard.Get(InputKey.LeftDon), Is.EqualTo(new[] { Key.F }));
            old.general.keyboard.Set(InputKey.LeftDon);
            old.general.keyboard.Set(InputKey.RightDon, Key.A, Key.S, Key.S);
            var loaded = old.Clone();
            Assert.That(loaded.general.keyboard.Get(InputKey.LeftDon), Is.Empty);
            Assert.That(loaded.general.keyboard.Get(InputKey.RightDon), Is.EqualTo(new[] { Key.A, Key.S }));
            Assert.That(loaded.general.language, Is.EqualTo("ja"));
            loaded.general.keyboard.Set(InputKey.RightDon);
            Assert.That(old.general.keyboard.Get(InputKey.RightDon).Count, Is.EqualTo(2));
        }

        [Test]
        public void MalformedBindingsAreNormalizedAndExplicitKeysWinOverMissingDefaults()
        {
            var settings = GameSettings.FromJson("{\"general\":{\"keyboard\":{\"bindings\":[{\"action\":0,\"keys\":[19,19,0,9999]},{\"action\":99,\"keys\":[1]}]}}}");
            var bindings = settings.general.keyboard;
            var all = bindings.bindings.SelectMany(b => b.keys).ToArray();
            Assert.That(bindings.bindings.Length, Is.EqualTo(Enum.GetValues(typeof(InputKey)).Length));
            Assert.That(all, Is.Unique);
            Assert.That(all, Has.None.EqualTo(Key.None));
            Assert.That(all.All(k => Enum.IsDefined(typeof(Key), k)), Is.True);
            var partial = new KeyboardBindings { bindings = new[] { new KeyboardBindings.Binding(InputKey.LeftDon, Key.J) } };
            partial.Normalize();
            Assert.That(partial.Get(InputKey.LeftDon), Is.EqualTo(new[] { Key.J }));
            Assert.That(partial.Get(InputKey.RightDon), Is.Empty);
        }

        [Test]
        public void AssignedKeysMoveOwnersAndRuntimeBindingsMatchSavedSettings()
        {
            var bindings = new KeyboardBindings();
            bindings.Set(InputKey.LeftDon, Key.J, Key.A);
            Assert.That(bindings.Get(InputKey.RightDon), Is.Empty);
            bindings.Apply();
            Assert.That(InputManager.GetBinding(InputKey.LeftDon), Is.EqualTo(new[] { Key.J, Key.A }));
            Assert.That(InputManager.GetBinding(InputKey.RightDon), Is.Empty);
            InputManager.SetBinding(InputKey.RightDon, Key.A, Key.A, Key.None, (Key)999);
            Assert.That(InputManager.GetBinding(InputKey.LeftDon), Is.EqualTo(new[] { Key.J }));
            Assert.That(InputManager.GetBinding(InputKey.RightDon), Is.EqualTo(new[] { Key.A }));
            InputManager.ResetBindings();
            Assert.That(InputManager.GetBinding(InputKey.LeftDon), Is.EqualTo(new[] { Key.F }));
        }

        [Test]
        public void MenuAddsRemovesClearsResetsAndCancelsWithoutReservingKeys()
        {
            var menu = new SettingsMenu(SettingsMenu.Catalog(), new GameSettings());
            menu.Don(); menu.Ka(1); menu.Don();
            Assert.That(menu.CurrentItem.BindingKey, Is.EqualTo(InputKey.LeftDon));
            menu.Don();
            Assert.That(menu.CapturingKey, Is.True);
            Assert.That(menu.Ka(1), Is.EqualTo(SettingsMenu.Result.None));
            Assert.That(menu.Don(), Is.EqualTo(SettingsMenu.Result.None));
            Assert.That(menu.CaptureKey(Key.Escape), Is.EqualTo(SettingsMenu.Result.Changed));
            Assert.That(menu.Settings.general.keyboard.Get(InputKey.Back), Is.Empty);
            Assert.That(menu.Settings.general.keyboard.Get(InputKey.LeftDon), Is.EqualTo(new[] { Key.F, Key.Escape }));
            menu.TapChoice(3); // remove F
            Assert.That(menu.Settings.general.keyboard.Get(InputKey.LeftDon), Is.EqualTo(new[] { Key.Escape }));
            menu.TapChoice(1);
            Assert.That(menu.Settings.general.keyboard.Get(InputKey.LeftDon), Is.Empty);
            menu.TapChoice(2);
            Assert.That(menu.Settings.general.keyboard.Get(InputKey.LeftDon), Is.EqualTo(new[] { Key.F }));
            menu.TapChoice(0); menu.Back();
            Assert.That(menu.CapturingKey, Is.False);
            Assert.That(menu.CaptureKey(Key.A), Is.EqualTo(SettingsMenu.Result.None));
            Assert.That(menu.Focus, Is.EqualTo(SettingsFocus.Choice));
            menu.Back();
            Assert.That(menu.Focus, Is.EqualTo(SettingsFocus.Items));
        }
    }
}
