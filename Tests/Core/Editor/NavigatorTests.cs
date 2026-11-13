using System.Collections.Generic;
using NUnit.Framework;
using Threads.UiFrames.Testing;

namespace Threads.UiFrames.Tests
{
    /// <summary>
    /// Keyboard/gamepad navigation in levels: frames → cards → controls, Enter in, Esc out.
    /// Scene (dp, y down):
    /// <code>
    ///  y 0    strip "photos": 6 cards of 100, view 300; card i has button i, card 2 also a slider above it
    ///  y 200  deck: 6 plain cards (nothing to focus)
    ///  y 400  button "outside" (not in a frame)
    ///  x 700  drawer (closed) with two items and a handle
    /// </code>
    /// </summary>
    public class NavigatorTests
    {
        ManualClock _clock;
        FrameRegistry _registry;
        FrameEnvironment _env;
        FrameSet _set;
        FakeFrameElement _root;
        FakeFocusHost _host;
        FrameNavigator _nav;

        StripFrame _strip;
        DeckFrame _deck;
        DrawerFrame _drawer;
        FakeFrameElement _outside, _slider, _drawerA, _drawerB;
        readonly List<FakeFrameElement> _buttons = new List<FakeFrameElement>();
        readonly List<IFrameElement> _activated = new List<IFrameElement>();

        [SetUp]
        public void SetUp()
        {
            _clock = new ManualClock();
            _registry = FrameRegistry.WithBuiltIns();
            _env = new FrameEnvironment(_clock);
            _set = new FrameSet();
            _root = new FakeFrameElement("root", 0, 0, 1000, 800);
            _buttons.Clear();
            _activated.Clear();

            var stripEl = new FakeFrameElement("photos", 0, 0, 300, 100, FrameTags.CardStrip);
            var cards = stripEl.Add(new FakeFrameElement("cards", 0, 0, 600, 100, FrameTags.Cards));
            for (var i = 0; i < 6; i++)
            {
                var card = cards.Add(new FakeFrameElement("card" + i, i * 100, 0, 100, 100));
                if (i == 2) _slider = card.Add(new FakeFrameElement("slider", 10, 10, 80, 20) { Focusable = true, Arrows = FrameControlArrows.Adjust });
                _buttons.Add(card.Add(new FakeFrameElement("button" + i, 10, 50, 80, 40) { Focusable = true }));
            }
            _strip = Add<StripFrame>(_root, stripEl);

            var deckEl = new FakeFrameElement("deck", 0, 200, 300, 100, FrameTags.CardDeck);
            var deckCards = deckEl.Add(new FakeFrameElement("deck-cards", 0, 0, 600, 100, FrameTags.Cards));
            for (var i = 0; i < 6; i++) deckCards.Add(new FakeFrameElement("deck" + i, i * 100, 0, 100, 100));
            _deck = Add<DeckFrame>(_root, deckEl);

            _outside = _root.Add(new FakeFrameElement("outside", 0, 400, 100, 40) { Focusable = true });

            var drawerEl = new FakeFrameElement("drawer", 700, 0, 280, 600, FrameTags.Drawer);
            var list = drawerEl.Add(new FakeFrameElement("list", 0, 0, 280, 400));
            _drawerA = list.Add(new FakeFrameElement("item-a", 0, 0, 280, 40) { Focusable = true });
            _drawerB = list.Add(new FakeFrameElement("item-b", 0, 50, 280, 40) { Focusable = true });
            drawerEl.Add(new FakeFrameElement("handle", -40, 300, 40, 60, FrameTags.DrawerHandle) { Focusable = true });
            _drawer = Add<DrawerFrame>(_root, drawerEl, StyleOf.With(FrameProperties.Edge, FrameEdge.Right));

            _host = new FakeFocusHost(_root);
            _nav = new FrameNavigator(_set, _host);
            _nav.Activated += _activated.Add;
            _nav.Startup();
        }

        [TearDown]
        public void TearDown() => _nav.Shutdown();

        T Add<T>(FakeFrameElement parent, FakeFrameElement el, FrameStyle style = null) where T : class, IFrameBehaviour
        {
            parent.Add(el);
            var b = _registry.Resolve(el).Create(new FrameContext(el, _env, _registry));
            if (style != null) b.ApplyStyle(style);
            _set.Add(b);
            return (T)b;
        }

        void Settle()
        {
            for (var i = 0; i < 60; i++) { _clock.AdvanceMs(16f); _set.Update(); _nav.Update(); }
        }

        IFrameElement Card(int i) => _strip.Content.Children[i];

        // ---- Frames level ----

        [Test]
        public void FirstMoveSelectsTheFirstFrame()
        {
            Assert.IsTrue(_nav.Move(FrameNavDirection.Down));
            Assert.AreSame(_strip.Element, _nav.Selected);
            Assert.AreSame(_strip.Element, _host.Focused, "the frame took UI focus");
            Assert.IsTrue(_strip.Element.HasClass(FrameTags.Selected));
            Assert.AreSame(_strip, _set.Focused);
        }

