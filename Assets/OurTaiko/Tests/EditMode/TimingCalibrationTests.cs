using System;
using System.IO;
using NUnit.Framework;

namespace OurTaiko.Tests
{
    public sealed class TimingCalibrationTests
    {
        static TimingStatistics Statistics(params double[] offsets) => new TimingStatistics(
            Array.ConvertAll(offsets, value => new HitTiming(0, value, Judgment.Good)), 0, 25, 75, 108);
        static GameSettings Settings(int audio = 12, int judge = -4)
        {
            var settings = new GameSettings();
            settings.play.audioOffsetMs = audio; settings.play.judgeOffsetMs = judge;
            return settings;
        }

        [TestCase(-30.1, -15)] [TestCase(18.7, 9)]
        [TestCase(-1, -1)] [TestCase(1, 1)]
        public void RecommendationAddsHalfSignedMeanToThePlayedOffsets(double mean, int correction)
        {
            var calibration = new TimingCalibration(Statistics(mean), 12, -4);
            Assert.That(calibration.CorrectionMs, Is.EqualTo(correction));
            Assert.That(calibration.RecommendedAudioMs, Is.EqualTo(12 + correction));
            Assert.That(calibration.RecommendedJudgeMs, Is.EqualTo(-4 + correction));
        }

        [TestCase(true)] [TestCase(false)]
        public void ApplyChangesOnlySelectedOffsetAndEachControlCanApplyOnce(bool audio)
        {
            var settings = Settings(); settings.audio.volume.master = .3f;
            var calibration = new TimingCalibration(Statistics(-30.1), 12, -4);
            GameSettings saved = null;
            Assert.That(calibration.TryApply(settings, audio, value => saved = value), Is.True);
            Assert.That(saved.play.audioOffsetMs, Is.EqualTo(audio ? -3 : 12));
            Assert.That(saved.play.judgeOffsetMs, Is.EqualTo(audio ? -4 : -19));
            Assert.That(saved.audio.volume.master, Is.EqualTo(.3f));
            Assert.That(settings.play.audioOffsetMs, Is.EqualTo(12), "Do not mutate live settings before saving.");
            Assert.That(settings.play.judgeOffsetMs, Is.EqualTo(-4));
            Assert.That(calibration.TryApply(settings, audio, _ => Assert.Fail("Duplicate save")), Is.False);
            Assert.That(calibration.TryApply(saved, !audio, value => saved = value), Is.True);
            Assert.That(saved.play.audioOffsetMs, Is.EqualTo(-3));
            Assert.That(saved.play.judgeOffsetMs, Is.EqualTo(-19));
            Assert.That(calibration.TryApply(saved, audio, _ => Assert.Fail("Duplicate save")), Is.False);
            Assert.That(calibration.TryApply(saved, !audio, _ => Assert.Fail("Duplicate save")), Is.False);
        }

        [Test]
        public void StaleSettingsCannotApplyAnOldRunRecommendation()
        {
            var calibration = new TimingCalibration(Statistics(20), 12, -4);
            Assert.That(calibration.CanApply(Settings(13, -4).play, true), Is.False);
            Assert.That(calibration.CanApply(Settings(12, -3).play, false), Is.False);
        }

        [Test]
        public void FailedSaveCanBeRetriedWithoutLosingRecommendation()
        {
            var calibration = new TimingCalibration(Statistics(20), 12, -4);
            Assert.Throws<IOException>(() => calibration.TryApply(Settings(), true, _ => throw new IOException("Test failure")));
            Assert.That(calibration.AudioApplied, Is.False);
            Assert.That(calibration.TryApply(Settings(), true, _ => { }), Is.True);
        }

        [Test]
        public void EmptyOrSubMillisecondMeanDoesNotOfferAnAdjustment()
        {
            foreach (var statistics in new[] { Statistics(), Statistics(-.4, .2) })
            {
                var calibration = new TimingCalibration(statistics, 12, -4);
                Assert.That(calibration.CanApply(Settings().play, true), Is.False);
                Assert.That(calibration.CanApply(Settings().play, false), Is.False);
            }
        }

        [Test]
        public void ExtremeSavedSettingsDoNotWrapTheRecommendedValue()
        {
            var calibration = new TimingCalibration(Statistics(100), int.MaxValue, 0);
            Assert.That(calibration.RecommendedAudioMs, Is.Null);
            Assert.That(calibration.RecommendedJudgeMs, Is.EqualTo(50));
            Assert.That(calibration.CanApply(Settings(int.MaxValue, 0).play, true), Is.False);
        }

        [Test]
        public void ResultCapturesOffsetsActuallyUsedAndAutoplayOffersNoCalibration()
        {
            var chart = new TaikoChart();
            chart.Notes.Add(new ChartNote { Kind = NoteKind.Don, Time = 1 });
            var session = new PlaySession(chart, -.004);
            session.Hit(false, .976);
            var result = PlayResult.From(session, "test", false, 12);
            Assert.That(result.Calibration.AudioOffsetMs, Is.EqualTo(12));
            Assert.That(result.Calibration.JudgeOffsetMs, Is.EqualTo(-4));
            Assert.That(result.Calibration.CorrectionMs, Is.EqualTo(-10));
            Assert.That(PlayResult.From(session, "test", true, 12).Calibration, Is.Null);
            Assert.That(PlayResult.From(session, "test", false).Calibration, Is.Null, "Unknown played settings must not be guessed.");
        }
    }
}
