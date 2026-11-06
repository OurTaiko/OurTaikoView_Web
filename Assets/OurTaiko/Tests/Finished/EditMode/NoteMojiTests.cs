using System.Linq;
using NUnit.Framework;

namespace OurTaiko.Tests
{
    // Text frames under the notes: ドン0 ド1 コ2 カッ3 カ4 ドン(大)5 カッ(大)6 連打7 連打(大)8 ふうせん9 くすだま11.
    public sealed class NoteMojiTests
    {
        static TaikoChart Parse(string body) => TjaParser.Parse("TITLE:Test\nBPM:120\nCOURSE:Oni\n#START\n" + body + "\n#END");
        static int[] Moji(TaikoChart chart) => chart.Notes.Select(n => n.Moji).ToArray();

        [TestCase("1111,", new[] { 0, 0, 0, 0 })]                          // quarters each stand alone
        [TestCase("11111111,", new[] { 1, 1, 1, 1, 1, 1, 1, 0 })]           // eighths: ド…ドン
        [TestCase("11101110,", new[] { 1, 2, 0, 1, 2, 0 })]                 // an eighth triple is ドコドン…
        [TestCase("1120101010000000,", new[] { 1, 1, 3, 1, 2, 0 })]         // …also with an eighth on one side only
        [TestCase("11110000,", new[] { 1, 1, 1, 0 })]
        [TestCase("11300000,", new[] { 1, 1, 5 })]
        [TestCase("1110111000000000,", new[] { 1, 2, 0, 1, 2, 0 })]         // ドコドン twice
        [TestCase("2220000000000000,", new[] { 4, 4, 3 })]
        [TestCase("12120000,", new[] { 1, 4, 1, 3 })]
        [TestCase("33440000,", new[] { 5, 5, 6, 6 })]                       // big notes keep their text
        [TestCase("1310000000000000,", new[] { 1, 5, 0 })]                  // a big note in the stream: no コ
        [TestCase("1130000000000000,", new[] { 1, 1, 5 })]
        [TestCase("1100000000000000,", new[] { 1, 0 })]
        [TestCase("110000000000000000000000,", new[] { 1, 1 })]             // last within a 24th stays ド
        [TestCase("101100000000000000000000,", new[] { 1, 2, 1 })]          // ド コド
        [TestCase("1111100000000000,", new[] { 1, 1, 1, 1, 0 })]            // 5 and 9: too short for コ
        [TestCase("1111111110000000,", new[] { 1, 1, 1, 1, 1, 1, 1, 1, 0 })]
        [TestCase("1111111111100000,", new[] { 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 0 })]
        [TestCase("1111111111110000,", new[] { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0 })]   // even runs have none
        [TestCase("1111111111111111,\n1000,", new[] { 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 0 })]
        [TestCase("1111111111115008,", new[] { 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 7 })]   // ドコ… 連打
        [TestCase("1111111111117008,", new[] { 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 9 })]
        [TestCase("1111111111500008,", new[] { 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 7 })]
        [TestCase("1111111115000008,", new[] { 1, 1, 1, 1, 1, 1, 1, 1, 1, 7 })]             // 9 before a roll
        [TestCase("1111111111150008,", new[] { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 7 })]       // odd runs before a roll have none
        [TestCase("1170000800000000,", new[] { 1, 1, 9 })]                  // ドド ふうせん
        [TestCase("11500008,", new[] { 1, 1, 7 })]                          // a roll head ends an eighth stream too
        [TestCase("50080011,", new[] { 7, 1, 0 })]                          // and nothing joins after it
        [TestCase("60000008,\n70000008,\n90000008,", new[] { 8, 9, 11 })]
        [TestCase("#BPMCHANGE 60\n1110000000000000,", new[] { 1, 2, 0 })]  // gaps are in beats
        // The examples of the rule document, in sixteenths. Their eighth triples touch both neighbours: ドドドン.
        [TestCase("1000100010001011,\n1010101011101000,", new[] { 0, 0, 0, 0, 1, 2, 0, 1, 1, 0, 1, 2, 0, 0 })]
        [TestCase("1022102212221010,\n1010221110101010,\n1000,",
            new[] { 0, 4, 4, 0, 4, 4, 1, 4, 4, 4, 0, 1, 1, 0, 4, 4, 1, 1, 0, 1, 1, 1, 0 })]
        [TestCase("1120101010111000,", new[] { 1, 1, 3, 1, 1, 0, 1, 2, 0 })]
        public void AssignsFrames(string body, int[] expected)
        {
            Assert.That(Moji(Parse(body)), Is.EqualTo(expected));
        }

        [Test] public void BarLinesDoNotBreakStreams()
        {
            Assert.That(Moji(Parse("0000000000000011,\n1000,")), Is.EqualTo(new[] { 1, 2, 0 }));
        }

        [Test] public void BranchRoutesAreSeparateLists()
        {
            var chart = Parse("11111111,\n#BRANCHSTART p,50,80\n#N\n11000000,\n#E\n11000000,\n#M\n11000000,\n#BRANCHEND\n11000000,");
            Assert.That(chart.NoteLists.Count, Is.EqualTo(4));
            // The common list continues across the branch; each route stream is its own.
            foreach (BranchRoute route in new[] { BranchRoute.Normal, BranchRoute.Expert, BranchRoute.Master })
                Assert.That(chart.Notes.Where(n => n.BranchId == 0 && n.Route == route).Select(n => n.Moji), Is.EqualTo(new[] { 1, 0 }));
            Assert.That(chart.Notes.Where(n => n.BranchId < 0).Select(n => n.Moji),
                Is.EqualTo(new[] { 1, 1, 1, 1, 1, 1, 1, 0, 1, 0 }));
        }

        [Test] public void ModifiersReassignAfterSwappingColours()
        {
            var chart = Parse("1110000000000000,");
            ChartModifiers.Apply(chart, new PlayOptions { inverse = true }, new System.Random(1));
            Assert.That(Moji(chart), Is.EqualTo(new[] { 4, 4, 3 }));
        }
    }
}
