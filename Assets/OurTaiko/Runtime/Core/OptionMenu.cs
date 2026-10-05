using System;

namespace OurTaiko
{
    public enum OptionRow { Auto, Speed, Display, Inverse, Random, Skip, Neiro }

    // ModifierSelector (src/objects/song_select/modifier.cpp) with Nijiiro's option_skip_row and
    // option_neiro_row: ka changes the current row's value, don moves to the next row and closes the
    // panel after the last one. 演奏スキップ is greyed: it needs a free 2P lane, which single play lacks.
    public sealed class OptionMenu
    {
        public static readonly OptionRow[] Rows = (OptionRow[])Enum.GetValues(typeof(OptionRow));
        // song_select/animation.json 28 (in) and 39 (out): 548 px in 333.33 ms through a 75 px overshoot.
        public const double SlideMs = 333.33, SlideDistance = 548, Overshoot = 75;
        const double InPeak = 0.7368, OutPeak = 0.2105;

        readonly int soundCount;

        public PlayOptions Options { get; }
        public int Index { get; private set; }
        public bool IsConfirmed { get; private set; }
        public OptionRow Current => Rows[Math.Min(Index, Rows.Length - 1)];
        // 音色 slots: every hit-sound set, then 無音.
        public int NeiroSlots => soundCount + 1;
        public int NeiroSlot => Options.neiro < 0 || Options.neiro >= soundCount ? soundCount : Options.neiro;

        public OptionMenu(PlayOptions options, int hitSoundCount)
        {
            Options = options;
            soundCount = Math.Max(0, hitSoundCount);
            if (options.neiro >= soundCount) options.neiro = PlayOptions.Mute;
        }

        public static bool IsGreyed(OptionRow row) => row == OptionRow.Skip;

        public bool IsChanged(OptionRow row) => row switch
        {
            OptionRow.Auto => Options.auto,
            OptionRow.Speed => Options.speed != PlayOptions.DefaultSpeed,
            OptionRow.Display => Options.display,
            OptionRow.Inverse => Options.inverse,
            OptionRow.Random => Options.random != RandomMode.Off,
            OptionRow.Neiro => Options.neiro != 0,
            _ => false,
        };

        // Moves the cursor to a row (touch); the drum only ever walks forward.
        public void Select(int row)
        {
            if (!IsConfirmed && row >= 0 && row < Rows.Length) Index = row;
        }

        // Returns whether the value changed (greyed rows and a closed panel ignore it).
        public bool Left() => Change(-1);
        public bool Right() => Change(+1);

        bool Change(int direction)
        {
            if (IsConfirmed || IsGreyed(Current)) return false;
            switch (Current)
            {
                case OptionRow.Speed: Options.speed = PlayOptions.StepSpeed(Options.speed, direction); break;
                case OptionRow.Random: Options.random = (RandomMode)(((int)Options.random + direction + 3) % 3); break;
                case OptionRow.Neiro:
                    int slot = ((NeiroSlot + direction) % NeiroSlots + NeiroSlots) % NeiroSlots;
                    Options.neiro = slot == soundCount ? PlayOptions.Mute : slot;
                    break;
                case OptionRow.Auto: Options.auto = !Options.auto; break;
                case OptionRow.Display: Options.display = !Options.display; break;
                case OptionRow.Inverse: Options.inverse = !Options.inverse; break;
            }
            return true;
        }

        public void Confirm()
        {
            if (IsConfirmed) return;
            if (++Index == Rows.Length) IsConfirmed = true;
        }

        // Closes at once from any row (touch outside the panel, Esc).
        public void ConfirmAll()
        {
            if (IsConfirmed) return;
            Index = Rows.Length;
            IsConfirmed = true;
        }

        // Panel top lift from its off-screen y (1080) while sliding in: 0 -> 623 -> 548, piecewise linear.
        public static double SlideIn(double ms)
        {
            double peakMs = SlideMs * InPeak, peak = SlideDistance + Overshoot;
            if (ms <= 0) return 0;
            if (ms < peakMs) return peak * ms / peakMs;
            if (ms < SlideMs) return peak + (SlideDistance - peak) * (ms - peakMs) / (SlideMs - peakMs);
            return SlideDistance;
        }

        // Panel drop from its resting y while sliding out: 0 -> -75 -> 548.
        public static double SlideOut(double ms)
        {
            double peakMs = SlideMs * OutPeak;
            if (ms <= 0) return 0;
            if (ms < peakMs) return -Overshoot * ms / peakMs;
            if (ms < SlideMs) return -Overshoot + (SlideDistance + Overshoot) * (ms - peakMs) / (SlideMs - peakMs);
            return SlideDistance;
        }
    }
}
