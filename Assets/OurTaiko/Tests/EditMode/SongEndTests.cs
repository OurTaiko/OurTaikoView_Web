using NUnit.Framework;

namespace OurTaiko.Tests
{
    // A play ends two seconds after its last note, whatever #END and the music say, and the music
    // fades out linearly under the ending. 120 BPM: a measure is 2 s.
    public sealed class SongEndTests
    {
        static TaikoChart Chart(string body) => TjaParser.Parse("TITLE:Test\nBPM:120\nCOURSE:Oni\n#START\n" + body + "\n#END");

        [Test] public void ItEndsTwoSecondsAfterTheLastNote()
        {
            var chart = Chart("1000,\n1000,\n0000,\n0000,");
            Assert.That(chart.Duration, Is.EqualTo(8).Within(1e-9));
            Assert.That(SongEnd.Of(chart), Is.EqualTo(4).Within(1e-9));
        }

        // #END + 1 s is 3 s here; the last note still decides.
        [Test] public void EndDoesNotCutItShort()
        {
            Assert.That(SongEnd.Of(Chart("0001,")), Is.EqualTo(3.5).Within(1e-9));
        }

        [Test] public void ARollCountsUntilItsEnd()
        {
            Assert.That(SongEnd.Of(Chart("5000,\n0008,\n0000,\n0000,")), Is.EqualTo(5.5).Within(1e-9));
        }

        [Test] public void AChartWithoutNotesEndsAtEnd()
        {
            Assert.That(SongEnd.Of(Chart("0000,")), Is.EqualTo(2).Within(1e-9));
        }

        [Test] public void ALateJudgeOffsetMovesIt()
        {
            var chart = Chart("1000,\n1000,\n0000,\n0000,");
            Assert.That(SongEnd.Of(chart, 0.1), Is.EqualTo(4.1).Within(1e-9));
            Assert.That(SongEnd.Of(chart, -0.1), Is.EqualTo(4).Within(1e-9));
        }

        [Test] public void TheMusicFadesLinearlyOverTheEnding()
        {
            Assert.That(SongEnd.Fade(0, 4), Is.EqualTo(1));
            Assert.That(SongEnd.Fade(1, 4), Is.EqualTo(0.75f).Within(1e-6));
            Assert.That(SongEnd.Fade(4, 4), Is.Zero);
            Assert.That(SongEnd.Fade(5, 4), Is.Zero);
            Assert.That(SongEnd.Fade(0, 0), Is.Zero, "Without an ending the result follows at once.");
        }
    }
}
