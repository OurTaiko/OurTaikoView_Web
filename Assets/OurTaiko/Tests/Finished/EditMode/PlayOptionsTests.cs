using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace OurTaiko.Tests
{
    public sealed class PlayOptionsTests
    {
        static TaikoChart Parse(string body) => TjaParser.Parse("TITLE:Test\nBPM:120\nCOURSE:Oni\n#START\n" + body + "\n#END");

        [Test]
        public void SpeedStepsMatchModifierCpp()
        {
            Assert.That(PlayOptions.StepSpeed(10, +1), Is.EqualTo(11));
            Assert.That(PlayOptions.StepSpeed(19, +1), Is.EqualTo(20));
            Assert.That(PlayOptions.StepSpeed(20, +1), Is.EqualTo(30));
            Assert.That(PlayOptions.StepSpeed(30, +1), Is.EqualTo(40));
            Assert.That(PlayOptions.StepSpeed(40, +1), Is.EqualTo(1), "Wraps to 0.1.");
            Assert.That(PlayOptions.StepSpeed(1, -1), Is.EqualTo(40));
            Assert.That(PlayOptions.StepSpeed(40, -1), Is.EqualTo(30));
            Assert.That(PlayOptions.StepSpeed(30, -1), Is.EqualTo(20));
            Assert.That(PlayOptions.StepSpeed(20, -1), Is.EqualTo(19));
            Assert.That(PlayOptions.SpeedBadge(10), Is.EqualTo(-1));
            Assert.That(PlayOptions.SpeedBadge(5), Is.EqualTo(-1));
            Assert.That(PlayOptions.SpeedBadgeValues[PlayOptions.SpeedBadge(11)], Is.EqualTo(11));
            Assert.That(PlayOptions.SpeedBadgeValues[PlayOptions.SpeedBadge(30)], Is.EqualTo(30));
            Assert.That(PlayOptions.SpeedBadgeValues[PlayOptions.SpeedBadge(40)], Is.EqualTo(40));
        }

        [Test]
        public void MenuWalksRowsAndChangesValues()
        {
            var options = new PlayOptions();
            var menu = new OptionMenu(options, 21);
            Assert.That(menu.Current, Is.EqualTo(OptionRow.Auto));
            Assert.That(menu.Right(), Is.True);
            Assert.That(options.auto && menu.IsChanged(OptionRow.Auto), Is.True);
            menu.Confirm();
            menu.Left();
            Assert.That(options.speed, Is.EqualTo(9));
            menu.Confirm(); menu.Right();
            menu.Confirm(); menu.Right();
            Assert.That(options.display && options.inverse, Is.True);
            menu.Confirm(); menu.Left();
            Assert.That(options.random, Is.EqualTo(RandomMode.Detarame), "Left wraps off -> でたらめ.");
            menu.Right(); menu.Right();
            Assert.That(options.random, Is.EqualTo(RandomMode.Kimagure));
            menu.Confirm();
            Assert.That(menu.Current, Is.EqualTo(OptionRow.Skip));
            Assert.That(menu.Right() || menu.Left(), Is.False, "演奏スキップ is greyed in single play.");
            menu.Confirm();
            menu.Left();
            Assert.That(options.neiro, Is.EqualTo(PlayOptions.Mute), "Left from set 0 reaches 無音.");
            Assert.That(menu.NeiroSlot, Is.EqualTo(21));
            menu.Right();
            Assert.That(options.neiro, Is.EqualTo(0));
            menu.Left(); menu.Left();
            Assert.That(options.neiro, Is.EqualTo(20));
            menu.Confirm();
            Assert.That(menu.IsConfirmed, Is.True);
            Assert.That(menu.Left(), Is.False);
            menu.Select(0);
            Assert.That(menu.Index, Is.EqualTo(OptionMenu.Rows.Length), "A confirmed menu keeps its cursor.");
        }

        [Test]
        public void PanelSlidesThroughTheOvershoot()
        {
            Assert.That(OptionMenu.SlideIn(0), Is.EqualTo(0));
            Assert.That(OptionMenu.SlideIn(OptionMenu.SlideMs * 0.7368), Is.EqualTo(623).Within(1e-6));
            Assert.That(OptionMenu.SlideIn(1000), Is.EqualTo(548));
            Assert.That(OptionMenu.SlideOut(OptionMenu.SlideMs * 0.2105), Is.EqualTo(-75).Within(1e-6));
            Assert.That(OptionMenu.SlideOut(1000), Is.EqualTo(548));
        }

        [Test]
        public void ModifiersChangeTheChart()
        {
            var plain = Parse("1234,\n5000000000000008,");
            var chart = Parse("1234,\n5000000000000008,");
            var options = new PlayOptions { inverse = true, display = true, speed = 30 };
            ChartModifiers.Apply(chart, options, new System.Random(1));
            Assert.That(chart.Notes.Select(n => n.Kind).Take(4),
                Is.EqualTo(new[] { NoteKind.Ka, NoteKind.Don, NoteKind.BigKa, NoteKind.BigDon }));
            Assert.That(chart.Notes.Last().Kind, Is.EqualTo(NoteKind.Roll));
            Assert.That(chart.Notes.All(n => !n.Display), Is.True);
            Assert.That(chart.Bars.All(b => b.Display), Is.True, "ドロン keeps the bar lines.");
            for (int i = 0; i < chart.Notes.Count; i++)
                Assert.That(chart.Notes[i].ScrollX, Is.EqualTo(plain.Notes[i].ScrollX * 3).Within(1e-12));
            Assert.That(chart.Bars[0].ScrollX, Is.EqualTo(plain.Bars[0].ScrollX * 3).Within(1e-12));
        }

        [TestCase(RandomMode.Kimagure, ChartModifiers.KimagureChance)]
        [TestCase(RandomMode.Detarame, ChartModifiers.DetarameChance)]
        public void RandomSwapsEachNoteWithItsChance(RandomMode mode, double chance)
        {
            var body = string.Join(",\n", Enumerable.Repeat("1111", 1000)) + ",";
            var chart = Parse(body);
            ChartModifiers.Apply(chart, new PlayOptions { random = mode }, new System.Random(7));
            double swapped = chart.Notes.Count(n => n.Kind == NoteKind.Ka) / (double)chart.Notes.Count;
            Assert.That(chart.Notes.Count, Is.EqualTo(4000));
            Assert.That(swapped, Is.EqualTo(chance).Within(0.03));
        }

        [Test]
        public void OptionsSaveAndLoad()
        {
            string path = Path.Combine(Application.temporaryCachePath, "editmode-options.json");
            if (File.Exists(path)) File.Delete(path);
            try
            {
                var options = PlayOptions.Load(path);
                Assert.That(options.speed, Is.EqualTo(PlayOptions.DefaultSpeed));
                options.speed = 25; options.random = RandomMode.Detarame; options.neiro = PlayOptions.Mute; options.auto = true;
                options.Save();
                var loaded = PlayOptions.Load(path);
                Assert.That((loaded.speed, loaded.random, loaded.neiro, loaded.auto), Is.EqualTo((25, RandomMode.Detarame, -1, true)));
                File.WriteAllText(path, "{\"speed\": 99, \"random\": 7}");
                loaded = PlayOptions.Load(path);
                Assert.That((loaded.speed, loaded.random), Is.EqualTo((PlayOptions.MaxSpeed, RandomMode.Off)));
                new PlayOptions { speed = 5 }.Save();
                Assert.That(PlayOptions.Load(path).speed, Is.EqualTo(PlayOptions.MaxSpeed), "Unsaved instances never write.");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
    }
}
