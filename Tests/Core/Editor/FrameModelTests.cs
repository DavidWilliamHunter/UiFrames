using System.Collections.Generic;
using NUnit.Framework;
using Threads.UiFrames.Testing;

namespace Threads.UiFrames.Tests
{
    public class DrawerModelTests
    {
        ManualClock _clock;
        DrawerModel _d;
        List<bool> _openChanges;

        [SetUp]
        public void SetUp()
        {
            _clock = new ManualClock();
            _d = new DrawerModel(_clock) { ExtentDp = 300f };
            _openChanges = new List<bool>();
            _d.OpenChanged += _openChanges.Add;
        }

        void Run(float ms) { for (var t = 0f; t < ms; t += 16.67f) { _clock.AdvanceMs(16.67f); _d.Update(); } }

        [Test]
        public void OpensIn220msByDefault()
        {
            _d.Open();
            Assert.IsTrue(_d.IsOpen);
            Assert.AreEqual(0f, _d.Progress);
            _clock.AdvanceMs(110);
            _d.Update();
            Assert.That(_d.Progress, Is.InRange(0.5f, 1f));
            _clock.AdvanceMs(110);
            _d.Update();
            Assert.AreEqual(1f, _d.Progress);
            CollectionAssert.AreEqual(new[] { true }, _openChanges);
        }

        [Test]
        public void ToggleCommand()
        {
            Assert.IsTrue(_d.Execute(FrameCommand.Toggle));
            Run(300);
            Assert.AreEqual(1f, _d.Progress);
            _d.Execute(FrameCommand.Toggle);
            Run(300);
            Assert.AreEqual(0f, _d.Progress);
        }

        [Test]
        public void UnhandledCommandsReturnFalse() => Assert.IsFalse(_d.Execute(FrameCommand.Next));

        [Test]
        public void DragFollowsFinger()
        {
            _d.Execute(FrameCommand.DragBegin);
            _d.Execute(FrameCommand.DragMove(90f));
            Assert.AreEqual(0.3f, _d.Progress, 1e-4f);
            Assert.IsTrue(_d.IsDragging);
        }

        [Test]
        public void SlowReleasePastHalfOpens()
        {
            _d.Execute(FrameCommand.DragMove(160f));
            _d.Execute(FrameCommand.DragEnd(0f));
            Run(300);
            Assert.IsTrue(_d.IsOpen);
            Assert.AreEqual(1f, _d.Progress);
        }

        [Test]
        public void SlowReleaseBeforeHalfStaysClosed()
        {
            _d.Execute(FrameCommand.DragMove(100f));
            _d.Execute(FrameCommand.DragEnd(100f));
            Run(300);
            Assert.IsFalse(_d.IsOpen);
            Assert.AreEqual(0f, _d.Progress);
            CollectionAssert.IsEmpty(_openChanges);
        }

        [Test]
        public void FlingOpensFromAShortDrag()
        {
            _d.Execute(FrameCommand.DragMove(30f));
            _d.Execute(FrameCommand.DragEnd(800f));
            Assert.IsTrue(_d.IsOpen);
        }

        [Test]
        public void FlingClosesFromNearlyOpen()
        {
            _d.Open(animate: false);
            _d.Execute(FrameCommand.DragMove(-20f));
            _d.Execute(FrameCommand.DragEnd(-800f));
            Assert.IsFalse(_d.IsOpen);
        }

        [Test]
        public void RubberBandsPastOpen()
        {
            _d.Open(animate: false);
            _d.Execute(FrameCommand.DragMove(200f));
            Assert.That(_d.Progress, Is.InRange(1.0001f, 1.2f));
        }

        [Test]
        public void OpenIsIgnoredWhileDragging()
        {
            _d.Execute(FrameCommand.DragMove(10f));
            _d.Open();
            Assert.IsFalse(_d.IsOpen);
        }

        [Test]
        public void CancelGoesBackToItsState()
        {
            _d.Execute(FrameCommand.DragMove(250f));
            _d.Execute(FrameCommand.DragCancel);
            Run(300);
            Assert.IsFalse(_d.IsOpen);
            Assert.AreEqual(0f, _d.Progress);
        }

        [Test]
        public void ResizeKeepsOpenState()
        {
            _d.Open(animate: false);
            _d.ExtentDp = 400f;
            Assert.AreEqual(1f, _d.Progress);
            Assert.AreEqual(400f, _d.OffsetDp);
        }

        [Test]
        public void StyleSetsDuration()
        {
            _d.ApplyStyle(StyleOf.With(FrameProperties.Duration, 0f));
            _d.Open();
            Assert.AreEqual(1f, _d.Progress);
        }
    }

    public class StripModelTests
    {
        ManualClock _clock;
        StripModel _s;