        [Test]
        public void ArrowsMoveBetweenFramesSpatially()
        {
            _nav.Move(FrameNavDirection.Down);
            _nav.Move(FrameNavDirection.Down);
            Assert.AreSame(_deck.Element, _nav.Selected);
            _nav.Move(FrameNavDirection.Down);
            Assert.AreSame(_outside, _nav.Selected, "controls outside frames are items too");
            _nav.Move(FrameNavDirection.Up);
            Assert.AreSame(_deck.Element, _nav.Selected);
            _nav.Move(FrameNavDirection.Right);
            Assert.AreSame(_drawer.Element, _nav.Selected);
            Assert.IsFalse(_strip.Element.HasClass(FrameTags.Selected), "one selection at a time");
        }

        [Test]
        public void TabCyclesItemsOfTheLevelAndNeverEntersAClosedDrawer()
        {
            var seen = new List<IFrameElement>();
            for (var i = 0; i < 5; i++) { _nav.Tab(1); seen.Add(_nav.Selected); }
            CollectionAssert.AreEqual(new IFrameElement[] { _strip.Element, _deck.Element, _outside, _drawer.Element, _strip.Element }, seen);
            CollectionAssert.DoesNotContain(seen, _drawerA);
            _nav.Tab(-1);
            Assert.AreSame(_drawer.Element, _nav.Selected);
        }

        // ---- Into a frame, a card, a control, and back ----

        [Test]
        public void EnterGoesFrameCardControlAndEscComesBack()
        {
            _nav.Move(FrameNavDirection.Down);
            Assert.IsTrue(_nav.Submit());
            Assert.AreEqual(1, _nav.Depth);
            Assert.AreSame(Card(0), _nav.Selected);
            Assert.IsTrue(_strip.Element.HasClass(FrameTags.Entered));

            Assert.IsTrue(_nav.Submit(), "card 0 has one button: straight to it");
            Assert.AreEqual(2, _nav.Depth);
            Assert.AreSame(_buttons[0], _nav.Selected);
            Assert.AreSame(_buttons[0], _host.Focused);
            Assert.IsFalse(_nav.Submit(), "Enter on a button is the button's: it presses");

            Assert.IsTrue(_nav.Cancel());
            Assert.AreEqual(1, _nav.Depth);
            Assert.AreSame(Card(0), _nav.Selected);
            Assert.IsTrue(_nav.Cancel());
            Assert.AreEqual(0, _nav.Depth);
            Assert.AreSame(_strip.Element, _nav.Selected);
            Assert.IsFalse(_strip.Element.HasClass(FrameTags.Entered));
            Assert.IsFalse(_nav.Cancel(), "nothing to leave at the top");
        }

        [Test]
        public void CardsFollowTheAxisAndTheStripScrolls()
        {
            _nav.Move(FrameNavDirection.Down);
            _nav.Submit();
            for (var i = 0; i < 3; i++) _nav.Move(FrameNavDirection.Right);
            Assert.AreSame(Card(3), _nav.Selected);
            Settle();
            Assert.IsTrue(_strip.IsShown(Card(3)), "scrolled to show the selected card");
            Assert.IsTrue(_nav.Move(FrameNavDirection.Up), "across the axis: used, nothing moves");
            Assert.AreSame(Card(3), _nav.Selected);
            for (var i = 0; i < 5; i++) _nav.Move(FrameNavDirection.Right);
            Assert.AreSame(Card(5), _nav.Selected, "stops at the end");
        }

        [Test]
        public void RememberedCardOnReEntry()
        {
            _nav.Move(FrameNavDirection.Down);
            _nav.Submit();
            _nav.Move(FrameNavDirection.Right);
            _nav.Cancel();
            _nav.Submit();
            Assert.AreSame(Card(1), _nav.Selected);
        }

        [Test]
        public void ControlsInACardAndSliderNeedsEnter()
        {
            _nav.Move(FrameNavDirection.Down);
            _nav.Submit();
            _nav.Move(FrameNavDirection.Right);
            _nav.Move(FrameNavDirection.Right);
            _nav.Submit(); // card 2: slider and button
            Assert.AreSame(_slider, _nav.Selected);
            Assert.IsTrue(_nav.Move(FrameNavDirection.Down));
            Assert.AreSame(_buttons[2], _nav.Selected, "arrows move between controls, not the slider's value");
            _nav.Move(FrameNavDirection.Up);
            Assert.AreSame(_slider, _nav.Selected);

            Assert.IsTrue(_nav.Submit(), "Enter starts adjusting");
            Assert.IsTrue(_nav.IsEditing);
            Assert.IsTrue(_host.Editing.Contains(_slider));
            Assert.IsTrue(_slider.HasClass(FrameTags.Editing));
            Assert.IsFalse(_nav.Move(FrameNavDirection.Right), "while adjusting, arrows are the slider's");
            Assert.IsTrue(_nav.Submit(), "Enter ends adjusting");
            Assert.IsFalse(_nav.IsEditing);
            _nav.Submit();
            Assert.IsTrue(_nav.Cancel(), "Esc also ends adjusting");
            Assert.IsFalse(_nav.IsEditing);
            Assert.AreEqual(2, _nav.Depth, "without leaving the card");
        }

