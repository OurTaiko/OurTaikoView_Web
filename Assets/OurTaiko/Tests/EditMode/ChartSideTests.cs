using System;
using System.Linq;
using NUnit.Framework;

namespace OurTaiko.Tests
{
    // Local TJA files pick a course's block by seat the way online charts do (ChartSide; user rule
    // R036 in OurTaikoLogs taiko-official-schema-requirements.txt). A player always sits at 1P or
    // 2P: 1P reads p1 → single, 2P reads p2 → single → p1; "single" is a side, never a seat.
    public sealed class ChartSideTests
    {
        const string Header = "TITLE:Sides\nBPM:120\n";
        static string Block(string course, int level, string player, string notes)
            => $"COURSE:{course}\nLEVEL:{level}\n#START {player}\n{notes}\n#END\n".Replace("#START \n", "#START\n");

        // STYLE:Double 1P/2P written before the single block, as official files often are.
        static readonly string ThreeSides = Header
            + "STYLE:Double\n" + Block("Oni", 7, "P1", "1,") + Block("Oni", 8, "P2", "11,")
            + "STYLE:Single\n" + Block("Oni", 9, "", "111,");

        [Test] public void EachSeatReadsItsOwnSideFirst()
        {
            Assert.That(TjaParser.Parse(ThreeSides, "Oni").Level, Is.EqualTo(7), "the default seat is 1P");
            Assert.That(TjaParser.Parse(ThreeSides, "Oni", "p1").Level, Is.EqualTo(7));
            Assert.That(TjaParser.Parse(ThreeSides, "Oni", "p2").Level, Is.EqualTo(8));
            Assert.That(SongInfo.Read(ThreeSides).Course(Difficulty.Oni).Side, Is.EqualTo("p1"));
            Assert.That(SongInfo.Read(ThreeSides, seat: "p2").Course(Difficulty.Oni).Level, Is.EqualTo(8));
        }

        [Test] public void SeatsFallBackToSingleThenTheOtherSide()
        {
            string onlySingle = Header + Block("Oni", 9, "", "111,");
            Assert.That(TjaParser.Parse(onlySingle, "Oni", "p1").Level, Is.EqualTo(9), "p1 → single");
            Assert.That(TjaParser.Parse(onlySingle, "Oni", "p2").Level, Is.EqualTo(9), "p2 → single");
            string noP2 = Header + Block("Oni", 7, "P1", "1,") + Block("Oni", 9, "", "111,");
            Assert.That(TjaParser.Parse(noP2, "Oni", "p2").Level, Is.EqualTo(9), "p2 → single before p1");
            string onlyP1 = Header + Block("Oni", 7, "P1", "1,");
            Assert.That(TjaParser.Parse(onlyP1, "Oni", "p2").Level, Is.EqualTo(7), "p2 → single → p1");
            string onlyP2 = Header + Block("Oni", 8, "P2", "11,");
            Assert.Throws<FormatException>(() => TjaParser.Parse(onlyP2, "Oni", "p1"), "p1 never falls back to p2");
            Assert.That(TjaParser.Parse(onlyP2, "Oni", "p2").Level, Is.EqualTo(8));
        }

        [Test] public void ACourseWithOnlyA2PBlockIsNotListedForThe1PSeat()
        {
            string text = Header + Block("Hard", 5, "", "1,") + Block("Oni", 8, "P2", "11,");
            Assert.Throws<FormatException>(() => TjaParser.Parse(text, "Oni"));
            Assert.That(SongInfo.Read(text).Courses.Select(c => c.Difficulty), Is.EqualTo(new[] { Difficulty.Hard }));
            Assert.That(SongInfo.Read(text, seat: "p2").Courses.Select(c => (c.Difficulty, c.Level, c.Side)),
                Is.EqualTo(new[] { (Difficulty.Hard, 5, "single"), (Difficulty.Oni, 8, "p2") }));
        }

        [Test] public void TheParserAndTheSongListAgreeOnTheBlockAndItsHeaders()
        {
            // Headers of the played block (BALLOON, a scroll mode) apply even when it comes last.
            string text = Header + "BALLOON:2\n" + Block("Oni", 7, "", "7,8,") + "COURSE:Oni\nLEVEL:9\nBALLOON:4\n#BMSCROLL\n#START P1\n7,8,\n#END\n";
            var chart = TjaParser.Parse(text, "Oni");
            var info = SongInfo.Read(text).Course(Difficulty.Oni);
            Assert.That((chart.Level, info.Level, info.Side), Is.EqualTo((9, 9, "p1")));
            Assert.That(chart.Notes.First(n => n.Kind == NoteKind.Balloon).BalloonHits, Is.EqualTo(4));
            var p2 = TjaParser.Parse(text, "Oni", "p2");
            Assert.That(p2.Level, Is.EqualTo(7));
            Assert.That(p2.Notes.First(n => n.Kind == NoteKind.Balloon).BalloonHits, Is.EqualTo(2));
        }

        [Test] public void OnlineAndLocalShareTheReadOrder()
        {
            Assert.That(ChartSide.Seats, Is.EqualTo(new[] { "p1", "p2" }));
            Assert.That(ChartSide.Reads("p1"), Is.EqualTo(new[] { "p1", "single" }));
            Assert.That(ChartSide.Reads("p2"), Is.EqualTo(new[] { "p2", "single", "p1" }));
            Assert.That(ChartSide.OfStart("P2"), Is.EqualTo("p2"));
            Assert.That(ChartSide.OfStart(""), Is.EqualTo("single"));
            Assert.Throws<ArgumentException>(() => ChartSide.Reads("single"), "single is a side, not a seat");
        }
    }
}
