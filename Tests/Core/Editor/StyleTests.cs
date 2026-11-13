using NUnit.Framework;

namespace Threads.UiFrames.Tests
{
    public class StyleTests
    {
        static T Parse<T>(FrameProperty<T> p, string raw)
        {
            var s = new FrameStyle();
            Assert.IsTrue(s.TryApply(p, raw, out var error), error);
            Assert.IsTrue(s.TryGet(p, out var v));
            return v;
        }

        [TestCase("left", FrameEdge.Left)]
        [TestCase(" Right ", FrameEdge.Right)]
        [TestCase("\"top\"", FrameEdge.Top)]
        [TestCase("auto", FrameEdge.Auto)]
        public void EdgeKeywords(string value, FrameEdge expected) =>
            Assert.AreEqual(expected, Parse(FrameProperties.Edge, value));

        [Test]
        public void BadKeywordLeavesValueAndReportsChoices()
        {
            var s = new FrameStyle();
            s.Set(FrameProperties.Axis, FrameAxis.Vertical);
            Assert.IsFalse(s.TryApply(FrameProperties.Axis, "diagonal", out var error));
            Assert.AreEqual(FrameAxis.Vertical, s.Get(FrameProperties.Axis, FrameAxis.Horizontal));
            StringAssert.Contains("--uf-axis", error);
            StringAssert.Contains("horizontal", error);
        }

        [TestCase("160ms", 0.16f)]
        [TestCase("0.22S", 0.22f)]
        [TestCase("0ms", 0f)]
        public void Durations(string value, float seconds) =>
            Assert.AreEqual(seconds, Parse(FrameProperties.Duration, value), 1e-5f);

        [TestCase("160")]
        [TestCase("-5ms")]
        [TestCase("fast")]
        public void BadDurations(string value) =>
            Assert.IsFalse(new FrameStyle().TryApply(FrameProperties.Duration, value, out _));

        [TestCase("ease-out", FrameEasing.EaseOut)]
        [TestCase("ease-in-out", FrameEasing.EaseInOut)]
        [TestCase("linear", FrameEasing.Linear)]
        public void Easings(string value, FrameEasing expected) =>
            Assert.AreEqual(expected, Parse(FrameProperties.Easing, value));

        [Test]
        public void DecelerationMustBeAFraction()
        {
            Assert.AreEqual(0.2f, Parse(FrameProperties.Deceleration, "0.2"), 1e-6f);
            Assert.IsFalse(new FrameStyle().TryApply(FrameProperties.Deceleration, "1.5", out _));
        }

        [TestCase("all", FrameInputKinds.All)]
        [TestCase("none", FrameInputKinds.None)]
        [TestCase("touch pointer", FrameInputKinds.Touch | FrameInputKinds.Pointer)]
        [TestCase("Navigation", FrameInputKinds.Navigation)]
        public void InputKinds(string value, FrameInputKinds expected) =>
            Assert.AreEqual(expected, Parse(FrameProperties.Input, value));

        [Test]
        public void InputKindsRejectDeviceNames() =>
            Assert.IsFalse(new FrameStyle().TryApply(FrameProperties.Input, "mouse", out _));

        [Test]
        public void PropertyNamesMustStartWithDashes() =>
            Assert.Throws<System.ArgumentException>(() => FrameStyleParser.Duration("uf-duration"));

        [Test]
        public void CustomParsersKeepCase()
        {
            var name = new FrameProperty<string>("--x-sprite", (string v, out string r, out string e) => { r = v; e = null; return true; });
            Assert.AreEqual("HeroIcon", Parse(name, " \"HeroIcon\" "));
        }

        [Test]
        public void OverlayLaterWins()
        {
            var a = new FrameStyle();
            a.Set(FrameProperties.Axis, FrameAxis.Horizontal);
            a.Set(FrameProperties.Snap, FrameSnap.None);
            var b = new FrameStyle();
            b.Set(FrameProperties.Snap, FrameSnap.Card);
            a.Overlay(b);
            Assert.AreEqual(FrameAxis.Horizontal, a.Get(FrameProperties.Axis, FrameAxis.Vertical));
            Assert.AreEqual(FrameSnap.Card, a.Get(FrameProperties.Snap, FrameSnap.None));
        }

        [Test]
        public void AcceptsDefaultsToAll()
        {
            Assert.IsTrue(new FrameStyle().Accepts(FrameInputKinds.Pointer));
            var s = new FrameStyle();
            s.Set(FrameProperties.Input, FrameInputKinds.Touch);
            Assert.IsFalse(s.Accepts(FrameInputKinds.Navigation));
        }

        [Test]
        public void OptionsTakeStyleValues()
        {
            var s = new FrameStyle();
            s.Set(FrameProperties.Duration, 0.3f);
            s.Set(FrameProperties.Snap, FrameSnap.Card);
            s.Set(FrameProperties.Deceleration, 0.5f);
            var o = new StripOptions();
            o.ApplyStyle(s);
            Assert.AreEqual(0.3f, o.DurationSeconds);
            Assert.AreEqual(FrameSnap.Card, o.Snap);
            Assert.AreEqual(0.5f, o.DecelerationRate);
        }

        [Test]
        public void CommandIdsCompareByName()
        {
            Assert.AreEqual(FrameCommandId.Open, new FrameCommandId("open"));
            Assert.AreNotEqual(FrameCommandId.Open, new FrameCommandId("mypkg.open"));
            Assert.Throws<System.ArgumentException>(() => new FrameCommandId(" "));
        }
    }
}
