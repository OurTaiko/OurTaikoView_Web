using System.Collections.Generic;
using System.Linq;

namespace OurTaiko
{
    // Course-select cursor of the original single-player song select
    // (src/objects/song_select/player.cpp + difficulty_selection.h). The NEIRO panel is not ported,
    // so the cursor follows the option_neiro_row layout: first course <-> MODIFIER <-> BACK.
    public sealed class DifficultyCursor
    {
        const int UraPresses = 10;
        readonly List<Difficulty> courses;
        int uraToggle;

        public Difficulty Selected { get; private set; }
        public bool IsUra { get; private set; }
        public List<Difficulty> Visible => VisibleCourses(courses, IsUra);

        public DifficultyCursor(IEnumerable<Difficulty> courses, bool preferUra, int lastDifficulty)
        {
            this.courses = courses.OrderBy(d => d).ToList();
            IsUra = UraMode(this.courses, preferUra);
            Selected = Initial(VisibleCourses(this.courses, IsUra), lastDifficulty);
        }

        public static bool UraMode(IList<Difficulty> courses, bool preferred)
            => courses.Contains(Difficulty.Ura) && (preferred || !courses.Contains(Difficulty.Oni));

        // Oni and Edit share a column; a standalone Edit is always visible.
        public static List<Difficulty> VisibleCourses(IList<Difficulty> courses, bool ura)
        {
            ura = UraMode(courses, ura);
            return courses.Where(d => !(d == Difficulty.Oni && ura) && !(d == Difficulty.Ura && !ura)).ToList();
        }

        public static Difficulty Initial(IList<Difficulty> visible, int last)
        {
            if (last < (int)Difficulty.Easy) return Difficulty.Back;
            var desired = (Difficulty)System.Math.Min(last, (int)Difficulty.Oni);
            var pick = Difficulty.Back;
            foreach (var d in visible)
                if (d >= Difficulty.Easy && d <= Difficulty.Ura && (d > Difficulty.Oni ? Difficulty.Oni : d) <= desired) pick = d;
            if (pick == Difficulty.Back)
                foreach (var d in visible)
                    if (d >= Difficulty.Easy && d <= Difficulty.Ura) return d;
            return pick;
        }

        public void Left()
        {
            var visible = Visible;
            if (Selected == Difficulty.Modifier) Selected = Difficulty.Back;
            else if (Selected == Difficulty.Back) { }
            else if (visible.Count == 0) Selected = Difficulty.Modifier;
            else if (!visible.Contains(Selected)) Selected = visible[0];
            else if (Selected == visible[0]) Selected = Difficulty.Modifier;
            else Selected = visible[visible.IndexOf(Selected) - 1];
        }

        // Returns true when the tenth consecutive right press on Oni/Edit flips the column.
        public bool Right()
        {
            var visible = Visible;
            bool hasUra = courses.Contains(Difficulty.Ura), hasOni = courses.Contains(Difficulty.Oni);
            if ((Selected == Difficulty.Oni || Selected == Difficulty.Ura) && hasUra && hasOni)
            {
                uraToggle = (uraToggle + 1) % UraPresses;
                if (uraToggle == 0) return TryToggleUra();
            }
            else if (Selected == Difficulty.Modifier) { if (visible.Count > 0) Selected = visible[0]; }
            else if (Selected == Difficulty.Back) Selected = Difficulty.Modifier;
            else if (visible.Count > 0)
            {
                int index = visible.IndexOf(Selected);
                if (index >= 0 && index == visible.Count - 1) return false;
                Selected = index < 0 ? visible[0] : visible[index + 1];
            }
            return false;
        }

        public bool TryToggleUra()
        {
            if (!courses.Contains(Difficulty.Oni) || !courses.Contains(Difficulty.Ura)) return false;
            uraToggle = 0;
            IsUra = !IsUra;
            Selected = IsUra ? Difficulty.Ura : Difficulty.Oni;
            return true;
        }
    }
}