        [SetUp]
        public void SetUp()
        {
            _clock = new ManualClock();
            _s = new StripModel(_clock);
            // 10 cards of 100 dp with 10 dp gaps in a 350 dp viewport: content 1090, max 740.
            _s.SetLayout(CardLayout.Uniform(350f, 10, 100f, 10f));
        }

        void Run(float ms = 3000f)
        {
            for (var t = 0f; t < ms && _s.IsMoving; t += 16.67f) { _clock.AdvanceMs(16.67f); _s.Update(); }
        }

        [Test]
        public void MaxOffset() => Assert.AreEqual(740f, _s.MaxOffsetDp);

        [Test]
        public void DragThenInertia()
        {
            _s.Execute(FrameCommand.DragMove(100f));
            Assert.AreEqual(100f, _s.OffsetDp);
            _s.Execute(FrameCommand.DragEnd(300f));
            Assert.IsTrue(_s.IsMoving);
            Run();
            Assert.AreEqual(100f + 300f / 2.0025f, _s.OffsetDp, 2f);
        }

        [Test]
        public void ReleasePastStartSpringsBack()
        {
            _s.Execute(FrameCommand.DragMove(-80f));
            Assert.Less(_s.OffsetDp, 0f);
            _s.Execute(FrameCommand.DragEnd(0f));
            Run();
            Assert.AreEqual(0f, _s.OffsetDp);
        }

        [Test]
        public void InertiaStopsAtEnd()
        {
            _s.Execute(FrameCommand.DragMove(700f));
            _s.Execute(FrameCommand.DragEnd(5000f));
            Run();
            Assert.AreEqual(740f, _s.OffsetDp);
        }

        [Test]
        public void SnapSettlesOnACard()
        {
            _s.ApplyStyle(StyleOf.With(FrameProperties.Snap, FrameSnap.Card));
            _s.Execute(FrameCommand.DragMove(130f));
            _s.Execute(FrameCommand.DragEnd(0f));
            Run();
            Assert.AreEqual(110f, _s.OffsetDp);
        }

        [Test]
        public void SnapUsesProjectedRest()
        {
            _s.ApplyStyle(StyleOf.With(FrameProperties.Snap, FrameSnap.Card));
            _s.Execute(FrameCommand.DragMove(10f));
            _s.Execute(FrameCommand.DragEnd(450f)); // projects ~235 → card 2 at 220
            Run();
            Assert.AreEqual(220f, _s.OffsetDp);
        }

        [Test]
        public void SnapCanReachTheEnd()
        {
            _s.ApplyStyle(StyleOf.With(FrameProperties.Snap, FrameSnap.Card));
            _s.Execute(FrameCommand.DragMove(730f));
            _s.Execute(FrameCommand.DragEnd(0f));
            Run();
            Assert.AreEqual(740f, _s.OffsetDp);
        }

        [Test]
        public void NextAndPreviousStepByCard()
        {
            _s.Execute(FrameCommand.Next);
            _s.Execute(FrameCommand.Next); // stacks on the animation target
            Run();
            Assert.AreEqual(220f, _s.OffsetDp);
            _s.Execute(FrameCommand.Previous);
            Run();
            Assert.AreEqual(110f, _s.OffsetDp);
        }

        [Test]
        public void PageStepsByViewport()
        {
            _s.Execute(FrameCommand.PageNext);
            Run();
            Assert.AreEqual(350f, _s.OffsetDp);
        }

        [Test]
        public void ScrollByIsAnimatedAndClamped()
        {
            _s.Execute(FrameCommand.ScrollBy(5000f));
            Assert.AreNotEqual(740f, _s.OffsetDp);
            Run();
            Assert.AreEqual(740f, _s.OffsetDp);
        }

        [Test]
        public void SmallScrollStillStepsASnappingStrip()
        {
            _s.ApplyStyle(StyleOf.With(FrameProperties.Snap, FrameSnap.Card));
            _s.Execute(FrameCommand.ScrollBy(20f));
            Run();
            Assert.AreEqual(110f, _s.OffsetDp);
        }

        [Test]
        public void HomeEndAndGoTo()
        {
            _s.Execute(FrameCommand.End);
            Run();
            Assert.AreEqual(740f, _s.OffsetDp);
            _s.Execute(FrameCommand.GoTo(3));
            Run();
            Assert.AreEqual(330f, _s.OffsetDp);
            _s.Execute(FrameCommand.Home);
            Run();
            Assert.AreEqual(0f, _s.OffsetDp);
        }

        [Test]
        public void ShortContentDoesNotScroll()
        {
            _s.SetLayout(CardLayout.Uniform(350f, 2, 100f));
            _s.Execute(FrameCommand.DragMove(50f));
            _s.Execute(FrameCommand.DragEnd(1000f));
            Run();
            Assert.AreEqual(0f, _s.OffsetDp);
        }

        [Test]
        public void NotOpenable() => Assert.IsFalse(_s.Execute(FrameCommand.Open));
    }

