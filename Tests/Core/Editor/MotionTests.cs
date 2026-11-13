using NUnit.Framework;
using Threads.UiFrames.Testing;

namespace Threads.UiFrames.Tests
{
    public class MotionTests
    {
        ManualClock _clock;
        AxisMotion _m;

        [SetUp]
        public void SetUp()
        {
            _clock = new ManualClock();
            _m = new AxisMotion(_clock) { RubberBandLimitDp = 60f };
            _m.SetBounds(0f, 1000f);
        }

        [TestCase(FrameEasing.Linear)]
        [TestCase(FrameEasing.EaseIn)]
        [TestCase(FrameEasing.EaseOut)]
        [TestCase(FrameEasing.EaseInOut)]
        public void EasingsRunFromZeroToOne(FrameEasing e)
        {
            Assert.AreEqual(0f, Easing.Apply(e, 0f), 1e-6f);
            Assert.AreEqual(1f, Easing.Apply(e, 1f), 1e-6f);
            Assert.AreEqual(1f, Easing.Apply(e, 2f), 1e-6f);
        }

        [Test]
        public void EaseOutIsFastFirst() => Assert.Greater(Easing.Apply(FrameEasing.EaseOut, 0.5f), 0.5f);

        [Test]
        public void RubberBandNeverReachesLimitAndInverts()
        {
            Assert.Less(RubberBand.Apply(10000f, 60f), 60f);
            var shown = RubberBand.Apply(80f, 60f);
            Assert.AreEqual(80f, RubberBand.Inverse(shown, 60f), 0.01f);
        }

        [Test]
        public void DragFollowsOneToOneInsideBounds()
        {
            _m.JumpTo(100f);
            _m.DragBy(50f);
            Assert.AreEqual(150f, _m.Position, 1e-4f);
            Assert.AreEqual(MotionState.Dragging, _m.State);
        }

        [Test]
        public void DragRubberBandsPastTheEnd()
        {
            _m.DragBy(-100f);
            Assert.Less(_m.Position, 0f);
            Assert.Greater(_m.Position, -60f);
            _m.DragBy(100f); // back where it started: no drift from the rubber band
            Assert.AreEqual(0f, _m.Position, 1e-3f);
        }

        [Test]
        public void AnimationDoesNotMoveUntilTimePasses()
        {
            _m.AnimateTo(500f, 0.16f, FrameEasing.EaseOut);
            _m.Update();
            Assert.AreEqual(0f, _m.Position);
            _clock.AdvanceMs(80);
            _m.Update();
            Assert.Greater(_m.Position, 250f); // ease-out: more than half way at half time
            _clock.AdvanceMs(80);
            _m.Update();
            Assert.AreEqual(500f, _m.Position);
            Assert.AreEqual(MotionState.Idle, _m.State);
        }

        [Test]
        public void ZeroDurationJumpsAndSettles()
        {
            var settled = 0;
            _m.Settled += () => settled++;
            _m.AnimateTo(300f, 0f, FrameEasing.EaseOut);
            Assert.AreEqual(300f, _m.Position);
            Assert.AreEqual(1, settled);
        }

        [Test]
        public void DragStopsAnAnimation()
        {
            _m.AnimateTo(500f, 0.2f, FrameEasing.Linear);
            _clock.AdvanceMs(100);
            _m.Update();
            var p = _m.Position;
            _m.BeginDrag();
            _clock.AdvanceMs(100);
            _m.Update();
            Assert.AreEqual(p, _m.Position, "no animation while dragging");
        }

        [Test]
        public void CoastStopsAtProjectedRest()
        {
            _m.JumpTo(100f);
            var rest = _m.ProjectedRest(500f, 0.135f);
            Assert.AreEqual(100f + 500f / 2.0024f, rest, 0.5f); // -1/ln(0.135) ≈ 0.4994
            _m.Coast(500f, 0.135f, 0.16f, FrameEasing.EaseOut);
            for (var i = 0; i < 600 && _m.IsMoving; i++) { _clock.AdvanceMs(16.67f); _m.Update(); }
            Assert.AreEqual(MotionState.Idle, _m.State);
            Assert.AreEqual(rest, _m.Position, 2f);
        }

        [Test]
        public void CoastStopsAtBounds()
        {
            _m.JumpTo(990f);
            _m.Coast(2000f, 0.135f, 0.16f, FrameEasing.EaseOut);
            for (var i = 0; i < 600 && _m.IsMoving; i++) { _clock.AdvanceMs(16.67f); _m.Update(); }
            Assert.AreEqual(1000f, _m.Position);
        }

        [Test]
        public void CoastFromOvershootSpringsBack()
        {
            _m.DragBy(-50f);
            _m.EndDrag();
            _m.Coast(-300f, 0.135f, 0.16f, FrameEasing.EaseOut);
            Assert.AreEqual(MotionState.Animating, _m.State);
            Assert.AreEqual(0f, _m.Target);
        }

        [TestCase(FrameEdge.Left, 10f, 0f, 10f)]
        [TestCase(FrameEdge.Right, 10f, 0f, -10f)]
        [TestCase(FrameEdge.Bottom, 0f, 10f, 10f)]
        [TestCase(FrameEdge.Top, 0f, 10f, -10f)]
        public void DrawerDelta(FrameEdge edge, float dx, float dy, float expected) =>
            Assert.AreEqual(expected, FrameAxisMath.DrawerDelta(edge, dx, dy));

        [Test]
        public void ScrollDeltaRevealsLaterContent()
        {
            Assert.AreEqual(10f, FrameAxisMath.ScrollDelta(FrameAxis.Horizontal, -10f, 0f), "drag left");
            Assert.AreEqual(10f, FrameAxisMath.ScrollDelta(FrameAxis.Vertical, 0f, 10f), "drag up (y up)");
        }

        [TestCase(30f, 0f, FrameEdge.Left)]
        [TestCase(-30f, 5f, FrameEdge.Right)]
        [TestCase(0f, 30f, FrameEdge.Bottom)]
        [TestCase(2f, -30f, FrameEdge.Top)]
        public void InferEdgeFromHandle(float dx, float dy, FrameEdge expected) =>
            Assert.AreEqual(expected, FrameAxisMath.InferEdge(dx, dy));
    }
}
