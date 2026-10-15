using NUnit.Framework;

namespace OurTaiko.Tests
{
    public sealed class SongClockTests
    {
        [Test]
        public void CountdownStartsTheTrackAndTheClockRestartsThere()
        {
            var clock = new SongClock();
            Assert.That(clock.Started, Is.False);
            Assert.That(clock.Time, Is.EqualTo(-2));
            Assert.That(clock.TrackDue(0), Is.Null);
            clock.Start(100, 3);
            clock.Update(99.999); // The frame began before Start.
            Assert.That(clock.Time, Is.EqualTo(-3));
            Assert.That(clock.TrackDue(102.999), Is.Null);
            // The frame passes the countdown; the track starts a little later, from its beginning.
            clock.Update(103.01);
            Assert.That(clock.Time, Is.EqualTo(0.01).Within(1e-9));
            Assert.That(clock.TrackDue(103.02), Is.EqualTo(0));
            clock.TrackStarted(103.02);
            Assert.That(clock.TrackDue(103.03), Is.Null);
            Assert.That(clock.Time, Is.EqualTo(0.01).Within(1e-9)); // This frame's time stays published.
            clock.Update(103.05);
            Assert.That(clock.Time, Is.EqualTo(0.03).Within(1e-9));
        }

        [Test]
        public void BassSimpleDriftIsPulledInFromTheNextFrameForTwoSeconds()
        {
            var clock = new SongClock();
            clock.Start(8, 2);
            clock.Update(9, 0.5); // No track yet: playback means nothing.
            Assert.That(clock.Time, Is.EqualTo(-1));
            clock.TrackStarted(10);
            clock.Update(10.1, 0.08);
            Assert.That(clock.Time, Is.EqualTo(0.1).Within(1e-9));
            clock.Update(10.2);
            Assert.That(clock.Time, Is.EqualTo(0.184).Within(1e-9)); // 0.2 - 0.8 * 0.02

            // The window holds up to exactly two seconds after the track start.
            clock = new SongClock();
            clock.Start(8, 2);
            clock.TrackStarted(10);
            clock.Update(12, 1.5);
            Assert.That(clock.Time, Is.EqualTo(2));
            clock.Update(12.5, 9);
            Assert.That(clock.Time, Is.EqualTo(2.1).Within(1e-9)); // 2.5 - 0.8 * 0.5
            clock.Update(13, 9);
            Assert.That(clock.Time, Is.EqualTo(2.6).Within(1e-9));
        }

        [Test]
        public void PracticeRateKeepsTheWindowInRealSeconds()
        {
            var clock = new SongClock();
            clock.Seek(10, 0.5);
            clock.Resume(100);
            Assert.That(clock.TrackDue(100), Is.EqualTo(10));
            clock.TrackStarted(100);
            clock.Update(102, 10.5); // Two real seconds are one track second.
            Assert.That(clock.Time, Is.EqualTo(11));
            clock.Update(103);
            Assert.That(clock.Time, Is.EqualTo(11.1).Within(1e-9)); // 11.5 - 0.8 * 0.5
            clock.Update(104, 20);
            clock.Update(105);
            Assert.That(clock.Time, Is.EqualTo(12.1).Within(1e-9));
        }

        [Test]
        public void ResumeRestartsTheTrackWhereTheClockStopped()
        {
            var clock = new SongClock();
            clock.Start(100, 3);
            clock.Update(103);
            clock.TrackStarted(103);
            clock.Update(104);
            clock.Pause();
            Assert.That(clock.TrackDue(110), Is.Null);
            clock.Update(110, 8);
            Assert.That(clock.Time, Is.EqualTo(1));
            clock.Resume(120);
            clock.Update(119.999);
            Assert.That(clock.Time, Is.EqualTo(1));
            Assert.That(clock.TrackDue(120), Is.EqualTo(1));
            clock.TrackStarted(120.01);
            clock.Update(121.01, 1.9);
            Assert.That(clock.Time, Is.EqualTo(2).Within(1e-9));
            clock.Update(121.11);
            Assert.That(clock.Time, Is.EqualTo(2.02).Within(1e-9));
        }

        [Test]
        public void ResumeDuringCountdownWaitsForTheTrackStart()
        {
            var clock = new SongClock();
            clock.Start(10, 2);
            clock.Update(11);
            clock.Pause();
            clock.Resume(20);
            Assert.That(clock.Time, Is.EqualTo(-1));
            Assert.That(clock.TrackDue(20.999), Is.Null);
            Assert.That(clock.TrackDue(21), Is.EqualTo(0));
        }

        [Test]
        public void SkippedTrackLeavesTheClockRunningUncorrected()
        {
            var clock = new SongClock();
            clock.Start(10, 2);
            clock.Update(12);
            clock.SkipTrack();
            Assert.That(clock.TrackDue(12), Is.Null);
            clock.Update(12.5, 9);
            clock.Update(13);
            Assert.That(clock.Time, Is.EqualTo(1));
        }
    }
}
