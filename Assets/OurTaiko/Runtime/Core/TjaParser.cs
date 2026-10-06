using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace OurTaiko
{
    // TJA time is measured relative to the audio start. Positive OFFSET starts the chart earlier.
    // Commands are kept between note slots, so a BPM/SCROLL change inside a measure stays in place.
    public static class TjaParser
    {
        static double Number(string value) => double.Parse(value, CultureInfo.InvariantCulture);
        static string Course(string value)
        {
            string[] names = { "Easy", "Normal", "Hard", "Oni", "Edit" };
            return int.TryParse(value, out int n) && n >= 0 && n < names.Length ? names[n] : value;
        }

        // One parse for every mode: how branches are played is PlaySession's choice.
        public static TaikoChart Parse(string text, string requestedCourse = "Oni")
        {
            if (string.IsNullOrWhiteSpace(text)) throw new FormatException("The TJA chart is empty.");
            var chart = new TaikoChart();
            var tokens = new List<string>();
            var balloons = new List<int>();
            string course = "Oni";
            int level = 0;
            bool reading = false, found = false, ended = false;
            foreach (string raw in text.TrimStart('\uFEFF').Replace("\r", "").Split('\n'))
            {
                string line = raw.Split(new[] { "//" }, StringSplitOptions.None)[0].Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("#START", StringComparison.OrdinalIgnoreCase))
                {
                    reading = string.Equals(Course(course), Course(requestedCourse), StringComparison.OrdinalIgnoreCase);
                    if (reading) { found = true; chart.Course = Course(course).Split('_')[0]; chart.Level = level; }
                    continue;
                }
                if (line.Equals("#END", StringComparison.OrdinalIgnoreCase)) { if (reading) { ended = true; break; } reading = false; continue; }
                if (reading)
                {
                    if (line[0] == '#') tokens.Add(line);
                    else
                    {
                        foreach (char c in line.Where(c => !char.IsWhiteSpace(c))) tokens.Add(c.ToString());
                    }
                    continue;
                }
                int colon = line.IndexOf(':');
                if (colon < 0) continue;
                string key = line.Substring(0, colon).Trim().ToUpperInvariant(), value = line.Substring(colon + 1).Trim();
                switch (key)
                {
                    case "TITLE": chart.Title = value; break;
                    case "SUBTITLE": chart.Subtitle = value.TrimStart('-', '+'); break;
                    case "BPM": chart.Bpm = Number(value); break;
                    case "OFFSET": chart.Offset = Number(value); break;
                    case "COURSE": course = value; balloons.Clear(); level = 0; break;
                    case "LEVEL": level = (int)Number(value); break;
                    // Match the reference parser's shared balloon pool and route cursor rewind.
                    case "BALLOONNOR": case "BALLOONEXP": balloons.AddRange(Balloons(value)); break;
                    case "BALLOONMAS": case "BALLOON": balloons = Balloons(value); break;
                }
            }
            if (!found || !ended) throw new FormatException($"No complete {requestedCourse} course (#START / #END).");
            if (!(chart.Bpm > 0) || double.IsInfinity(chart.Bpm)) throw new FormatException("BPM must be positive and finite.");
            new ChartReader(chart, balloons).Read(tokens);
            return chart;
        }

        static List<int> Balloons(string value) => value.Replace('.', ',').Split(',')
            .Where(s => s.Trim().Length > 0).Select(s => (int)Number(s)).ToList();

        sealed class TimingState
        {
            public double Time, Bpm, Measure = 1, ScrollX = 1, ScrollY;
            public bool Barline = true;
            public int BalloonIndex;
            public TimingState Copy() => (TimingState)MemberwiseClone();
        }

        sealed class ChartReader
        {
            readonly TaikoChart chart;
            readonly List<int> balloons;
            readonly List<string> pending = new List<string>();
            TimingState state, branchStart;
            ChartBranch branch;
            readonly bool[] routesSeen = new bool[3];
            BranchRoute route;
            bool hasRoute, branchBar, sectionBarPending;
            double? sectionBarTime;
            ChartNote longNote;
            readonly List<ChartEntry> common = new List<ChartEntry>();
            List<ChartEntry>[] routeLists;
            List<ChartEntry> Entries => branch == null ? common : routeLists[(int)route];

            public ChartReader(TaikoChart chart, List<int> balloons)
            {
                this.chart = chart; this.balloons = balloons;
                state = new TimingState { Time = -chart.Offset, Bpm = chart.Bpm };
                chart.NoteLists.Add(common);
            }

            public void Read(List<string> tokens)
            {
                foreach (string token in tokens)
                {
                    string key = token.Split(new[] { ' ', '\t' }, 2)[0].ToUpperInvariant();
                    if (token == ",") Flush();
                    else if (key == "#BRANCHSTART" || key == "#BRANCHEND" || key == "#N" || key == "#E" || key == "#M")
                    {
                        FlushCommands();
                        Command(token);
                    }
                    else pending.Add(token);
                }
                FlushCommands();
                if (branch != null) EndBranch(); // #BRANCHEND is optional before #END / the next branch.
                CheckLongNote();
                chart.Duration = Math.Max(chart.Duration, state.Time);
                chart.Notes.Sort((a, b) => a.Time.CompareTo(b.Time));
                chart.Bars.Sort((a, b) => a.Time.CompareTo(b.Time));
                var gogos = chart.Gogos.OrderBy(g => g.Time).ToList(); // stable, unlike List.Sort
                chart.Gogos.Clear(); chart.Gogos.AddRange(gogos);
                double previousDecision = double.NegativeInfinity;
                foreach (var checkpoint in chart.Branches)
                {
                    var first = checkpoint.FirstEntries[(int)BranchRoute.Master]
                        ?? checkpoint.FirstEntries[(int)BranchRoute.Expert] ?? checkpoint.FirstEntries[(int)BranchRoute.Normal];
                    // The original arms two measures early, then chooses when the first route
                    // object loads. A consecutive checkpoint cannot precede its predecessor.
                    checkpoint.DecisionTime = Math.Max(previousDecision,
                        Math.Max(checkpoint.ArmTime, first == null ? checkpoint.Time : NoteScroll.LoadTime(first)));
                    previousDecision = checkpoint.DecisionTime;
                }
                NoteMoji.Assign(chart);
            }

            void FlushCommands()
            {
                if (pending.Any(t => t[0] != '#')) throw new FormatException("A measure is missing its comma before a branch boundary or #END.");
                foreach (string token in pending) Command(token);
                pending.Clear();
            }

            void CheckLongNote()
            {
                if (longNote != null) throw new FormatException("Long note is missing its 8 tail before a route boundary or #END.");
            }

            void EndBranch()
            {
                CheckLongNote();
                if (!routesSeen[0]) throw new FormatException("A branch must define #N exactly once.");
                // Match Fanmade's image renderer: omitted E uses N; omitted M uses E.
                if (!routesSeen[1]) branch.Routes[1] = BranchRoute.Normal;
                if (!routesSeen[2]) branch.Routes[2] = branch.Routes[1];
                branch.EndTime = state.Time;
                // As in OurTaikoPlayer, common notes continue from the final authored route.
                branch = null; hasRoute = false; branchBar = false;
            }

            void StartBranch(string arg)
            {
                if (branch != null) EndBranch();
                CheckLongNote();
                string[] parts = arg.Split(',');
                if (parts.Length != 3) throw new FormatException("#BRANCHSTART requires condition, expert threshold, master threshold.");
                string condition = parts[0].Trim().ToLowerInvariant();
                // s (score) is recorded but never evaluated; only a fixed route can play it.
                if (condition != "p" && condition != "r" && condition != "s")
                    throw new NotSupportedException("Only p (accuracy), r (drumroll) and s (score) branch conditions are recognised.");
                double expert = Number(parts[1]), master = Number(parts[2]);
                if (double.IsNaN(expert) || double.IsInfinity(expert) || double.IsNaN(master) || double.IsInfinity(master))
                    throw new FormatException("Branch thresholds must be finite.");
                branchStart = state.Copy();
                branch = new ChartBranch { Id = chart.Branches.Count, Time = state.Time,
                    ArmTime = (sectionBarTime ?? state.Time) - 480.0 / state.Bpm,
                    Condition = condition == "p" ? BranchCondition.Accuracy : condition == "r" ? BranchCondition.Drumroll : BranchCondition.Score,
                    ExpertThreshold = expert, MasterThreshold = master };
                chart.Branches.Add(branch);
                routeLists = new[] { new List<ChartEntry>(), new List<ChartEntry>(), new List<ChartEntry>() };
                chart.NoteLists.AddRange(routeLists);
                sectionBarTime = null;
                Array.Clear(routesSeen, 0, routesSeen.Length);
                hasRoute = false;
            }

            void SelectRoute(BranchRoute selected)
            {
                if (branch == null) throw new FormatException("Route marker without #BRANCHSTART.");
                CheckLongNote();
                if (routesSeen[(int)selected]) throw new FormatException("Duplicate branch route: " + selected);
                routesSeen[(int)selected] = true;
                state = branchStart.Copy(); route = selected; hasRoute = true; branchBar = true;
            }

            ChartNote NewNote() => new ChartNote { Time = state.Time, EndTime = state.Time,
                Bpm = state.Bpm, ScrollX = state.ScrollX, ScrollY = state.ScrollY,
                BranchId = branch == null ? -1 : branch.Id, Route = route };

            void SetGogo(bool on)
            {
                chart.Gogos.Add(new ChartGogo { Time = state.Time, On = on, BranchId = branch == null ? -1 : branch.Id, Route = route });
            }

            void AddBar()
            {
                if (branch != null && !hasRoute) throw new FormatException("Branch notes must follow #N, #E or #M.");
                var bar = NewNote(); bar.Display = state.Barline; bar.IsBranchStart = branchBar;
                chart.Bars.Add(bar);
                Entries.Add(new ChartEntry(bar, false));
                if (branch != null && branch.FirstEntries[(int)route] == null) branch.FirstEntries[(int)route] = bar;
                branchBar = false;
                if (sectionBarPending) { sectionBarTime = state.Time; sectionBarPending = false; }
            }

            void Command(string command)
            {
                int space = command.IndexOfAny(new[] { ' ', '\t' });
                string key = (space < 0 ? command : command.Substring(0, space)).ToUpperInvariant();
                string arg = space < 0 ? "" : command.Substring(space + 1).Trim();
                switch (key)
                {
                    case "#BPMCHANGE": state.Bpm = Number(arg); if (!(state.Bpm > 0) || double.IsInfinity(state.Bpm)) throw new FormatException("Invalid BPMCHANGE."); break;
                    case "#MEASURE": var parts = arg.Split('/'); state.Measure = Number(parts[0]) / Number(parts[1]); if (!(state.Measure > 0) || double.IsInfinity(state.Measure)) throw new FormatException("Invalid MEASURE."); break;
                    case "#SCROLL":
                        var match = Regex.Match(arg, @"^([+-]?[\d.]+)([+-][\d.]+)i$");
                        state.ScrollX = match.Success ? Number(match.Groups[1].Value) : Number(arg);
                        state.ScrollY = match.Success ? Number(match.Groups[2].Value) : 0;
                        break;
                    case "#DELAY": state.Time += Number(arg); break;
                    case "#GOGOSTART": SetGogo(true); break;
                    case "#GOGOEND": SetGogo(false); break;
                    case "#BARLINEOFF": state.Barline = false; break;
                    case "#BARLINEON": state.Barline = true; break;
                    case "#BRANCHSTART": StartBranch(arg); break;
                    case "#BRANCHEND": if (branch == null) throw new FormatException("#BRANCHEND without #BRANCHSTART."); EndBranch(); break;
                    case "#N": SelectRoute(BranchRoute.Normal); break;
                    case "#E": SelectRoute(BranchRoute.Expert); break;
                    case "#M": SelectRoute(BranchRoute.Master); break;
                    case "#SECTION":
                        chart.Sections.Add(new ChartSection { Time = state.Time, BranchId = branch == null ? -1 : branch.Id, Route = route });
                        sectionBarPending = true;
                        break;
                    case "#LEVELHOLD": case "#BMSCROLL": case "#HBSCROLL":
                        throw new NotSupportedException($"{key} is not supported by the single-lane port yet.");
                    default: if (!chart.Warnings.Contains(key)) chart.Warnings.Add(key); break;
                }
            }

            void Flush()
            {
                int slots = pending.Count(t => t[0] != '#'), index = 0;
                bool addedBar = false;
                foreach (string token in pending)
                {
                    if (token[0] == '#') { Command(token); continue; }
                    if (!addedBar)
                    {
                        AddBar();
                        addedBar = true;
                    }
                    if (token[0] < '0' || token[0] > '9') throw new NotSupportedException($"Unsupported TJA note: {token}");
                    int type = token[0] - '0';
                    if (type == 8)
                    {
                        if (longNote == null) throw new FormatException("Long-note tail has no head.");
                        longNote.EndTime = state.Time;
                        longNote.TailBpm = state.Bpm;
                        Entries.Add(new ChartEntry(longNote, true));
                        longNote = null;
                    }
                    else if (type != 0)
                    {
                        if (longNote != null) throw new FormatException("Overlapping long notes are not supported.");
                        var note = NewNote(); note.Kind = (NoteKind)type;
                        if (note.IsBalloon) { note.BalloonHits = state.BalloonIndex < balloons.Count ? balloons[state.BalloonIndex] : 1; state.BalloonIndex++; }
                        chart.Notes.Add(note);
                        Entries.Add(new ChartEntry(note, false));
                        if (note.IsLong) longNote = note;
                    }
                    state.Time += 240.0 / state.Bpm * state.Measure / Math.Max(1, slots);
                    index++;
                }
                if (index == 0) { AddBar(); state.Time += 240.0 / state.Bpm * state.Measure; }
                chart.Duration = Math.Max(chart.Duration, state.Time);
                pending.Clear();
            }
        }
    }
}
