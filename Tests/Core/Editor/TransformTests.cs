using NUnit.Framework;
using Threads.UiFrames.Testing;

namespace Threads.UiFrames.Tests
{
    public class TransformTests
    {
        ManualClock _clock;
        FrameRegistry _registry;
        FrameEnvironment _env;
        FrameSet _set;
        FramePointerTracker _t;
        FakeFrameElement _root;

        [SetUp]
        public void SetUp()
        {
            _clock = new ManualClock();
            _registry = FrameRegistry.WithBuiltIns();
            _env = new FrameEnvironment(_clock);
            _set = new FrameSet();
            _t = new FramePointerTracker(_set, _env);
            _root = new FakeFrameElement("root", 0, 0, 1000, 800);
        }

        StripFrame Strip(FakeFrameElement parent, FrameStyle style = null)
        {
            var el = new FakeFrameElement("strip", 0, 0, 300, 100, FrameTags.CardStrip);
            var content = el.Add(new FakeFrameElement("cards", 0, 0, 1000, 100, FrameTags.Cards));
            for (var i = 0; i < 10; i++) content.Add(new FakeFrameElement("c" + i, i * 100, 0, 100, 100));
            parent.Add(el);
            var s = (StripFrame)_registry.Resolve(el).Create(new FrameContext(el, _env, _registry));
            if (style != null) s.ApplyStyle(style);
            _set.Add(s);
            return s;
        }

        void Drag(IFrameBehaviour f, float dx, float dy)
        {
            _t.Down(1, 500, 400, new[] { f });
            for (var i = 1; i <= 10; i++) { _clock.AdvanceMs(16f); _t.Move(1, 500 + dx * i / 10f, 400 + dy * i / 10f); }
            _clock.AdvanceMs(200f);
            _t.Up(1, 500 + dx, 400 + dy);
        }

        [Test]
        public void ScaledParentStillFollowsOneToOne()
        {
            var panel = _root.Add(new FakeFrameElement("panel", 0, 0, 600, 400) { Scale = 0.5f });
            var s = Strip(panel);
            Drag(s, -100f, 0f);
            Assert.AreEqual(200f, s.Model.OffsetDp, 0.5f, "200 local dp at half scale = 100 dp on screen");
        }

        [Test]
        public void RotatedStripDragsAlongItsOwnAxis()
        {
            var panel = _root.Add(new FakeFrameElement("panel", 0, 0, 600, 400) { RotationDeg = 90f });
            var s = Strip(panel);
            // Rotated 90° clockwise, the strip's "left" points up the screen.
            Drag(s, 0f, 100f);
            Assert.AreEqual(100f, s.Model.OffsetDp, 0.5f);
        }

        [Test]
        public void CrossDragOnRotatedStripIsNotTaken()
        {
            var panel = _root.Add(new FakeFrameElement("panel", 0, 0, 600, 400) { RotationDeg = 90f });
            var s = Strip(panel);
            _t.Down(1, 500, 400, new[] { (IFrameBehaviour)s });
            _t.Move(1, 560, 400); // horizontal on screen = across the rotated strip
            Assert.IsFalse(_t.IsTracking);
        }

        [Test]
        public void AxisStateClassesFollowStyle()
        {
            var s = Strip(_root);
            Assert.IsTrue(s.Element.HasClass(FrameTags.Horizontal));
            s.ApplyStyle(StyleOf.With(FrameProperties.Axis, FrameAxis.Vertical));
            Assert.IsTrue(s.Element.HasClass(FrameTags.Vertical));
            Assert.IsFalse(s.Element.HasClass(FrameTags.Horizontal));
        }
    }
}
