using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

namespace OurTaiko
{
    // Logical inputs. Scenes ask for these instead of reading physical keys. The 2P drum is
    // keyboard only and appended, so saved bindings keep their values.
    public enum InputKey
    {
        LeftDon, RightDon, LeftKa, RightKa,
        Confirm, Back, Pause, Restart, MenuLeft, MenuRight, MenuUp, MenuDown,
        P2LeftDon, P2RightDon, P2LeftKa, P2RightKa,
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
        public const int Seats = 2;
        public static bool IsDrum(this InputKey key) => key <= InputKey.RightKa || key >= InputKey.P2LeftDon;
        public static bool IsKa(this InputKey key) => key.Face() == InputKey.LeftKa || key.Face() == InputKey.RightKa;
        public static bool IsRight(this InputKey key) => key.Face() == InputKey.RightDon || key.Face() == InputKey.RightKa;
        // 0 for 1P, 1 for the 2P drum; menu keys belong to 1P.
        public static int Seat(this InputKey key) => key >= InputKey.P2LeftDon ? 1 : 0;
        // The drum face as 1P names it (LeftDon..RightKa), whichever seat struck it.
        public static InputKey Face(this InputKey key) => key >= InputKey.P2LeftDon ? (InputKey)(key - InputKey.P2LeftDon) : key;
        // A seat's key for a drum face.
        public static InputKey OfSeat(this InputKey face, int seat) => seat == 1 && face <= InputKey.RightKa ? face + (int)InputKey.P2LeftDon : face;
    }

    // One seat's drum: its presses of the frame (keys named as 1P's faces) and its input lock.
    // Each seat judges its own earliest press per frame; the seats never block each other.
    public sealed class PlayerInput
    {
        public int Seat { get; }
        internal readonly List<InputPress> frame = new List<InputPress>();
        internal readonly List<InputPress> leads = new List<InputPress>();
        internal PlayerInput(int seat) => Seat = seat;

        // This seat's drum presses of this frame in the order they happened.
        public IReadOnlyList<InputPress> PressesThisFrame => frame;
        // InputManager.DrumLeadsThisFrame for this seat alone.
        public IReadOnlyList<InputPress> DrumLeadsThisFrame => leads;
        public bool GetKeyDown(InputKey face) => InputManager.GetKeyDown(face.OfSeat(Seat));
        public bool GetKey(InputKey face) => InputManager.GetKey(face.OfSeat(Seat));
        public bool DonDown => GetKeyDown(InputKey.LeftDon) || GetKeyDown(InputKey.RightDon);
    }

    // Single entry point for game input, after MajdataPlay's IO/InputManager:
    // raw keyboard events are collected as they arrive; once per frame (before any scene Update)
    // they are merged with the touch/mouse presses on registered DrumPad zones and published
    // as one time-ordered list of presses plus per-key flags. Players[seat] splits the drum
    // presses by seat; touch and mouse drum zones always strike 1P.
    public static class InputManager
    {
        static readonly int KeyCount = Enum.GetValues(typeof(InputKey)).Length;
        static readonly Key[][] bindings = new Key[KeyCount][];
        static readonly Dictionary<Key, InputKey> reverse = new Dictionary<Key, InputKey>();
        static readonly List<InputPress> pending = new List<InputPress>();
        static readonly List<InputPress> frame = new List<InputPress>();
        static readonly List<InputPress> leads = new List<InputPress>();
        // How many presses at the start of pending were already there last frame, waiting out DrumDelay.
        static int waiting;
        static readonly bool[] pressedThisFrame = new bool[KeyCount];
        // Keys whose press arrived as an event since the last frame, so the poll does not add it again.
        static readonly bool[] recorded = new bool[KeyCount];
        static readonly bool[] held = new bool[KeyCount];
        static readonly List<DrumPad> pads = new List<DrumPad>();
        static readonly PlayerInput[] players = { new PlayerInput(0), new PlayerInput(1) };
        static bool installed;

        public static IReadOnlyList<PlayerInput> Players => players;
        public static PlayerInput P1 => players[0];
        public static PlayerInput P2 => players[1];

        // Presses of this frame in the order they happened.
        public static IReadOnlyList<InputPress> PressesThisFrame => frame;
        public static bool GetKeyDown(InputKey key) => pressedThisFrame[(int)key];
        public static bool GetKey(InputKey key) => held[(int)key];
        // Seconds a drum press waits before it is published, like the arcade's input delay.
        // The play scene sets it for its lifetime; other keys and GetKey are never delayed.
        public static double DrumDelay { get; set; }
        // Drum presses first seen this frame that will be the earliest drum press of their seat in the
        // frame that publishes them, so a scene can sound them before the delay ends. That frame is
        // certain while DrumDelay is no longer than a frame; with a longer delay every later frame
        // counts as one and a press may be left out here although it ends up first in its frame.
        public static IReadOnlyList<InputPress> DrumLeadsThisFrame => leads;

