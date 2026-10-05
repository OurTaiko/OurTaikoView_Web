using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

namespace OurTaiko
{
    // Logical inputs. Scenes ask for these instead of reading physical keys.
    public enum InputKey
    {
        LeftDon, RightDon, LeftKa, RightKa,
        Confirm, Back, Pause, Restart, MenuLeft, MenuRight, MenuUp, MenuDown,
    }

    public readonly struct InputPress
    {
        public readonly InputKey Key;
        // Input System time (seconds, same base as InputState.currentTime).
        public readonly double Time;
        public InputPress(InputKey key, double time) { Key = key; Time = time; }
    }

    public static class InputKeyExtensions
    {
        public static bool IsDrum(this InputKey key) => key <= InputKey.RightKa;
        public static bool IsKa(this InputKey key) => key == InputKey.LeftKa || key == InputKey.RightKa;
        public static bool IsRight(this InputKey key) => key == InputKey.RightDon || key == InputKey.RightKa;
    }

    // Single entry point for game input, after MajdataPlay's IO/InputManager:
    // raw keyboard events are collected as they arrive; once per frame (before any scene Update)
    // they are merged with the touch/mouse presses on registered DrumPad zones and published
    // as one time-ordered list of presses plus per-key flags.
    public static class InputManager
    {
        static readonly int KeyCount = Enum.GetValues(typeof(InputKey)).Length;
        static readonly Key[][] bindings = new Key[KeyCount][];
        static readonly Dictionary<Key, InputKey> reverse = new Dictionary<Key, InputKey>();
        static readonly List<InputPress> pending = new List<InputPress>();
        static readonly List<InputPress> frame = new List<InputPress>();
        static readonly bool[] pressedThisFrame = new bool[KeyCount];
        static readonly bool[] held = new bool[KeyCount];
        static readonly List<DrumPad> pads = new List<DrumPad>();
        static bool installed;

        // Presses of this frame in the order they happened.
        public static IReadOnlyList<InputPress> PressesThisFrame => frame;
        public static bool GetKeyDown(InputKey key) => pressedThisFrame[(int)key];
        public static bool GetKey(InputKey key) => held[(int)key];

        static InputManager() => ResetBindings();

        public static void ResetBindings()
        {
            SetBinding(InputKey.LeftDon, Key.F);
            SetBinding(InputKey.RightDon, Key.J);
            SetBinding(InputKey.LeftKa, Key.D);
            SetBinding(InputKey.RightKa, Key.K);
            SetBinding(InputKey.Confirm, Key.Enter, Key.NumpadEnter);
            SetBinding(InputKey.Back, Key.Escape);
            SetBinding(InputKey.Pause, Key.Space);
            SetBinding(InputKey.Restart, Key.F1);
            SetBinding(InputKey.MenuLeft, Key.LeftArrow);
            SetBinding(InputKey.MenuRight, Key.RightArrow);
            SetBinding(InputKey.MenuUp, Key.UpArrow);
            SetBinding(InputKey.MenuDown, Key.DownArrow);
        }

        // A physical key drives at most one logical key; rebinding takes it from the old owner.
        public static void SetBinding(InputKey key, params Key[] keys)
        {
            foreach (var old in bindings[(int)key] ?? Array.Empty<Key>()) reverse.Remove(old);
            foreach (var physical in keys)
            {
                if (reverse.TryGetValue(physical, out var owner))
                    bindings[(int)owner] = Array.FindAll(bindings[(int)owner], k => k != physical);
                reverse[physical] = key;
            }
            bindings[(int)key] = (Key[])keys.Clone();
        }
        public static IReadOnlyList<Key> GetBinding(InputKey key) => bindings[(int)key];

        // On-screen drum zones, hit-tested against touches and the mouse in OnPreUpdate.
        internal static void RegisterPad(DrumPad pad) { if (!pads.Contains(pad)) pads.Add(pad); }
        internal static void UnregisterPad(DrumPad pad) => pads.Remove(pad);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            pending.Clear(); frame.Clear(); pads.Clear();
            Array.Clear(pressedThisFrame, 0, KeyCount); Array.Clear(held, 0, KeyCount);
        }

        internal static void Initialize()
        {
            if (!installed) { InputSystem.onEvent += OnInputEvent; installed = true; }
        }

        // Runs while the Input System processes events, before the key state is written,
        // so `isPressed` still holds the previous state and the call order is the press order.
        static void OnInputEvent(InputEventPtr eventPtr, InputDevice device)
        {
            if (!(device is Keyboard keyboard)) return;
            if (InputState.currentUpdateType == InputUpdateType.Editor) return;
            if (!eventPtr.IsA<StateEvent>() && !eventPtr.IsA<DeltaStateEvent>()) return;
            foreach (var pair in reverse)
            {
                KeyControl control = keyboard[pair.Key];
                if (control == null || control.isPressed) continue;
                if (control.ReadValueFromEvent(eventPtr, out float value) && control.IsValueConsideredPressed(value))
                    pending.Add(new InputPress(pair.Value, eventPtr.time));
            }
        }

        // Called once per frame by GameLoop, ahead of every scene script.
        internal static void OnPreUpdate()
        {
            frame.Clear(); frame.AddRange(pending); pending.Clear();
            Array.Clear(pressedThisFrame, 0, KeyCount);
            Array.Clear(held, 0, KeyCount);
            foreach (var press in frame) pressedThisFrame[(int)press.Key] = true;
            PollKeyboard();
            PollPads();
            SortFrame();
        }

        static void PollKeyboard()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            for (int i = 0; i < KeyCount; i++)
            {
                bool pressed = false;
                foreach (var physical in bindings[i])
                {
                    var control = keyboard[physical];
                    held[i] |= control.isPressed;
                    pressed |= control.wasPressedThisFrame;
                }
                // Fallback if an event was missed: keep the press, ordered after the recorded ones.
                if (pressed && !pressedThisFrame[i]) AddPress((InputKey)i, InputState.currentTime);
            }
        }

        static void PollPads()
        {
            if (pads.Count == 0) return;
            var touchscreen = Touchscreen.current;
            if (touchscreen != null)
                foreach (var touch in touchscreen.touches)
                    PollPointer(touch.press, touch.position.ReadValue(), touch.startTime.ReadValue());
            var mouse = Mouse.current;
            if (mouse != null) PollPointer(mouse.leftButton, mouse.position.ReadValue(), mouse.lastUpdateTime);
        }

        static void PollPointer(ButtonControl button, Vector2 position, double time)
        {
            bool pressed = button.wasPressedThisFrame;
            if (!pressed && !button.isPressed) return;
            foreach (var pad in pads)
            {
                if (!pad.TryHit(position, out var key)) continue;
                held[(int)key] = true;
                if (pressed) { AddPress(key, time); pad.Press(); }
                return;
            }
        }

        static void AddPress(InputKey key, double time)
        {
            pressedThisFrame[(int)key] = true;
            frame.Add(new InputPress(key, time));
        }

        // Stable insertion sort: keyboard and pointer presses interleave by the time they happened.
        static void SortFrame()
        {
            for (int i = 1; i < frame.Count; i++)
            {
                var press = frame[i];
                int j = i - 1;
                for (; j >= 0 && frame[j].Time > press.Time; j--) frame[j + 1] = frame[j];
                frame[j + 1] = press;
            }
        }
    }

}
