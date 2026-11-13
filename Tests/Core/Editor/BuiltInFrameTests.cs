using NUnit.Framework;
using Threads.UiFrames.Testing;

namespace Threads.UiFrames.Tests
{
    public class BuiltInFrameTests
    {
        ManualClock _clock;
        FrameRegistry _registry;
        FrameEnvironment _env;

        [SetUp]
        public void SetUp()
        {
            _clock = new ManualClock();
            _registry = FrameRegistry.WithBuiltIns();
            _env = new FrameEnvironment(_clock);
        }

        IFrameBehaviour Create(IFrameElement el, FrameStyle style = null)
        {
            var b = _registry.Resolve(el).Create(new FrameContext(el, _env, _registry));
            if (style != null) b.ApplyStyle(style);
            return b;
        }

        void Run(IFrameBehaviour b, float ms = 1000f)
        {
            for (var t = 0f; t < ms; t += 16.67f) { _clock.AdvanceMs(16.67f); b.Update(); }
        }

        // ---- Drawer ----

        [TestCase(FrameEdge.Left, -280f, 0f)]
        [TestCase(FrameEdge.Right, 280f, 0f)]
        [TestCase(FrameEdge.Top, 0f, -600f)]
        [TestCase(FrameEdge.Bottom, 0f, 600f)]
        public void ClosedDrawerIsMovedOffItsEdge(FrameEdge edge, float x, float y)
        {
            var el = new FakeFrameElement("drawer", 0, 0, 280, 600, FrameTags.Drawer);
            Create(el, StyleOf.With(FrameProperties.Edge, edge));
            Assert.AreEqual(x, el.TranslationX);
            Assert.AreEqual(y, el.TranslationY);
        }

        [Test]
        public void DrawerOpensAndSetsStateClasses()
        {
            var el = new FakeFrameElement("drawer", 0, 0, 280, 600, FrameTags.Drawer);
            var d = (DrawerFrame)Create(el);
            d.Execute(FrameCommand.DragMove(10f));
            Assert.IsTrue(el.HasClass(FrameTags.Dragging));
            Assert.AreEqual(-270f, el.TranslationX);
            d.Execute(FrameCommand.DragEnd(1000f));
            Assert.IsFalse(el.HasClass(FrameTags.Dragging));
            Assert.IsTrue(el.HasClass(FrameTags.Open));
            Run(d);
            Assert.AreEqual(0f, el.TranslationX);
        }

        [Test]
        public void DrawerScreenMotionMapsByEdge()
        {
            var el = new FakeFrameElement("drawer", 0, 0, 600, 280, FrameTags.Drawer);
            var d = Create(el, StyleOf.With(FrameProperties.Edge, FrameEdge.Bottom));
            Assert.AreEqual(FrameAxis.Vertical, d.DragAxis);
            Assert.AreEqual(25f, d.ToCommandDelta(3f, -25f), "dragging up (y down: negative) opens a bottom drawer");
        }

        [TestCase(285f, 300f, FrameEdge.Left)]   // handle sticks out on the right
        [TestCase(-45f, 300f, FrameEdge.Right)]  // on the left
        [TestCase(140f, -40f, FrameEdge.Bottom)] // above (y down)
        [TestCase(140f, 640f, FrameEdge.Top)]    // below
        public void AutoEdgeFromHandle(float hx, float hy, FrameEdge expected)
        {
            var el = new FakeFrameElement("drawer", 0, 0, 280, 600, FrameTags.Drawer);
            el.Add(new FakeFrameElement("handle", hx - 20, hy - 20, 40, 40, FrameTags.DrawerHandle));
            var d = (DrawerFrame)Create(el);
            Assert.AreEqual(expected, d.Edge);
        }

        [Test]
        public void DrawerWithoutHandleOrEdgeDefaultsLeft()
        {
            var d = (DrawerFrame)Create(new FakeFrameElement("drawer", 0, 0, 280, 600, FrameTags.Drawer));
            Assert.AreEqual(FrameEdge.Left, d.Edge);
        }

        [Test]
        public void DrawerUsesEnvironmentFlingThreshold()
        {
            _env.FlingThresholdDps = 2000f;
            var d = (DrawerFrame)Create(new FakeFrameElement("drawer", 0, 0, 280, 600, FrameTags.Drawer));
            d.Execute(FrameCommand.DragMove(20f));
            d.Execute(FrameCommand.DragEnd(1000f));
            Assert.IsFalse(d.IsOpen, "1000 dp/s is below this platform's fling threshold");
        }

