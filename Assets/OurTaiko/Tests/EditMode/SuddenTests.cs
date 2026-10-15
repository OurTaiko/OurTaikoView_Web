using System.Linq;
using NUnit.Framework;

namespace OurTaiko.Tests
{
    // #SUDDEN as OpenTaiko reads and draws it (plan: OurTaikoLogs OurTaikoPlay/Documentation/
    // 2026-10-14-bmscroll-plan.md, "追加计划：#SUDDEN").
    public sealed class SuddenTests
    {
        const double Travel = 1302, Unset = double.PositiveInfinity;

        static TaikoChart Parse(string body) => TjaParser.Parse("TITLE:Sudden\nBPM:120\nBALLOON:3\nCOURSE:Oni\n#START\n" + body + "\n#END");
        static (double, double)[] Times(TaikoChart chart) => chart.Notes.Select(n => (n.SuddenShow, n.SuddenMove)).ToArray();

        // S1
        [Test] public void NotesTakeTheTimesWrittenBeforeThem()
        {
            var chart = Parse("#SUDDEN 1 0.5\n1,\n#SUDDEN 0 0\n1,");
            Assert.That(Times(chart), Is.EqualTo(new[] { (1.0, 0.5), (Unset, Unset) }));
            Assert.That(chart.Bars.All(b => double.IsPositiveInfinity(b.SuddenShow) && double.IsPositiveInfinity(b.SuddenMove)), "Bar lines never take them.");
            Assert.That(Times(Parse("#SUDDEN 1 0.5005\n1,")), Is.EqualTo(new[] { (1.0, 0.5) }), "The move time is cut to whole milliseconds.");
            Assert.That(Times(Parse("#SUDDEN 0.0004 0.0009\n1,")), Is.EqualTo(new[] { (Unset, Unset) }), "Under a millisecond is unset.");
            var missing = Parse("#SUDDEN 1 0.5\n#SUDDEN 2\n1,");
            Assert.That(Times(missing), Is.EqualTo(new[] { (1.0, 0.5) }), "A command without both times is ignored.");
            Assert.That(missing.Warnings, Has.Member("#SUDDEN 2"));
        }

        [Test] public void RoutesStartFromTheBranchStartTimes()
        {
            var chart = Parse("#SUDDEN 1 0.5\n1,\n#BRANCHSTART p,0,0\n#N\n#SUDDEN 2 1\n1,\n#E\n1,\n#M\n1,\n#BRANCHEND");
            (double, double) Route(BranchRoute route) => Times(chart)[chart.Notes.FindIndex(n => n.BranchId == 0 && n.Route == route)];
            Assert.That(Route(BranchRoute.Normal), Is.EqualTo((2.0, 1.0)));
            Assert.That(Route(BranchRoute.Expert), Is.EqualTo((1.0, 0.5)));
        }

        // S2
        [Test] public void NotesAppearAtTheirShowTimeAndAreStillJudged()
        {
            var chart = Parse("0001,\n#SUDDEN 1 0.5\n1,");
            var plain = chart.Notes[0]; var sudden = chart.Notes[1];
            Assert.That(NoteScroll.Appeared(plain, -100), Is.True);
            Assert.That(NoteScroll.Appeared(sudden, 0.99), Is.False);
            Assert.That(NoteScroll.Appeared(sudden, 1.0), Is.True);
            var session = new PlaySession(chart);
            session.Hit(false, 1.5); session.Hit(false, 2);
            Assert.That(session.Good, Is.EqualTo(2));
        }

        // S3
        [Test] public void NotesStandWhereTheyWillStartUntilTheyMove()
        {
            // NM: the note at 1.5 s starts moving at 0.5 s, from (0.5 s × 120 / 240) × SCROLL 2 travels out.
            var normal = Parse("#SUDDEN 2 1\n#SCROLL 2+1i\n0001,");
            var clocks = new ScrollClockTimeline(normal);
            var note = normal.Notes[0];
            Assert.That(NoteScroll.Distance(note, 0, clocks, Travel), Is.EqualTo((1302.0, 976.0)), "Vertical still moves (whole pixels).");
            Assert.That(NoteScroll.Distance(note, 0.4, clocks, Travel).X, Is.EqualTo(1302));
            Assert.That(NoteScroll.Distance(note, 1, clocks, Travel), Is.EqualTo(NoteScroll.RawDistance(note, 1, clocks, Travel)));
            // BM: the note (clock 1.75, at 2.75 s) starts at 1.75 s, when the clock is 0.875.
            var bm = Parse("#BMSCROLL\n#SUDDEN 2 1\n1,\n#BPMCHANGE 240\n0001,");
            var bmClocks = new ScrollClockTimeline(bm);
            Assert.That(NoteScroll.Distance(bm.Notes[1], 1, bmClocks, Travel).X, Is.EqualTo(System.Math.Truncate((1.75 - 0.875) * Travel)));
            Assert.That(NoteScroll.Distance(bm.Notes[1], 2.5, bmClocks, Travel).X, Is.EqualTo(System.Math.Truncate(0.25 * Travel)));
            // A roll stays rigid: only its head stands, the length is the same.
            var roll = Parse("#SUDDEN 3 1\n5008,").Notes[0];
            Assert.That(NoteScroll.RollLength(roll, Travel), Is.EqualTo(NoteScroll.DistanceFromJudge(1.5, 0, 120, 1, Travel)));
            Assert.That(NoteScroll.Distance(roll, -2, clocks, Travel).X, Is.EqualTo(NoteScroll.DistanceFromJudge(0, -1, 120, 1, Travel)));
        }

        // S4
        [Test] public void AnAppearanceWithoutAMoveTimeHidesRollsAndBalloons()
        {
            var chart = Parse("#SUDDEN 1 0\n5008,\n7008,\n1,");
            Assert.That(chart.Notes.Select(n => NoteScroll.Appeared(n, 100)), Is.EqualTo(new[] { false, false, true }));
        }
    }
}
