using System.Collections.Generic;
using NUnit.Framework;
using Threads.UiFrames.Testing;

namespace Threads.UiFrames.Tests
{
    public class PointerTrackerTests
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
            _env = new FrameEnvironment(_clock) { DragSlopDp = 8f };
            _set = new FrameSet();
            _t = new FramePointerTracker(_set, _env);
            _root = new FakeFrameElement("root", 0, 0, 800, 600);
        }

        IFrameBehaviour Add(FakeFrameElement parent, FakeFrameElement el, FrameStyle style = null)
        {
            parent.Add(el);
            var b = _registry.Resolve(el).Create(new FrameContext(el, _env, _registry));
            if (style != null) b.ApplyStyle(style);
            _set.Add(b);
            return b;
        }

        static FakeFrameElement Strip(string marker, bool vertical, int count = 10)
        {
            var root = new FakeFrameElement(marker, 0, 0, 300, 300, marker);
            var content = root.Add(vertical
                ? new FakeFrameElement("cards", 0, 0, 300, count * 100, FrameTags.Cards)
                : new FakeFrameElement("cards", 0, 0, count * 100, 300, FrameTags.Cards));
            for (var i = 0; i < count; i++)
                content.Add(vertical ? new FakeFrameElement("c" + i, 0, i * 100, 300, 100) : new FakeFrameElement("c" + i, i * 100, 0, 100, 300));
            return root;
        }

        void Step(float ms = 16f) => _clock.AdvanceMs(ms);

        [Test]
        public void SmallMoveIsNotADrag()
        {
            var s = (StripFrame)Add(_root, Strip(FrameTags.CardStrip, false));
            _t.Down(1, 100, 100, new[] { (IFrameBehaviour)s });
            Assert.IsFalse(_t.Move(1, 95, 100));
            Assert.IsFalse(_t.IsDragging);
            Assert.AreEqual(0f, s.Model.OffsetDp);
        }

        [Test]
        public void DragFollowsFromThePress()
        {
            var s = (StripFrame)Add(_root, Strip(FrameTags.CardStrip, false));
            _t.Down(1, 100, 100, new[] { (IFrameBehaviour)s });
            Step();
            Assert.IsTrue(_t.Move(1, 80, 102), "the move that starts the drag reports it");
            Assert.AreEqual(20f, s.Model.OffsetDp, 1e-3f, "1:1 from the press, slop included");
            Step();
            Assert.IsFalse(_t.Move(1, 60, 101));
            Assert.AreEqual(40f, s.Model.OffsetDp, 1e-3f);
        }

        [Test]
        public void AxisLockPassesCrossDragToOuterFrame()
        {
            var outer = (StripFrame)Add(_root, Strip(FrameTags.CardStrip, true), StyleOf.With(FrameProperties.Axis, FrameAxis.Vertical));
            var innerEl = Strip(FrameTags.CardStrip, false);
            var inner = (StripFrame)Add((FakeFrameElement)outer.Content.Children[0], innerEl);
            _t.Down(1, 100, 100, new IFrameBehaviour[] { inner, outer });
            Step();
            _t.Move(1, 102, 130); // mostly up
            Assert.AreSame(outer, _t.DragFrame);
            Assert.AreEqual(30f, outer.Model.OffsetDp, 1e-3f);
            Assert.AreEqual(0f, inner.Model.OffsetDp);
        }

        [Test]
        public void NoFrameOnThatAxisReleasesThePointer()
        {
            var s = Add(_root, Strip(FrameTags.CardStrip, false));
            _t.Down(1, 100, 100, new[] { s });
            _t.Move(1, 100, 140);
            Assert.IsFalse(_t.IsTracking, "a vertical drag is left to the UI");
        }

        [Test]
        public void FlingReleaseCoasts()
        {
            var s = (StripFrame)Add(_root, Strip(FrameTags.CardStrip, false));
            _t.Down(1, 300, 100, new[] { (IFrameBehaviour)s });
            for (var x = 290; x >= 200; x -= 10) { Step(10); _t.Move(1, x, 100); } // 1000 dp/s left
            Step(10);
            _t.Up(1, 190, 100);
            Assert.IsTrue(s.Model.IsMoving, "coasting with ~1000 dp/s");
            var before = s.Model.OffsetDp;
            Step(50);
            s.Update();
            Assert.Greater(s.Model.OffsetDp, before);
        }

        [Test]
        public void HeldStillBeforeReleaseDoesNotFling()
        {
            var s = (StripFrame)Add(_root, Strip(FrameTags.CardStrip, false));
            _t.Down(1, 300, 100, new[] { (IFrameBehaviour)s });
            for (var x = 290; x >= 200; x -= 10) { Step(10); _t.Move(1, x, 100); }
            Step(200);
            _t.Up(1, 200, 100);
            Assert.IsFalse(s.Model.IsMoving);
        }

        [Test]
        public void HandleClickTogglesDrawer()
        {
            var el = new FakeFrameElement("drawer", 0, 0, 280, 600, FrameTags.Drawer);
            var d = (DrawerFrame)Add(_root, el);
            _t.Down(1, 290, 300, new IFrameBehaviour[] { d }, handleOf: d);
            _t.Up(1, 291, 300);
            Assert.IsTrue(d.IsOpen);
        }

        [Test]
        public void DragOnHandleDragsDrawerWithoutToggling()
        {
            var el = new FakeFrameElement("drawer", 0, 0, 280, 600, FrameTags.Drawer);
            var d = (DrawerFrame)Add(_root, el);
            _t.Down(1, 10, 300, new IFrameBehaviour[] { d }, handleOf: d);
            Step();
            _t.Move(1, 60, 300);
            Assert.AreEqual(50f, d.Model.OffsetDp, 1e-3f);
            Step(200);
            _t.Up(1, 60, 300);
            Assert.IsFalse(d.IsOpen, "released before half way, no fling");
        }

        [Test]
        public void CancelSettles()
        {
            var el = new FakeFrameElement("drawer", 0, 0, 280, 600, FrameTags.Drawer);
            var d = (DrawerFrame)Add(_root, el);
            _t.Down(1, 10, 300, new IFrameBehaviour[] { d });
            _t.Move(1, 250, 300);
            _t.Cancel(1);
            Assert.IsFalse(d.Model.IsDragging);
            Assert.IsFalse(d.IsOpen);
        }

        [Test]
        public void PressFocusesInnermostFrame()
        {
            var a = Add(_root, Strip(FrameTags.CardStrip, false));
            _t.Down(1, 10, 10, new[] { a });
            Assert.AreSame(a, _set.Focused);
            Assert.IsTrue(((FakeFrameElement)a.Element).HasClass(FrameTags.Focused));
        }

        [Test]
        public void InputPropertyExcludesPointer()
        {
            var a = Add(_root, Strip(FrameTags.CardStrip, false), StyleOf.With(FrameProperties.Input, FrameInputKinds.Touch));
            _t.Down(1, 100, 100, new[] { a });
            Assert.IsFalse(_t.IsTracking);
        }

        [Test]
        public void WheelScrollsInnermostScrollableFrame()
        {
            var drawerEl = new FakeFrameElement("drawer", 0, 0, 300, 600, FrameTags.Drawer);
            var drawer = Add(_root, drawerEl);
            var strip = (StripFrame)Add(drawerEl, Strip(FrameTags.CardStrip, false));
            Assert.IsTrue(_t.Wheel(new IFrameBehaviour[] { strip, drawer }, 0f, 2f), "vertical wheel scrolls a horizontal strip");
            Assert.AreSame(strip, _set.Focused);
            for (var i = 0; i < 30; i++) { Step(); strip.Update(); }
            Assert.AreEqual(96f, strip.Model.OffsetDp, 1e-3f);
            Assert.IsFalse(_t.Wheel(new[] { drawer }, 0f, 1f), "a drawer does not use the wheel");
        }
    }

    public class FrameSetTests
    {
        FrameRegistry _registry;
        FrameEnvironment _env;
        FrameSet _set;
        FakeFrameElement _root;

        [SetUp]
        public void SetUp()
        {
            _registry = FrameRegistry.WithBuiltIns();
            _env = new FrameEnvironment(new ManualClock());
            _set = new FrameSet();
            _root = new FakeFrameElement("root", 0, 0, 800, 600);
        }

        IFrameBehaviour Add(FakeFrameElement parent, FakeFrameElement el)
        {
            parent.Add(el);
            var b = _registry.Resolve(el).Create(new FrameContext(el, _env, _registry));
            _set.Add(b);
            return b;
        }

        [Test]
        public void DispatchBubblesToOuterFrame()
        {
            var drawerEl = new FakeFrameElement("drawer", 0, 0, 300, 600, FrameTags.Drawer);
            var drawer = (DrawerFrame)Add(_root, drawerEl);
            var inner = new FakeFrameElement("strip", 0, 0, 300, 200, FrameTags.CardStrip);
            var strip = Add(drawerEl, inner);
            var handledBy = _set.Dispatch(strip, FrameCommand.Toggle, FrameInputKinds.Navigation);
            Assert.AreSame(drawer, handledBy);
            Assert.IsTrue(drawer.IsOpen);
            Assert.AreSame(drawer, _set.OuterOf(strip));
        }

        [Test]
        public void FocusMovesStateClass()
        {
            var a = Add(_root, new FakeFrameElement("a", 0, 0, 10, 10, FrameTags.CardStrip));
            var b = Add(_root, new FakeFrameElement("b", 0, 0, 10, 10, FrameTags.CardDeck));
            _set.Focus(a);
            _set.Focus(b);
            Assert.IsFalse(a.Element.HasClass(FrameTags.Focused));
            Assert.IsTrue(b.Element.HasClass(FrameTags.Focused));
        }

        [Test]
        public void CycleFocusWrapsAndSkipsNonNavigable()
        {
            var a = Add(_root, new FakeFrameElement("a", 0, 0, 10, 10, FrameTags.CardStrip));
            var b = Add(_root, new FakeFrameElement("b", 0, 0, 10, 10, FrameTags.CardStrip));
            var c = Add(_root, new FakeFrameElement("c", 0, 0, 10, 10, FrameTags.CardStrip));
            b.ApplyStyle(StyleOf.With(FrameProperties.Input, FrameInputKinds.Touch));
            _set.CycleFocus(1);
            Assert.AreSame(a, _set.Focused);
            _set.CycleFocus(1);
            Assert.AreSame(c, _set.Focused);
            _set.CycleFocus(1);
            Assert.AreSame(a, _set.Focused);
            _set.CycleFocus(-1);
            Assert.AreSame(c, _set.Focused);
        }

        [Test]
        public void RemoveClearsFocus()
        {
            var a = Add(_root, new FakeFrameElement("a", 0, 0, 10, 10, FrameTags.CardStrip));
            _set.Focus(a);
            _set.Remove(a);
            Assert.IsNull(_set.Focused);
            Assert.IsNull(_set.Find(a.Element.Native));
        }

        [Test]
        public void RemoveWorksAfterTheElementIsGone()
        {
            var el = new GoneElement();
            var b = _registry.Resolve(el).Create(new FrameContext(el, _env, _registry));
            _set.Add(b);
            el.Gone = true; // a destroyed GameObject: Native is null now
            Assert.IsTrue(_set.Remove(b));
            Assert.AreEqual(0, _set.Frames.Count);
        }

        sealed class GoneElement : IFrameElement
        {
            readonly object _native = new object();
            public bool Gone;
            public string Name => "gone";
            public object Native => Gone ? null : _native;
            public IFrameElement Parent => null;
            public System.Collections.Generic.IReadOnlyList<IFrameElement> Children => System.Array.Empty<IFrameElement>();
            public FrameRect LayoutRectDp => new FrameRect(0, 0, 100, 100);
            public FrameRect ScreenRectDp => LayoutRectDp;
            public bool HasClass(string c) => c == FrameTags.CardStrip;
            public void SetClass(string c, bool on) { }
            public void SetTranslationDp(float x, float y) { }
            public void SetVisible(bool v) { }
            public void SetSizeDp(FrameAxis axis, float size) { }
            public (float x, float y) ToLocalDeltaDp(float fx, float fy, float tx, float ty) => (tx - fx, fy - ty);
        }

        [Test]
        public void DuplicateFrameForElementThrows()
        {
            var el = new FakeFrameElement("a", 0, 0, 10, 10, FrameTags.CardStrip);
            Add(_root, el);
            var again = _registry.Resolve(el).Create(new FrameContext(el, _env, _registry));
            Assert.Throws<System.InvalidOperationException>(() => _set.Add(again));
        }

        [Test]
        public void VelocityUsesRecentWindow()
        {
            var v = new VelocityTracker();
            v.Add(0.00, 0, 0);
            v.Add(0.30, 0, 0);   // old, outside the window from the newest
            v.Add(0.35, 50, 0);
            v.Add(0.40, 100, 0);
            var (x, y) = v.Velocity(0.41);
            Assert.AreEqual(1000f, x, 1f);
            Assert.AreEqual(0f, y);
            Assert.AreEqual((0f, 0f), v.Velocity(0.5), "held still");
        }
    }
}