        static InputManager() => ResetBindings();

        public static void ResetBindings()
        {
            foreach (InputKey key in Enum.GetValues(typeof(InputKey)))
                SetBinding(key, KeyboardBindings.DefaultKeys(key));
        }

        // A physical key drives at most one logical key; rebinding takes it from the old owner.
        public static void SetBinding(InputKey key, params Key[] keys)
        {
            keys = KeyboardBindings.Clean(keys);
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
            pending.Clear(); frame.Clear(); pads.Clear(); leads.Clear(); waiting = 0;
            foreach (var player in players) { player.frame.Clear(); player.leads.Clear(); }
            Array.Clear(pressedThisFrame, 0, KeyCount); Array.Clear(held, 0, KeyCount);
            Array.Clear(recorded, 0, KeyCount);
            DrumDelay = 0;
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
                {
                    pending.Add(new InputPress(pair.Value, eventPtr.time));
                    recorded[(int)pair.Value] = true;
                }
            }
        }

        // Called once per frame by GameLoop, ahead of every scene script.
        internal static void OnPreUpdate()
        {
            frame.Clear();
            Array.Clear(pressedThisFrame, 0, KeyCount);
            Array.Clear(held, 0, KeyCount);
            PollKeyboard();
            PollPads();
            Array.Clear(recorded, 0, KeyCount);
            double now = InputState.currentTime;
            FindDrumLeads(now);
            PublishDue(now);
            Sort(frame, 0);
            SplitBySeat();
        }

        static void SplitBySeat()
        {
            foreach (var player in players) { player.frame.Clear(); player.leads.Clear(); }
            foreach (var press in frame)
                if (press.Key.IsDrum()) players[press.Key.Seat()].frame.Add(new InputPress(press.Key.Face(), press.Time));
            foreach (var press in leads)
                players[press.Key.Seat()].leads.Add(new InputPress(press.Key.Face(), press.Time));
        }

        static bool IsDue(InputPress press, double now) => !press.Key.IsDrum() || now - press.Time >= DrumDelay;

        static void FindDrumLeads(double now)
        {
            leads.Clear();
            // A press that was already waiting comes before any new one in the frame that publishes it.
            // Each seat has its own lock.
            Span<bool> takenNow = stackalloc bool[InputKeyExtensions.Seats], takenLater = stackalloc bool[InputKeyExtensions.Seats];
            for (int i = 0; i < waiting; i++)
            {
                if (!pending[i].Key.IsDrum()) continue;
                int seat = pending[i].Key.Seat();
                if (IsDue(pending[i], now)) takenNow[seat] = true; else takenLater[seat] = true;
            }
            Sort(pending, waiting);
            for (int i = waiting; i < pending.Count; i++)
            {
                var press = pending[i];
                if (!press.Key.IsDrum()) continue;
                int seat = press.Key.Seat();
                bool due = IsDue(press, now);
                if (!(due ? takenNow[seat] : takenLater[seat])) leads.Add(press);
                if (due) takenNow[seat] = true; else takenLater[seat] = true;
            }
        }

        // Forgets the drum presses still waiting out DrumDelay.
        public static void DropDelayed()
        {
            pending.RemoveAll(press => press.Key.IsDrum());
            waiting = 0;
        }

        // Moves the presses whose delay has passed from pending to this frame; the rest wait.
        static void PublishDue(double now)
        {
            int kept = 0;
            for (int i = 0; i < pending.Count; i++)
            {
                var press = pending[i];
                if (!IsDue(press, now)) { pending[kept++] = press; continue; }
                pressedThisFrame[(int)press.Key] = true;
                frame.Add(press);
            }
            pending.RemoveRange(kept, pending.Count - kept);
            waiting = kept;
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
                if (pressed && !recorded[i]) pending.Add(new InputPress((InputKey)i, InputState.currentTime));
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
                if (pressed) { pending.Add(new InputPress(key, time)); pad.Press(); }
                return;
            }
        }

        // Stable insertion sort: keyboard and pointer presses interleave by the time they happened.
        static void Sort(List<InputPress> presses, int start)
        {
            for (int i = start + 1; i < presses.Count; i++)
            {
                var press = presses[i];
                int j = i - 1;
                for (; j >= start && presses[j].Time > press.Time; j--) presses[j + 1] = presses[j];
                presses[j + 1] = press;
            }
        }
    }

}
