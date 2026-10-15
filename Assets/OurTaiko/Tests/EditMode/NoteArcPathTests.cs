using System;
using NUnit.Framework;

namespace OurTaiko.Tests
{
    public sealed class NoteArcPathTests
    {
        static double Degrees(double radians) => radians * 180 / Math.PI;

        [Test]
        public void SweepsNijiiroCircleFromJudgeToSoulBadge()
        {
            // Nijiiro note_arc_pivot: radius ~719, -154.89 -> -38.24 degrees over the top.
            Assert.That(NoteArcPath.Radius, Is.EqualTo(719.14).Within(0.01));
            Assert.That(Degrees(NoteArcPath.StartAngle), Is.EqualTo(-154.89).Within(0.01));
            Assert.That(Degrees(NoteArcPath.EndAngle), Is.EqualTo(-38.24).Within(0.01));

            NoteArcPath.Position(0, out double x, out double y);
            Assert.That(x, Is.EqualTo(618).Within(0.05)); Assert.That(y, Is.EqualTo(110).Within(0.05));
            NoteArcPath.Position(1, out x, out y);
            Assert.That(x, Is.EqualTo(1834).Within(0.05)); Assert.That(y, Is.EqualTo(-30).Within(0.05));
            // The apex rises 409 above the judge centre, as high as the Bezier's (curve height 608).
            NoteArcPath.Position(0.5, out x, out y);
            Assert.That(y, Is.EqualTo(-299).Within(1));
        }

        [Test]
        public void ConstantAngularRateOverThirtyFrames()
        {
            Assert.That(NoteArcPath.Duration, Is.EqualTo(0.5001).Within(1e-9));
            Assert.That(NoteArcPath.Progress(-1), Is.Zero);
            Assert.That(NoteArcPath.Progress(NoteArcPath.Duration / 4), Is.EqualTo(0.25).Within(1e-9));
            Assert.That(NoteArcPath.IsFinished(NoteArcPath.Duration - 0.001), Is.False);
            Assert.That(NoteArcPath.IsFinished(NoteArcPath.Duration), Is.True);
            for (double p = 0; p <= 1; p += 0.125)
            {
                NoteArcPath.Position(p, out double x, out double y);
                double angle = Math.Atan2(y - NoteArcPath.PivotY, x - NoteArcPath.PivotX);
                Assert.That(angle, Is.EqualTo(NoteArcPath.StartAngle + (NoteArcPath.EndAngle - NoteArcPath.StartAngle) * p).Within(1e-9));
                Assert.That(Math.Sqrt((x - NoteArcPath.PivotX) * (x - NoteArcPath.PivotX) + (y - NoteArcPath.PivotY) * (y - NoteArcPath.PivotY)),
                    Is.EqualTo(NoteArcPath.Radius).Within(1e-9));
            }
        }

        // #JPOSSCROLL: the arc leaves the moved judge and still lands on the soul badge.
        [Test]
        public void AMovedJudgeShiftsTheStartButNotTheLanding()
        {
            NoteArcPath.Position(0, -150, 75, out double x, out double y);
            Assert.That(x, Is.EqualTo(618 - 150).Within(0.05)); Assert.That(y, Is.EqualTo(110 + 75).Within(0.05));
            NoteArcPath.Position(0.5, -150, 75, out x, out y);
            NoteArcPath.Position(0.5, out double bx, out double by);
            Assert.That(x, Is.EqualTo(bx - 75).Within(1e-9)); Assert.That(y, Is.EqualTo(by + 37.5).Within(1e-9));
            NoteArcPath.Position(1, -150, 75, out x, out y);
            Assert.That(x, Is.EqualTo(1834).Within(0.05)); Assert.That(y, Is.EqualTo(-30).Within(0.05));
        }
    }
}
