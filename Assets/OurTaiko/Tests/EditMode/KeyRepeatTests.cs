using NUnit.Framework;

namespace OurTaiko.Tests
{
    public sealed class KeyRepeatTests
    {
        [Test]
        public void FiresAtOnceThenAfter200MsThenEvery30Ms()
        {
            var repeat = new KeyRepeat();
            Assert.That(repeat.Tick(true, 0), Is.True, "The press itself.");
            Assert.That(repeat.Tick(true, 100), Is.False);
            Assert.That(repeat.Tick(true, 200), Is.False, "KeyIntervalFunc needs more than 200 ms.");
            Assert.That(repeat.Tick(true, 201), Is.True);
            Assert.That(repeat.Tick(true, 231), Is.False);
            Assert.That(repeat.Tick(true, 232), Is.True);
            Assert.That(repeat.Tick(true, 262), Is.False);
            Assert.That(repeat.Tick(true, 263), Is.True);
        }

        [Test]
        public void ReleaseRestartsFromTheFirstPress()
        {
            var repeat = new KeyRepeat();
            repeat.Tick(true, 0); repeat.Tick(true, 300);
            Assert.That(repeat.Tick(false, 310), Is.False);
            Assert.That(repeat.Tick(true, 320), Is.True, "A new press fires at once.");
            Assert.That(repeat.Tick(true, 400), Is.False, "...and waits 200 ms again.");
            repeat.Reset();
            Assert.That(repeat.Tick(true, 401), Is.True);
        }

        [Test]
        public void FramesSkippedWhileScrollingKeepTheStage()
        {
            // CStageSongSelect does not call KeyIntervalFunc while the wheel scrolls; once it does
            // again, the held key fires as soon as its interval has passed.
            var repeat = new KeyRepeat();
            repeat.Tick(true, 0);
            Assert.That(repeat.Tick(true, 500), Is.True);
            Assert.That(repeat.Tick(true, 650), Is.True, "Later stage: 30 ms passed long ago.");
        }

        [Test]
        public void OptionMenuMoveWrapsBothWays()
        {
            var menu = new OptionMenu(new PlayOptions(), 21);
            Assert.That(menu.Move(-1), Is.True);
            Assert.That(menu.Index, Is.EqualTo(OptionMenu.Rows.Length - 1));
            Assert.That(menu.Move(+1), Is.True);
            Assert.That(menu.Index, Is.Zero);
            menu.Move(+1);
            Assert.That(menu.Current, Is.EqualTo(OptionRow.Speed));
            menu.ConfirmAll();
            Assert.That(menu.Move(+1), Is.False, "A closed panel ignores the arrows.");
        }
    }
}