    public class DeckModelTests
    {
        ManualClock _clock;
        DeckModel _d;
        List<int> _indices;

        [SetUp]
        public void SetUp()
        {
            _clock = new ManualClock();
            _d = new DeckModel(_clock);
            _indices = new List<int>();
            _d.IndexChanged += _indices.Add;
            // 6 cards of 100 dp in a 300 dp viewport: 3 visible, last rest index 3.
            _d.SetLayout(CardLayout.Uniform(300f, 6, 100f));
        }

        void Run(float ms = 1000f)
        {
            for (var t = 0f; t < ms && _d.IsMoving; t += 16.67f) { _clock.AdvanceMs(16.67f); _d.Update(); }
        }

        [Test]
        public void Layout()
        {
            Assert.AreEqual(3, _d.LastIndex);
            Assert.AreEqual(3, _d.VisibleCount);
        }

        [Test]
        public void NextAnimatesIn160ms()
        {
            _d.Next();
            Assert.AreEqual(1, _d.Index);
            _clock.AdvanceMs(160);
            _d.Update();
            Assert.AreEqual(100f, _d.OffsetDp);
            CollectionAssert.AreEqual(new[] { 1 }, _indices);
        }

        [Test]
        public void PageStep()
        {
            _d.ApplyStyle(StyleOf.With(FrameProperties.DeckStep, FrameDeckStep.Page));
            _d.Execute(FrameCommand.Next);
            Assert.AreEqual(3, _d.Index);
            _d.Execute(FrameCommand.Next);
            Assert.AreEqual(3, _d.Index, "clamped to the last rest index");
        }

        [Test]
        public void SlowDragSnapsToNearest()
        {
            _d.Execute(FrameCommand.DragMove(160f));
            _d.Execute(FrameCommand.DragEnd(50f));
            Run();
            Assert.AreEqual(2, _d.Index);
            Assert.AreEqual(200f, _d.OffsetDp);
        }

        [Test]
        public void FlingStepsOneCardFromAShortDrag()
        {
            _d.Execute(FrameCommand.DragMove(20f));
            _d.Execute(FrameCommand.DragEnd(900f));
            Assert.AreEqual(1, _d.Index);
        }

        [Test]
        public void FlingBack()
        {
            _d.GoTo(2, animate: false);
            _d.Execute(FrameCommand.DragMove(-20f));
            _d.Execute(FrameCommand.DragEnd(-900f));
            Assert.AreEqual(1, _d.Index);
        }

        [Test]
        public void FlingByPage()
        {
            _d.ApplyStyle(StyleOf.With(FrameProperties.DeckStep, FrameDeckStep.Page));
            _d.Execute(FrameCommand.DragMove(20f));
            _d.Execute(FrameCommand.DragEnd(900f));
            Assert.AreEqual(3, _d.Index);
        }

        [Test]
        public void AlwaysRestsOnWholeCards()
        {
            // A viewport that is not a whole number of cards: 250 dp shows 2.5 cards.
            _d.SetLayout(CardLayout.Uniform(250f, 6, 100f));
            Assert.AreEqual(4, _d.LastIndex, "rest at 400 rather than cut a card at the start");
            _d.Execute(FrameCommand.DragMove(130f));
            Assert.IsTrue(_d.IsCardShownAtRest(3), "cut-off cards show while moving");
            _d.Execute(FrameCommand.DragEnd(0f));
            Run();
            Assert.AreEqual(100f, _d.OffsetDp);
            Assert.IsTrue(_d.IsCardShownAtRest(1));
            Assert.IsTrue(_d.IsCardShownAtRest(2));
            Assert.IsFalse(_d.IsCardShownAtRest(3), "a cut-off card is hidden at rest");
        }

        [Test]
        public void ScrollStepsOneCard()
        {
            _d.Execute(FrameCommand.ScrollBy(3f));
            Assert.AreEqual(1, _d.Index);
            _d.Execute(FrameCommand.ScrollBy(-3f));
            Assert.AreEqual(0, _d.Index);
        }

        [Test]
        public void CancelReturnsToIndex()
        {
            _d.Execute(FrameCommand.DragMove(180f));
            _d.Execute(FrameCommand.DragCancel);
            Run();
            Assert.AreEqual(0, _d.Index);
            Assert.AreEqual(0f, _d.OffsetDp);
        }

        [Test]
        public void ShrinkingLayoutClampsIndex()
        {
            _d.GoTo(3, animate: false);
            _d.SetLayout(CardLayout.Uniform(300f, 4, 100f));
            Assert.AreEqual(1, _d.Index);
            Assert.AreEqual(100f, _d.OffsetDp);
        }

        [Test]
        public void RubberBandsPastTheEnds()
        {
            _d.Execute(FrameCommand.DragMove(-100f));
            Assert.Less(_d.OffsetDp, 0f);
            Assert.Greater(_d.OffsetDp, -60f);
        }
    }
}