        [Test]
        public void CardWithNothingInsideIsActivated()
        {
            _nav.Tab(1);
            _nav.Tab(1); // deck
            _nav.Submit();
            Assert.AreSame(_deck.Content.Children[0], _nav.Selected);
            _nav.Submit();
            CollectionAssert.AreEqual(new[] { _deck.Content.Children[0] }, _activated);
            Assert.AreEqual(1, _nav.Depth);
        }

        [Test]
        public void SingleCardFrameSkipsTheCardLevel()
        {
            var el = new FakeFrameElement("single", 0, 600, 300, 100, FrameTags.CardStrip);
            var content = el.Add(new FakeFrameElement("c", 0, 0, 100, 100, FrameTags.Cards));
            var card = content.Add(new FakeFrameElement("only", 0, 0, 100, 100));
            var a = card.Add(new FakeFrameElement("a", 0, 0, 100, 40) { Focusable = true });
            card.Add(new FakeFrameElement("b", 0, 50, 100, 40) { Focusable = true });
            var single = Add<StripFrame>(_root, el);
            _nav.SelectElement(single.Element);
            _nav.Submit();
            Assert.AreEqual(2, _nav.Depth, "the only card was entered too");
            Assert.AreSame(a, _nav.Selected);
            _nav.Cancel();
            Assert.AreEqual(0, _nav.Depth, "Esc goes back past the skipped level");
            Assert.AreSame(single.Element, _nav.Selected);
        }

        // ---- Drawer ----

        [Test]
        public void EnterOpensTheDrawerAndEscClosesIt()
        {
            _nav.SelectElement(_drawer.Element);
            Assert.IsTrue(_nav.Submit());
            Assert.IsTrue(_drawer.IsOpen);
            Assert.AreEqual(1, _nav.Depth);
            Assert.AreSame(_drawerA, _nav.Selected);
            Assert.AreSame(_drawerA, _host.Focused);
            _nav.Move(FrameNavDirection.Down);
            Assert.AreSame(_drawerB, _nav.Selected);
            _nav.Cancel();
            Assert.IsFalse(_drawer.IsOpen);
            Assert.AreSame(_drawer.Element, _nav.Selected);
            Settle();
            Assert.IsFalse(_host.CanFocus(_drawerA), "closed: its items cannot take focus");
        }

        [Test]
        public void DrawerClosedByOtherMeansLeavesItsLevel()
        {
            _nav.SelectElement(_drawer.Element);
            _nav.Submit();
            _drawer.Execute(FrameCommand.Close); // e.g. the mouse
            Settle();
            Assert.AreEqual(0, _nav.Depth);
            Assert.AreSame(_drawer.Element, _nav.Selected);
        }

        // ---- Following the mouse ----

        [Test]
        public void ScrollingTheSelectedCardAwaySelectsOneInView()
        {
            _nav.Move(FrameNavDirection.Down);
            _nav.Submit();
            _nav.Move(FrameNavDirection.Right);
            _nav.Submit(); // into card 1, button 1
            _strip.Execute(FrameCommand.End); // a drag or the wheel
            Settle();
            Assert.AreEqual(1, _nav.Depth, "left the card that went out of view");
            Assert.AreSame(Card(3), _nav.Selected, "first card in view");
        }

        [Test]
        public void ClickingAControlEntersItsLevels()
        {
            _host.MoveFocusTo(_buttons[4]);
            Assert.AreEqual(2, _nav.Depth);
            Assert.AreSame(_buttons[4], _nav.Selected);
            Assert.IsTrue(_strip.Element.HasClass(FrameTags.Entered));
            Assert.IsTrue(Card(4).HasClass(FrameTags.Entered));
            Settle();
            Assert.IsTrue(_strip.IsShown(_buttons[4]));
            _nav.Cancel();
            Assert.AreSame(Card(4), _nav.Selected);
        }

        [Test]
        public void PressingAFrameSelectsIt()
        {
            _set.Focus(_deck); // what a pointer press does
            Assert.AreSame(_deck.Element, _nav.Selected);
            Assert.AreEqual(0, _nav.Depth);
        }

        [Test]
        public void PressingInsideTheEnteredFrameKeepsTheLevels()
        {
            _nav.Move(FrameNavDirection.Down);
            _nav.Submit();
            _set.Focus(_strip);
            Assert.AreEqual(1, _nav.Depth);
        }

        [Test]
        public void CycleFrameGoesToTheNextFrameFromAnyLevel()
        {
            _host.MoveFocusTo(_buttons[1]);
            _nav.CycleFrame(1);
            Assert.AreEqual(0, _nav.Depth);
            Assert.AreSame(_deck.Element, _nav.Selected);
        }

        [Test]
        public void FlatFrameIsTransparent()
        {
            _strip.ApplyStyle(StyleOf.With(FrameProperties.Navigation, FrameNavigationMode.Flat));
            _nav.Tab(1);
            Assert.AreSame(_buttons[0], _nav.Selected, "its controls are items of the top level");
        }
    }
}