        [Test]
        public void DrawerDisposeResets()
        {
            var el = new FakeFrameElement("drawer", 0, 0, 280, 600, FrameTags.Drawer);
            var d = Create(el);
            d.Execute(FrameCommand.DragMove(10f));
            d.Dispose();
            Assert.AreEqual(0f, el.TranslationX);
            Assert.IsFalse(el.HasClass(FrameTags.Dragging));
        }

        // ---- Strip and deck ----

        static FakeFrameElement Cards(string marker, int count, float viewport = 300f, float card = 100f, bool vertical = false)
        {
            var root = vertical
                ? new FakeFrameElement("frame", 0, 0, 200, viewport, marker)
                : new FakeFrameElement("frame", 0, 0, viewport, 200, marker);
            var content = root.Add(vertical
                ? new FakeFrameElement("cards", 0, 0, 200, count * card, FrameTags.Cards)
                : new FakeFrameElement("cards", 0, 0, count * card, 200, FrameTags.Cards));
            for (var i = 0; i < count; i++)
                content.Add(vertical
                    ? new FakeFrameElement("card" + i, 0, i * card, 200, card)
                    : new FakeFrameElement("card" + i, i * card, 0, card, 200));
            return root;
        }

        [Test]
        public void StripMeasuresCardsAndMovesContent()
        {
            var el = Cards(FrameTags.CardStrip, 10);
            var s = (StripFrame)Create(el);
            Assert.AreEqual(700f, s.Model.MaxOffsetDp);
            s.Execute(FrameCommand.DragMove(s.ToCommandDelta(-120f, 0f))); // finger moves left
            Assert.AreEqual(-120f, ((FakeFrameElement)s.Content).TranslationX);
        }

        [Test]
        public void VerticalStripFromStyle()
        {
            var el = Cards(FrameTags.CardStrip, 10, vertical: true);
            var s = (StripFrame)Create(el, StyleOf.With(FrameProperties.Axis, FrameAxis.Vertical));
            Assert.AreEqual(FrameAxis.Vertical, s.DragAxis);
            Assert.AreEqual(700f, s.Model.MaxOffsetDp);
            s.Execute(FrameCommand.DragMove(s.ToCommandDelta(0f, -50f))); // finger moves up (local y down)
            Assert.AreEqual(-50f, ((FakeFrameElement)s.Content).TranslationY, "content moves up (y down)");
        }

        [Test]
        public void StripWithoutCardsIsInert()
        {
            var s = (StripFrame)Create(new FakeFrameElement("strip", 0, 0, 300, 200, FrameTags.CardStrip));
            Assert.IsNull(s.Content);
            Assert.IsTrue(s.Execute(FrameCommand.DragMove(50f)));
        }

        [Test]
        public void DeckHidesCutOffCardsOnlyAtRest()
        {
            var el = Cards(FrameTags.CardDeck, 6, viewport: 250f);
            var d = (DeckFrame)Create(el);
            var content = (FakeFrameElement)d.Content;
            Assert.IsTrue(Card(content, 1).Visible);
            Assert.IsFalse(Card(content, 2).Visible, "half of card 2 would show at rest");
            d.Execute(FrameCommand.Next);
            Assert.IsTrue(Card(content, 2).Visible, "shown while moving");
            Run(d);
            Assert.AreEqual(1, d.Index);
            Assert.AreEqual(-100f, content.TranslationX);
            Assert.IsTrue(Card(content, 0).Visible, "wholly out of view: clipped, still focusable");
            Assert.IsTrue(Card(content, 2).Visible);
            Assert.IsFalse(Card(content, 3).Visible);
        }

        [Test]
        public void DeckShowsAllCardsWhileDragging()
        {
            var el = Cards(FrameTags.CardDeck, 6, viewport: 250f);
            var d = (DeckFrame)Create(el);
            d.Execute(FrameCommand.DragMove(30f));
            Assert.IsTrue(Card((FakeFrameElement)d.Content, 2).Visible);
            Assert.IsTrue(el.HasClass(FrameTags.Dragging));
        }

        [Test]
        public void LayoutChangeRemeasures()
        {
            var el = Cards(FrameTags.CardStrip, 10);
            var s = (StripFrame)Create(el);
            el.LayoutRectDp = new FrameRect(0, 0, 500, 200);
            s.OnLayoutChanged();
            Assert.AreEqual(500f, s.Model.MaxOffsetDp);
        }

        [Test]
        public void InputPropertyIsAvailableToControllers()
        {
            var s = Create(Cards(FrameTags.CardStrip, 3), StyleOf.With(FrameProperties.Input, FrameInputKinds.Touch));
            Assert.IsTrue(s.Style.Accepts(FrameInputKinds.Touch));
            Assert.IsFalse(s.Style.Accepts(FrameInputKinds.Pointer));
        }

