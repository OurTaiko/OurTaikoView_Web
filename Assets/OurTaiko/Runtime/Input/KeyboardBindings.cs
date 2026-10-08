using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.InputSystem;

namespace OurTaiko
{
    // General settings: keyboard controls are shared by menus and gameplay.
    [Serializable]
    public sealed class KeyboardBindings
    {
        [Serializable]
        public sealed class Binding
        {
            public InputKey action;
            public Key[] keys;
            public Binding(InputKey action, params Key[] keys) { this.action = action; this.keys = keys; }
        }

        public Binding[] bindings = Defaults();

        static Binding[] Defaults() => new[]
        {
            new Binding(InputKey.LeftDon, Key.F), new Binding(InputKey.RightDon, Key.J),
            new Binding(InputKey.LeftKa, Key.D), new Binding(InputKey.RightKa, Key.K),
            new Binding(InputKey.Confirm, Key.Enter, Key.NumpadEnter), new Binding(InputKey.Back, Key.Escape),
            new Binding(InputKey.Pause, Key.Space), new Binding(InputKey.Restart, Key.F1),
            new Binding(InputKey.MenuLeft, Key.LeftArrow), new Binding(InputKey.MenuRight, Key.RightArrow),
            new Binding(InputKey.MenuUp, Key.UpArrow), new Binding(InputKey.MenuDown, Key.DownArrow),
        };

        public static Key[] DefaultKeys(InputKey action) => Defaults().First(b => b.action == action).keys;
        public static Key[] Clean(IEnumerable<Key> keys) => (keys ?? Array.Empty<Key>())
            .Where(k => k != Key.None && Enum.IsDefined(typeof(Key), k)).Distinct().ToArray();
        public IReadOnlyList<Key> Get(InputKey action) => bindings.First(b => b.action == action).keys;

        // Explicit empty arrays stay empty. Missing entries inherit defaults; malformed and duplicate
        // physical keys are removed. Explicit assignments take priority over missing-entry defaults.
        public void Normalize()
        {
            var source = bindings ?? Array.Empty<Binding>();
            var result = new List<Binding>();
            var used = new HashSet<Key>();
            foreach (var binding in source)
            {
                if (binding == null || !Enum.IsDefined(typeof(InputKey), binding.action) || result.Any(b => b.action == binding.action)) continue;
                result.Add(new Binding(binding.action, Clean(binding.keys).Where(used.Add).ToArray()));
            }
            foreach (var binding in Defaults())
                if (!result.Any(b => b.action == binding.action))
                    result.Add(new Binding(binding.action, binding.keys.Where(used.Add).ToArray()));
            bindings = result.OrderBy(b => b.action).ToArray();
        }

        public void Set(InputKey action, params Key[] keys)
        {
            var clean = Clean(keys);
            foreach (var binding in bindings)
                binding.keys = binding.action == action ? clean : binding.keys.Where(k => !clean.Contains(k)).ToArray();
        }

        public void Apply()
        {
            foreach (InputKey action in Enum.GetValues(typeof(InputKey))) InputManager.SetBinding(action);
            foreach (var binding in bindings) InputManager.SetBinding(binding.action, binding.keys);
        }

        public string Format(InputKey action) => Get(action).Count == 0 ? "Unbound" : string.Join(" / ", Get(action));
        public static string Label(InputKey action) => action switch
        {
            InputKey.LeftDon => "1P Left Don", InputKey.RightDon => "1P Right Don",
            InputKey.LeftKa => "1P Left Ka", InputKey.RightKa => "1P Right Ka",
            InputKey.MenuLeft => "Menu Left", InputKey.MenuRight => "Menu Right",
            InputKey.MenuUp => "Menu Up", InputKey.MenuDown => "Menu Down",
            _ => action.ToString(),
        };
    }
}
