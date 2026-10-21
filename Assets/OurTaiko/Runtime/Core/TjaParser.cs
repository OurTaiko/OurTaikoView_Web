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
        static double Number(string value) => TjaNumber.Parse(value);
        // A numeric COURSE (0–4) by its name; any other value as written.
        static string Course(string value)
            => int.TryParse(value, out int n) && n >= 0 && n < SongInfo.CourseNames.Count ? SongInfo.CourseNames[n] : value;

        static bool SameCourse(string course, string requestedCourse)
            => string.Equals(Course(course), Course(requestedCourse), StringComparison.OrdinalIgnoreCase);

        // The sides (#START players) a course's blocks have, in file order.
        static List<string> Sides(string text, string requestedCourse)
        {
            var sides = new List<string>();
            string course = "Oni";
            foreach (var line in TjaLines.Read(text))
            {
                if (line.Kind == TjaLineKind.Header && line.Key == "COURSE") course = line.Value;
                else if (line.Kind == TjaLineKind.Start && SameCourse(course, requestedCourse)) sides.Add(ChartSide.OfStart(line.Value));
            }
            return sides;
        }

        // One parse for every mode: how branches are played is PlaySession's choice. The seat
        // ("p1" or "p2") picks which of a course's blocks is played (ChartSide), as the online
        // playable copy does; SongInfo lists the same one.
        public static TaikoChart Parse(string text, string requestedCourse = "Oni", string seat = "p1")
        {
            if (string.IsNullOrWhiteSpace(text)) throw new FormatException("The TJA chart is empty.");
            string side = ChartSide.Pick(seat, Sides(text, requestedCourse));
            var chart = new TaikoChart();
            var tokens = new List<string>();
            // Balloon hits per route (Normal, Expert, Master); headers before any COURSE are
            // every course's defaults, and each COURSE starts again from them.
            var globalBalloons = NewBalloonLists();
            var balloons = NewBalloonLists();
            bool seenCourse = false;
            string course = "Oni";
            int level = 0;
            bool reading = false, found = false, ended = false;
            // A scroll command after the course's COURSE: and before its #START sets how it begins.
            ScrollMode startMode = ScrollMode.Normal;
            // The first block of the requested course on the chosen side is played.
            foreach (var line in TjaLines.Read(text))
            {
                if (line.Kind == TjaLineKind.Start)
                {
                    reading = side != null && SameCourse(course, requestedCourse) && ChartSide.OfStart(line.Value) == side;
                    if (reading) { found = true; chart.Course = Course(course).Split('_')[0]; chart.Level = level; }
                    continue;
                }
                if (line.Kind == TjaLineKind.End)
                {
                    if (!reading) continue;
                    if (line.Implicit) chart.Warnings.Add("#END missing");
                    ended = true; break;
                }
                if (line.Kind == TjaLineKind.Body)
                {
                    if (!reading) continue;
                    if (line.Text[0] == '#') tokens.Add(line.Text);
                    else foreach (char c in line.Text.Where(c => !char.IsWhiteSpace(c))) tokens.Add(c.ToString());
                    continue;
                }
                if (line.Kind == TjaLineKind.Other)
                {
                    if (seenCourse && ScrollModeOf(line.Text) is ScrollMode mode) startMode = mode;
                    continue;
                }
                if (line.Kind != TjaLineKind.Header) continue;
                string value = line.Value;
                switch (line.Key)
                {
                    case "TITLE": chart.Title = value; break;
                    case "SUBTITLE": chart.Subtitle = value.TrimStart('-', '+'); break;
                    case "BPM": chart.Bpm = Number(value); break;
                    // OpenTaiko keeps OFFSET in whole milliseconds, cut toward zero.
                    case "OFFSET": chart.Offset = (int)(Number(value) * 1000) / 1000.0; break;
                    case "COURSE":
                        course = value; level = 0; seenCourse = true; startMode = ScrollMode.Normal;
                        for (int r = 0; r < 3; r++) balloons[r] = new List<int>(globalBalloons[r]);
                        break;
                    case "LEVEL": level = (int)Number(value); break;
                    // OpenTaiko: BALLOON is the Normal route's list; each header replaces its route's list.
                    case "BALLOON": case "BALLOONNOR": case "BALLOONEXP": case "BALLOONMAS":
                        int route = line.Key == "BALLOONEXP" ? 1 : line.Key == "BALLOONMAS" ? 2 : 0;
                        if (!Balloons(value, out var hits)) { if (!chart.Warnings.Contains(line.Text)) chart.Warnings.Add(line.Text); break; }
                        balloons[route] = hits;
                        if (!seenCourse) globalBalloons[route] = new List<int>(hits);
                        break;
                }
            }
            if (!found || !ended) throw new FormatException($"No {requestedCourse} course (#START) for the {seat} seat.");
            if (!(chart.Bpm > 0) || double.IsInfinity(chart.Bpm)) throw new FormatException("BPM must be positive and finite.");
            new ChartReader(chart, balloons, startMode).Read(tokens);
            return chart;
        }

        // OpenTaiko #SCROLL (ParseReal / ParseComplex): a real number, or with an `i` a complex one
        // whose imaginary part is the vertical speed: `1+2i`, `1 + 2i`, `1 2i`, `2i`, `i`, `-i`,
        // exponents and `inf`. A positive imaginary part comes from below the judge.
        static readonly Regex ComplexParts = new Regex(@"(?<=[^e\s])\s*(?=[+-])|(?<=[^e\s])\s+(?=[^e\s])", RegexOptions.IgnoreCase);
        static readonly Regex Infinity = new Regex(@"inf(?:inity)?", RegexOptions.IgnoreCase);

        internal static bool Scroll(string value, out double x, out double y)
        {
            x = 0; y = 0;
            if (value.IndexOf('i') < 0) return TjaNumber.TryParse(Infinity.Replace(value, "Infinity"), out x);
            string[] parts = ComplexParts.Split(value);
            var ri = new double[2];
            int first = parts[0].EndsWith("i") ? 1 : 0;
            for (int k = 0, n = Math.Min(2 - first, parts.Length); k < n; k++)
            {
                string part = Infinity.Replace(parts[k], "Infinity").Replace(" ", "");
                if (first + k == 1 && part.EndsWith("i"))
                {
                    part = part.TrimEnd('i');
                    if (part.Length == 0 || part[part.Length - 1] == '+' || part[part.Length - 1] == '-') part += "1";
                }
                if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out ri[first + k])) return false;
            }
            x = ri[0]; y = ri[1];
            return true;
        }

        static ScrollMode? ScrollModeOf(string command)
        {
            string key = command.Split(new[] { ' ', '\t' }, 2)[0].ToUpperInvariant();
            return key == "#NMSCROLL" ? ScrollMode.Normal : key == "#BMSCROLL" ? ScrollMode.BMScroll
                : key == "#HBSCROLL" ? ScrollMode.HBScroll : (ScrollMode?)null;
        }

        static List<int>[] NewBalloonLists() => new[] { new List<int>(), new List<int>(), new List<int>() };

        // OpenTaiko ParseBalloon: comma-separated whole numbers, read up to the first empty item; a
        // number that does not read (such as `5.10`) rejects the whole line.
        static bool Balloons(string value, out List<int> hits)
        {
            hits = new List<int>();
            foreach (string item in value.Split(','))
            {
                if (item.Length == 0) break;
                if (!int.TryParse(item, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) return false;
                hits.Add(n);
            }
            return true;
        }

        sealed class TimingState
        {
            // RawMs and Clock16 are OpenTaiko's dbNowTime and dbNowBMScollTime: milliseconds from
            // #START and sixteenth notes, accumulated in its order of operations.
            public double RawMs, Beat, Bpm, Measure = 1, ScrollX = 1, ScrollY, Clock16;
            public double SuddenShow = double.PositiveInfinity, SuddenMove = double.PositiveInfinity;
            public bool Barline = true;
            public ScrollMode Mode;
            public TimingState Copy() => (TimingState)MemberwiseClone();
        }

        sealed class ChartReader
        {
            readonly TaikoChart chart;
            readonly long offsetMs;
            // The cursor's time: OpenTaiko's (int)dbNowTime, shifted by OFFSET, in seconds.
            double Now => ((long)state.RawMs - offsetMs) / 1000.0;
            // Each route reads its own list in order. A common balloon takes the Normal list's
            // number (the one played on every route) and moves every route's place past it.
            readonly List<int>[] balloons;
            readonly int[] balloonIndex = new int[3];
            readonly List<string> pending = new List<string>();
            TimingState state, branchStart;
            ChartBranch branch;
            readonly bool[] routesSeen = new bool[3];
            BranchRoute route;
            bool hasRoute, branchBar;
            // Per route, the latest bar line and the latest long-note end after it, for
            // OpenTaiko's branch judgement time (CTja.GetBranchJudgeChipTime).
            readonly double?[] lastBar = new double?[3], rollEndAfterBar = new double?[3];
            ChartNote longNote;
            readonly List<ChartEntry> common = new List<ChartEntry>();
            List<ChartEntry>[] routeLists;
            List<ChartEntry> Entries => branch == null ? common : routeLists[(int)route];

            public ChartReader(TaikoChart chart, List<int>[] balloons, ScrollMode mode)
            {
                this.chart = chart; this.balloons = balloons;
                offsetMs = (long)Math.Round(chart.Offset * 1000);
                state = new TimingState { Bpm = chart.Bpm, Mode = mode };
                chart.NoteLists.Add(common);
                AddClockPoint();
            }

            // A BPM point of the beat clock where the cursor is, on the current route or, outside a
            // route, on all of them.
            void AddClockPoint() => chart.ScrollClocks.Add(new ChartClockPoint { Time = Now, RawMs = state.RawMs, Clock = state.Clock16 / 16,
                Bpm = state.Bpm, BranchId = branch != null && hasRoute ? branch.Id : -1, Route = route });

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
                // OpenTaiko reads notes after the last comma as a measure of their own.
                if (pending.Any(t => t[0] != '#')) { chart.Warnings.Add("Last measure without a comma"); Flush(); }
                else FlushCommands();
                if (branch != null) EndBranch(); // #BRANCHEND is optional before #END / the next branch.
                CheckLongNote();
                chart.Duration = Math.Max(chart.Duration, Now);
                chart.Notes.Sort((a, b) => a.Time.CompareTo(b.Time));
                chart.Bars.Sort((a, b) => a.Time.CompareTo(b.Time));
                var gogos = chart.Gogos.OrderBy(g => g.Time).ToList(); // stable, unlike List.Sort
                chart.Gogos.Clear(); chart.Gogos.AddRange(gogos);
                var tempos = chart.Tempos.OrderBy(t => t.Time).ToList();
                chart.Tempos.Clear(); chart.Tempos.AddRange(tempos);
                var clocks = chart.ScrollClocks.OrderBy(c => c.Time).ToList();
                chart.ScrollClocks.Clear(); chart.ScrollClocks.AddRange(clocks);
                var moves = chart.JudgeMoves.OrderBy(m => m.Time).ToList();
                chart.JudgeMoves.Clear(); chart.JudgeMoves.AddRange(moves);
                JudgeShift.Link(chart.JudgeMoves);
                NoteMoji.Assign(chart);
            }

            void FlushCommands()
            {
                if (pending.Any(t => t[0] != '#')) throw new FormatException("A measure is missing its comma before a branch boundary.");
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
                branch.EndTime = Now;
                // As in OurTaikoPlayer, common notes continue from the final authored route.
                branch = null; hasRoute = false; branchBar = false;
                // OpenTaiko GotoBranchEnd: every route's beat clock continues from here.
                AddClockPoint();
            }

            void StartBranch(string arg)
            {
                if (branch != null) EndBranch();
                CheckLongNote();
                AddClockPoint();
                branchStart = state.Copy();
                branch = new ChartBranch { Id = chart.Branches.Count, Time = Now };
                ReadCondition(branch, arg);
                branch.DecisionTime = JudgeTime(branch.Condition == BranchCondition.Drumroll || branch.Condition == BranchCondition.BalloonHits);
                chart.Branches.Add(branch);
                routeLists = new[] { new List<ChartEntry>(), new List<ChartEntry>(), new List<ChartEntry>() };
                chart.NoteLists.AddRange(routeLists);
                Array.Clear(routesSeen, 0, routesSeen.Length);
                hasRoute = false;
            }

            // OpenTaiko judges a branch at the latest bar line before it (the start of the measure
            // before #BRANCHSTART), or four beats before the chart if there is none. For drumroll
            // and balloon conditions it waits for a roll ending in that last measure. Never after
            // the branch itself.
            double JudgeTime(bool waitForRolls)
            {
                double judge = lastBar.Max() ?? ((long)(-240000.0 / chart.Bpm) - offsetMs) / 1000.0;
                double? rollEnd = rollEndAfterBar.Max();
                if (waitForRolls && rollEnd > judge) judge = Math.Min(rollEnd.Value, Now);
                return judge;
            }

            // Marks the current route, or every route outside a branch.
            void Track(double?[] perRoute, double? value, bool latest = false)
            {
                for (int r = 0; r < 3; r++)
                    if (branch == null || r == (int)route) perRoute[r] = latest ? Math.Max(perRoute[r] ?? value.Value, value.Value) : value;
            }

            // OpenTaiko CTja #BRANCHSTART: `type, expert, master[, l|m]`; a missing comma after the
            // type is tolerated. An empty or unreadable condition keeps the current route; unlike
            // OpenTaiko, which drops the whole command, an unknown type or range does the same.
            void ReadCondition(ChartBranch target, string arg)
            {
                target.Condition = BranchCondition.None;
                arg = Regex.Replace(arg, @"(?<=^\s*[a-zA-Z]+)\s*(?=\d)", ",");
                if (string.IsNullOrWhiteSpace(arg)) return;
                string[] parts = arg.Split(',').Select(p => p.Trim()).ToArray();
                if (parts.Length < 3 || parts[0].Length == 0
                    || !TjaNumber.TryParse(parts[1], out double expert) || !TjaNumber.TryParse(parts[2], out double master))
                {
                    Warn("#BRANCHSTART " + arg);
                    return;
                }
                bool upper = char.IsUpper(parts[0][0]);
                BranchCondition? condition = parts[0].ToLowerInvariant() switch
                {
                    "p" => BranchCondition.Accuracy, "pp" => BranchCondition.PercentPerfect,
                    "jp" => BranchCondition.JudgePerfect, "d" => BranchCondition.JudgePerfect,
                    "jg" => BranchCondition.JudgeGood, "jb" => BranchCondition.JudgeBad,
                    "r" => BranchCondition.Drumroll, "rb" => BranchCondition.BalloonHits,
                    "s" => BranchCondition.Score, _ => null,
                };
                string range = parts.Length > 3 ? parts[3] : "";
                if (condition == null || range != "" && range != "l" && range != "m")
                {
                    Warn("#BRANCHSTART " + arg);
                    return;
                }
                target.Condition = condition.Value;
                // s is never big-only; d always is.
                target.BigOnly = parts[0].ToLowerInvariant() == "d" || upper && condition != BranchCondition.Score;
                target.Less = range == "l";
                target.ExpertThreshold = expert; target.MasterThreshold = master;
            }

            void Warn(string warning)
            {
                if (!chart.Warnings.Contains(warning)) chart.Warnings.Add(warning);
            }

            void SelectRoute(BranchRoute selected)
            {
                if (branch == null) throw new FormatException("Route marker without #BRANCHSTART.");
                CheckLongNote();
                if (routesSeen[(int)selected]) throw new FormatException("Duplicate branch route: " + selected);
                routesSeen[(int)selected] = true;
                state = branchStart.Copy(); route = selected; hasRoute = true; branchBar = true;
            }

            ChartNote NewNote() => new ChartNote { Time = Now, EndTime = Now,
                Beat = state.Beat, Bpm = state.Bpm, ScrollX = state.ScrollX, ScrollY = state.ScrollY,
                ScrollMode = state.Mode, ScrollClock = state.Clock16 / 16, EndScrollClock = state.Clock16 / 16,
                SuddenShow = state.SuddenShow, SuddenMove = state.SuddenMove,
                BranchId = branch == null ? -1 : branch.Id, Route = route };

            // A route without a list, or past its end, gives 5 hits (OpenTaiko's default for an
            // empty list; past the end it leaves 0, which no balloon can be popped at).
            int NextBalloon(int r)
            {
                var list = balloons[r];
                int i = balloonIndex[r]++;
                return i < list.Count ? list[i] : 5;
            }

            void SetGogo(bool on)
            {
                chart.Gogos.Add(new ChartGogo { Time = Now, On = on, BranchId = branch == null ? -1 : branch.Id, Route = route });
            }

            void AddBar()
            {
                if (branch != null && !hasRoute) throw new FormatException("Branch notes must follow #N, #E or #M.");
                var bar = NewNote(); bar.Display = state.Barline; bar.IsBranchStart = branchBar;
                bar.SuddenShow = bar.SuddenMove = double.PositiveInfinity;
                chart.Bars.Add(bar);
                Entries.Add(new ChartEntry(bar, false));
                if (branch != null && branch.FirstEntries[(int)route] == null) branch.FirstEntries[(int)route] = bar;
                branchBar = false;
                Track(lastBar, Now);
                Track(rollEndAfterBar, null);
            }

            void Command(string command)
            {
                int space = command.IndexOfAny(new[] { ' ', '\t' });
                string key = (space < 0 ? command : command.Substring(0, space)).ToUpperInvariant();
                string arg = space < 0 ? "" : command.Substring(space + 1).Trim();
                switch (key)
                {
                    case "#BPMCHANGE":
                        state.Bpm = Number(arg);
                        if (!(state.Bpm > 0) || double.IsInfinity(state.Bpm)) throw new FormatException("Invalid BPMCHANGE.");
                        chart.Tempos.Add(new ChartTempo { Time = Now, ResumeTime = Now, Beat = state.Beat, Bpm = state.Bpm,
                            BranchId = branch == null ? -1 : branch.Id, Route = route });
                        AddClockPoint();
                        break;
                    // OpenTaiko keeps the two numbers as floats and divides them as floats.
                    case "#MEASURE": var parts = arg.Split('/'); state.Measure = (float)Number(parts[0]) / (float)Number(parts[1]); if (!(state.Measure > 0) || double.IsInfinity(state.Measure)) throw new FormatException("Invalid MEASURE."); break;
                    case "#SCROLL":
                        // An unreadable SCROLL keeps the current one (OpenTaiko drops the command).
                        if (Scroll(arg, out double scrollX, out double scrollY)) { state.ScrollX = scrollX; state.ScrollY = scrollY; }
                        else if (!chart.Warnings.Contains(command)) chart.Warnings.Add(command);
                        break;
                    case "#DELAY":
                        double delay = Number(arg);
                        double start = Now;
                        state.RawMs += delay * 1000;
                        chart.Tempos.Add(new ChartTempo { Time = start, ResumeTime = Now,
                            Beat = state.Beat, Bpm = state.Bpm, BranchId = branch == null ? -1 : branch.Id, Route = route });
                        // The beat clock keeps running through a delay (OpenTaiko dbNowBMScollTime).
                        state.Clock16 += delay * 1000 * state.Bpm / 15000;
                        break;
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
                        chart.Sections.Add(new ChartSection { Time = Now, BranchId = branch == null ? -1 : branch.Id, Route = route });
                        break;
                    // OpenTaiko: inside a route it holds that route once the branch chooses it;
                    // anywhere else it holds the route being played from its time.
                    case "#LEVELHOLD":
                        if (branch != null && hasRoute) branch.HoldRoutes[(int)route] = true;
                        else chart.LevelHolds.Add(Now);
                        break;
                    case "#NMSCROLL": case "#BMSCROLL": case "#HBSCROLL": state.Mode = ScrollModeOf(key).Value; break;
                    // OpenTaiko: `#JPOSSCROLL seconds move [direction]`; move is a real (across) or a
                    // complex number (across and down) in 1280-wide pixels; direction 0 (the default)
                    // turns it around, as notes move. Moves on every route count.
                    case "#JPOSSCROLL":
                        var jpos = arg.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        int jposDirection = 0;
                        if (jpos.Length < 2 || !TjaNumber.TryParse(Infinity.Replace(jpos[0], "Infinity"), out double jposSeconds)
                            || !Scroll(jpos[1], out double jposX, out double jposY)
                            || jpos.Length >= 3 && !int.TryParse(jpos[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out jposDirection))
                        {
                            if (!chart.Warnings.Contains(command)) chart.Warnings.Add(command);
                            break;
                        }
                        if (jposDirection == 0) { jposX = -jposX; jposY = -jposY; }
                        chart.JudgeMoves.Add(new ChartJudgeMove { Time = Now, Duration = Math.Max(0, 1000 * jposSeconds), MoveX = jposX, MoveY = jposY });
                        break;
                    // OpenTaiko: `#SUDDEN show move` in seconds, both required. Less than a
                    // millisecond means unset; the move time is cut to whole milliseconds.
                    case "#SUDDEN":
                        var times = arg.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (times.Length < 2 || !TjaNumber.TryParse(times[0], out double show) || !TjaNumber.TryParse(times[1], out double move))
                        {
                            if (!chart.Warnings.Contains(command)) chart.Warnings.Add(command);
                            break;
                        }
                        state.SuddenShow = Math.Abs(Math.Truncate(show * 1000)) >= 1 ? show : double.PositiveInfinity;
                        state.SuddenMove = Math.Abs(Math.Truncate(move * 1000)) >= 1 ? Math.Truncate(move * 1000) / 1000 : double.PositiveInfinity;
                        break;
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
                        longNote.EndTime = Now;
                        longNote.TailBpm = state.Bpm;
                        longNote.EndScrollClock = state.Clock16 / 16;
                        Track(rollEndAfterBar, Now, latest: true);
                        Entries.Add(new ChartEntry(longNote, true));
                        longNote = null;
                    }
                    // OpenTaiko: a roll head while a roll is open is a blank (TaikoJiro's 9…9…8 kusudama).
                    else if (type != 0 && !(longNote != null && type >= 5))
                    {
                        if (longNote != null) throw new FormatException("Overlapping long notes are not supported.");
                        var note = NewNote(); note.Kind = (NoteKind)type;
                        if (note.IsBalloon)
                        {
                            if (branch != null) note.BalloonHits = NextBalloon((int)route);
                            else { note.BalloonHits = NextBalloon(0); NextBalloon(1); NextBalloon(2); }
                        }
                        chart.Notes.Add(note);
                        Entries.Add(new ChartEntry(note, false));
                        if (note.IsLong) longNote = note;
                    }
                    state.RawMs += 15000.0 / state.Bpm * state.Measure * (16.0 / slots);
                    state.Beat += 4.0 * state.Measure / Math.Max(1, slots);
                    state.Clock16 += state.Measure * (16.0 / slots);
                    index++;
                }
                if (index == 0)
                {
                    AddBar();
                    state.RawMs += 15000.0 / state.Bpm * state.Measure * 16.0;
                    state.Beat += 4 * state.Measure;
                    state.Clock16 += state.Measure * 16.0;
                }
                chart.Duration = Math.Max(chart.Duration, Now);
                pending.Clear();
            }
        }
    }
}
