using System.Linq;
using NUnit.Framework;

namespace OurTaiko.Tests
{
    // #JPOSSCROLL as OpenTaiko reads and plays it (plan: OurTaikoLogs OurTaikoPlay/Documentation/
    // 2026-10-14-bmscroll-plan.md, "追加计划：#JPOSSCROLL").
    public sealed class JudgeShiftTests
    {
        static TaikoChart Parse(string body) => TjaParser.Parse("TITLE:Jpos\nBPM:120\nCOURSE:Oni\n#START\n" + body + "\n#END");
        static (double, double, double, double) Move(ChartJudgeMove m) => (m.Time, m.Duration, m.MoveX, m.MoveY);

        // J1
        [Test] public void MovesReadTheirTimeDurationAndDirection()
        {
            Assert.That(Move(Parse("#JPOSSCROLL 1 100\n1,").JudgeMoves.Single()), Is.EqualTo((0.0, 1000.0, -100.0, 0.0)), "Direction 0 turns it around.");
            Assert.That(Move(Parse("#JPOSSCROLL 0.05 -10+16i 1\n1,").JudgeMoves.Single()), Is.EqualTo((0.0, 50.0, -10.0, 16.0)));
            Assert.That(Parse("#JPOSSCROLL -1 50 1\n1,").JudgeMoves.Single().Duration, Is.Zero);
            Assert.That(Parse("0\n#JPOSSCROLL 0.4 350 1\n0,").JudgeMoves.Single().Time, Is.EqualTo(1), "At its place in the measure.");
            foreach (string bad in new[] { "#JPOSSCROLL 1", "#JPOSSCROLL x 5", "#JPOSSCROLL 1 5 a" })
            {
                var chart = Parse(bad + "\n1,");
                Assert.That(chart.JudgeMoves, Is.Empty, bad);
                Assert.That(chart.Warnings, Has.Member(bad));
            }
        }

        // J2
        [Test] public void EachMoveStartsWhereTheLastEndedAndCutsAnUnfinishedOne()
        {
            // 1 s to the right by 100, then at 0.5 s 0.5 s back by 20.
            var moves = Parse("#JPOSSCROLL 1 100+40i 1\n1\n#JPOSSCROLL 0.5 -20 1\n000,").JudgeMoves;
            Assert.That(Move(moves[0]), Is.EqualTo((0.0, 500.0, 50.0, 20.0)), "Cut to the half it had, at its pace.");
            Assert.That((moves[1].FromX, moves[1].FromY), Is.EqualTo((50.0, 20.0)));
        }

        // J3
        [Test] public void TheShiftRunsLinearlyInWholeMillisecondsAndStays()
        {
            var moves = Parse("#JPOSSCROLL 1 100+40i 1\n1\n#JPOSSCROLL 0.5 -20 1\n000,").JudgeMoves;
            Assert.That(JudgeShift.At(moves, -1), Is.EqualTo((0.0, 0.0)), "Nothing before the first.");
            Assert.That(JudgeShift.At(moves, 0.25), Is.EqualTo((37.0, 15.0)), "25 and 10, ×1.5, cut.");
            Assert.That(JudgeShift.At(moves, 0.2504), Is.EqualTo(JudgeShift.At(moves, 0.25)), "Whole milliseconds.");
            Assert.That(JudgeShift.At(moves, 0.5), Is.EqualTo((75.0, 30.0)));
            Assert.That(JudgeShift.At(moves, 0.75), Is.EqualTo((60.0, 30.0)));
            Assert.That(JudgeShift.At(moves, 5), Is.EqualTo((45.0, 30.0)), "It stays at the end.");
            Assert.That(JudgeShift.MaxAcross(moves), Is.EqualTo(75));
        }
    }
}
