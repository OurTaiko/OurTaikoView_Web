using NUnit.Framework;

namespace OurTaiko.Tests
{
    public sealed class MovieSyncTests
    {
        static MovieSync.Player Ready(double position, bool playing = false, double length = 60)
            => new MovieSync.Player { Prepared = true, Playing = playing, Position = position, Length = length };

        [Test]
        public void BlackUntilPreparedAndUntilMovieOffset()
        {
            var plan = MovieSync.Decide(5, 1, true, new MovieSync.Player());
            Assert.That(plan.Visible, Is.False);
            Assert.That(plan.Play || plan.Pause || plan.SeekTo.HasValue, Is.False);
            // Countdown (or a positive MOVIEOFFSET): the first frame waits, hidden.
            plan = MovieSync.Decide(-1.2, 1, true, Ready(0));
            Assert.That(plan.Visible, Is.False);
            Assert.That(plan.Play || plan.Pause || plan.SeekTo.HasValue, Is.False);
        }

        [Test]
        public void StartsFromItsFirstFrameWhenTheSongReachesIt()
        {
            var plan = MovieSync.Decide(0.004, 1, true, Ready(0));
            Assert.That(plan.Visible, Is.True);
            Assert.That(plan.Play, Is.True);
            Assert.That(plan.SeekTo, Is.Null);
            Assert.That(plan.Rate, Is.EqualTo(1));
        }

        [Test]
        public void NegativeOffsetStartsPartWay()
        {
            // MOVIEOFFSET -3: at the song's first second the movie is 4 s in.
            var plan = MovieSync.Decide(1 - -3, 1, true, Ready(0));
            Assert.That(plan.SeekTo, Is.EqualTo(4));
            Assert.That(plan.Play, Is.True);
        }

        [Test]
        public void SmallDriftChangesTheRateLargeDriftSeeks()
        {
            Assert.That(MovieSync.Decide(10, 1, true, Ready(10.005, true)).Rate, Is.EqualTo(1), "within the deadband");
            var behind = MovieSync.Decide(10, 1, true, Ready(9.9, true));
            Assert.That(behind.SeekTo, Is.Null);
            Assert.That(behind.Play, Is.False, "already playing");
            Assert.That(behind.Rate, Is.EqualTo(1.1).Within(1e-9));
            Assert.That(MovieSync.Decide(10, 1, true, Ready(10.05, true)).Rate, Is.EqualTo(0.95).Within(1e-9));
            Assert.That(MovieSync.Decide(10, 1, true, Ready(10.2, true)).Rate, Is.EqualTo(0.9).Within(1e-9), "at most 10 %");
            var far = MovieSync.Decide(10, 1, true, Ready(9.5, true));
            Assert.That(far.SeekTo, Is.EqualTo(10));
            Assert.That(far.Play, Is.True);
        }

        [Test]
        public void FollowsTheSongRate()
        {
            Assert.That(MovieSync.Decide(10, 0.5, true, Ready(10, true)).Rate, Is.EqualTo(0.5));
            // 0.2 s behind at half speed is 0.4 s of play: a seek.
            Assert.That(MovieSync.Decide(10, 0.5, true, Ready(9.8, true)).SeekTo, Is.EqualTo(10));
        }

        [Test]
        public void PausesWithTheSongAndWaitsForASeekToFinish()
        {
            var plan = MovieSync.Decide(10, 1, false, Ready(10, true));
            Assert.That(plan.Pause, Is.True);
            Assert.That(plan.SeekTo, Is.Null);
            Assert.That(plan.Visible, Is.True, "a paused song keeps its frame");
            // The song was moved while stopped: the movie follows, still paused.
            plan = MovieSync.Decide(30, 1, false, Ready(10));
            Assert.That(plan.SeekTo, Is.EqualTo(30));
            Assert.That(plan.Play, Is.False);
            var seeking = new MovieSync.Player { Prepared = true, Seeking = true, Position = 3, Length = 60 };
            plan = MovieSync.Decide(10, 1, true, seeking);
            Assert.That(plan.Play || plan.Pause || plan.SeekTo.HasValue, Is.False);
        }

        [Test]
        public void KeepsTheLastFrameAfterTheEnd()
        {
            var plan = MovieSync.Decide(70, 1, true, new MovieSync.Player { Prepared = true, Ended = true, Position = 60, Length = 60 });
            Assert.That(plan.Visible, Is.True);
            Assert.That(plan.Play || plan.Pause || plan.SeekTo.HasValue, Is.False);
            // Still playing out its last frames as the song passes the end: left alone.
            plan = MovieSync.Decide(60.01, 1, true, Ready(59.99, true));
            Assert.That(plan.Play || plan.Pause || plan.SeekTo.HasValue, Is.False);
            // Ended early (it ran ahead): back to where the song is.
            plan = MovieSync.Decide(50, 1, true, new MovieSync.Player { Prepared = true, Ended = true, Position = 60, Length = 60 });
            Assert.That(plan.SeekTo, Is.EqualTo(50));
            Assert.That(plan.Play, Is.True);
        }

        [Test]
        public void UnknownLengthNeverEnds()
        {
            var plan = MovieSync.Decide(500, 1, true, Ready(500, true, 0));
            Assert.That(plan.Pause || plan.SeekTo.HasValue, Is.False);
            Assert.That(plan.Rate, Is.EqualTo(1));
        }
    }
}