        static FakeFrameElement Card(FakeFrameElement content, int i) => (FakeFrameElement)content.Children[i];

        // ---- --uf-visible-cards and whole cards ----

        /// <summary>What a layout group does after a resize: cards one after another with a gap.</summary>
        static void Reflow(FakeFrameElement content, float gap)
        {
            var x = 0f;
            foreach (FakeFrameElement c in content.Children)
            {
                var r = c.LayoutRectDp;
                c.LayoutRectDp = new FrameRect(x, r.Y, r.Width, r.Height);
                x += r.Width + gap;
            }
        }

        static FakeFrameElement GappedDeck(float viewport, int count, float card, float gap)
        {
            var root = new FakeFrameElement("deck", 0, 0, viewport, 200, FrameTags.CardDeck);
            var content = root.Add(new FakeFrameElement("cards", 0, 0, count * (card + gap), 200, FrameTags.Cards));
            for (var i = 0; i < count; i++) content.Add(new FakeFrameElement("card" + i, i * (card + gap), 0, card, 200));
            return root;
        }

        [Test]
        public void VisibleCardsSizesCardsToFillTheView()
        {
            var el = GappedDeck(500f, 8, 100f, 12f);
            var d = (DeckFrame)Create(el, StyleOf.With(FrameProperties.VisibleCards, 3));
            var content = (FakeFrameElement)d.Content;
            Assert.AreEqual((500f - 2 * 12f) / 3f, content.Children[0].LayoutRectDp.Width, 0.01f, "(view − 2 gaps) / 3");
            Reflow(content, 12f);
            d.OnLayoutChanged();
            Assert.AreEqual(3, d.Model.VisibleCount, "exactly three whole cards");
            d.OnLayoutChanged();
            Assert.AreEqual((500f - 24f) / 3f, content.Children[0].LayoutRectDp.Width, 0.01f, "stable: nothing more to do");
        }

        [Test]
        public void DeckThatCannotRestOnWholeCardsIsReported()
        {
            var warnings = new System.Collections.Generic.List<string>();
            _env.Warn = warnings.Add;
            Create(GappedDeck(250f, 6, 100f, 0f)); // 2.5 cards
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains("2.5 cards", warnings[0]);
            StringAssert.Contains("--uf-visible-cards", warnings[0]);
            warnings.Clear();
            Create(GappedDeck(312f, 6, 100f, 6f)); // 3 cards and 2 gaps
            CollectionAssert.IsEmpty(warnings);
        }

        // ---- Re-applying an unchanged style or layout leaves animations alone ----

        [Test]
        public void SameStyleAgainDoesNotInterruptADeckStep()
        {
            var style = StyleOf.With(FrameProperties.DeckStep, FrameDeckStep.Card);
            var d = (DeckFrame)Create(Cards(FrameTags.CardDeck, 6), style);
            d.Execute(FrameCommand.Next);
            _clock.AdvanceMs(60);
            d.Update();
            var mid = d.Model.OffsetDp;
            d.ApplyStyle(style.Clone()); // UI Toolkit re-resolves styles when a state class changes
            d.OnLayoutChanged();         // and a rescan or geometry event may follow
            Assert.IsTrue(d.Model.IsMoving);
            Assert.AreEqual(mid, d.Model.OffsetDp, 1e-3f);
            Run(d);
            Assert.AreEqual(100f, d.Model.OffsetDp, 1e-3f);
        }

        [Test]
        public void SameExtentDoesNotRestartTheDrawer()
        {
            var el = new FakeFrameElement("drawer", 0, 0, 280, 600, FrameTags.Drawer);
            var d = (DrawerFrame)Create(el);
            d.Execute(FrameCommand.Open);
            _clock.AdvanceMs(110);
            d.Update();
            var mid = d.Model.Progress;
            d.OnLayoutChanged();
            _clock.AdvanceMs(110);
            d.Update();
            Assert.AreEqual(1f, d.Model.Progress, 1e-4f, "finished on time");
            Assert.Greater(1f, mid);
        }

        [Test]
        public void ChangedStyleStillApplies()
        {
            var s = (StripFrame)Create(Cards(FrameTags.CardStrip, 10));
            s.ApplyStyle(StyleOf.With(FrameProperties.Snap, FrameSnap.Card));
            Assert.AreEqual(FrameSnap.Card, s.Model.Options.Snap);
        }
    }
}
