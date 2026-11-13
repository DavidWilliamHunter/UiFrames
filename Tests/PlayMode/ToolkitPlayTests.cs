using System.Collections;
using NUnit.Framework;
using Threads.UiFrames.Toolkit;
using Threads.UiFrames.Unity;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Threads.UiFrames.Tests
{
    /// <summary>
    /// The UI Toolkit adapter on a real runtime panel: custom properties read from USS (keywords,
    /// durations, quoted strings), layout measured, and mouse events dispatched through the panel,
    /// so pointer capture and Clickable behave as they do with live input.
    /// </summary>
    public class ToolkitPlayTests
    {
        const string Uxml = "Packages/com.threads.uiframes/Tests/PlayMode/TestAssets/TestFrames.uxml";
        const string Theme = "Packages/com.threads.uiframes/Tests/PlayMode/TestAssets/TestTheme.tss";

        GameObject _go;
        PanelSettings _settings;
        UIDocument _doc;
        FrameRegistry _registry;
        FrameEnvironment _env;
        FrameSet _set;
        ToolkitFrameBinder _binder;
        ToolkitPointerController _pointer;
        ToolkitFocusController _focus;
        FrameNavigator _nav;

        VisualElement Root => _doc.rootVisualElement;

        int _frameRate;
        readonly System.Collections.Generic.List<string> _warnings = new System.Collections.Generic.List<string>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // Editor-like frame times (batch mode otherwise runs ~1 ms frames).
            _frameRate = Application.targetFrameRate;
            Application.targetFrameRate = 60;
#if UNITY_EDITOR
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Uxml);
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(Theme);
#else
            VisualTreeAsset tree = null;
            ThemeStyleSheet theme = null;
#endif
            if (tree == null) Assert.Ignore("Test UXML is only loadable in the Editor.");
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            _settings.scale = 1f;
            _settings.themeStyleSheet = theme; // default controls need it for their size
            _go = new GameObject("Doc");
            _doc = _go.AddComponent<UIDocument>();
            _doc.panelSettings = _settings;
            _doc.visualTreeAsset = tree;
            yield return null;
            yield return null;

            _registry = FrameRegistry.WithBuiltIns();
            _warnings.Clear();
            _env = new FrameEnvironment(new UnityFrameClock()) { PixelsPerDp = 1f, Warn = _warnings.Add };
            _set = new FrameSet();
            _binder = new ToolkitFrameBinder(Root, _registry, _env, _set);
            _binder.Startup();
            _pointer = new ToolkitPointerController(_binder, _env);
            _pointer.Startup();
            _focus = new ToolkitFocusController(_binder);
            _focus.Startup();
            _nav = new FrameNavigator(_set, _focus);
            _nav.Startup();
            yield return null;
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            Application.targetFrameRate = _frameRate;
            _nav?.Shutdown();
            _focus?.Shutdown();
            _pointer?.Shutdown();
            _binder?.Shutdown();
            if (_go) Object.Destroy(_go);
            if (_settings) Object.Destroy(_settings);
        }

        T Frame<T>(string name) where T : class, IFrameBehaviour => _binder.FrameOf(Root.Q(name)) as T;

        IEnumerator Run(float seconds = 0.4f)
        {
            var end = Time.unscaledTime + seconds;
            while (Time.unscaledTime < end)
            {
                _binder.Update();
                _set.Update();
                _nav.Update();
                yield return null;
            }
        }

        // ---- Synthetic mouse through the panel ----

        void Send(EventType type, Vector2 panelPos, Vector2 delta = default)
        {
            var e = new Event { type = type, mousePosition = panelPos, button = 0, delta = delta, clickCount = 1 };
            EventBase evt;
            switch (type)
            {
                case EventType.MouseDown: evt = PointerDownEvent.GetPooled(e); break;
                case EventType.MouseUp: evt = PointerUpEvent.GetPooled(e); break;
                case EventType.ScrollWheel: evt = WheelEvent.GetPooled(e); break;
                default: evt = PointerMoveEvent.GetPooled(e); break;
            }
            using (evt) Root.panel.visualTree.SendEvent(evt);
        }

        /// <summary>Press, move in steps, then hold still (so the release is not a fling) and release.</summary>
        IEnumerator MouseDrag(Vector2 from, Vector2 by, int steps = 10, float holdSeconds = 0.1f)
        {
            Send(EventType.MouseDown, from);
            yield return null;
            for (var i = 1; i <= steps; i++)
            {
                Send(EventType.MouseDrag, from + by * ((float)i / steps));
                yield return null;
            }
            var end = Time.unscaledTime + holdSeconds;
            while (Time.unscaledTime < end) yield return null;
            Send(EventType.MouseUp, from + by);
            yield return null;
        }

        static Vector2 Centre(VisualElement ve) => ve.worldBound.center;

        // ---- Tests ----

        [Test]
        public void FramesAreBoundFromMarkerClasses()
        {
            Assert.IsInstanceOf<StripFrame>(_binder.FrameOf(Root.Q("strip")));
            Assert.IsInstanceOf<StripFrame>(_binder.FrameOf(Root.Q("vstrip")));
            Assert.IsInstanceOf<DrawerFrame>(_binder.FrameOf(Root.Q("drawer")));
            Assert.AreEqual(5, _set.Frames.Count);
        }

        [Test]
        public void CustomPropertiesAreReadFromUss()
        {
            var strip = Frame<StripFrame>("strip");
            Assert.AreEqual(FrameSnap.Card, strip.Style.Get(FrameProperties.Snap, FrameSnap.None), "keyword");
            Assert.AreEqual(0.1f, strip.Style.Get(FrameProperties.Duration, 0f), 1e-5f, "duration with unit");
            Assert.AreEqual(FrameEasing.Linear, strip.Style.Get(FrameProperties.Easing, FrameEasing.EaseOut));
            Assert.AreEqual(FrameAxis.Vertical, Frame<StripFrame>("vstrip").Axis);
            Assert.AreEqual(FrameEdge.Right, Frame<DrawerFrame>("drawer").Edge, "quoted string");
        }

        [Test]
        public void LayoutIsMeasured()
        {
            Assert.AreEqual(300f, Frame<StripFrame>("strip").Model.MaxOffsetDp, 0.5f);
            Assert.AreEqual(200f, Frame<StripFrame>("vstrip").Model.MaxOffsetDp, 0.5f);
            Assert.AreEqual(150f, Frame<DrawerFrame>("drawer").Model.ExtentDp, 0.5f);
        }

        [Test]
        public void ClosedRightDrawerIsTranslatedOff()
        {
            var t = Root.Q("drawer").resolvedStyle.translate;
            Assert.AreEqual(150f, t.x, 0.5f);
        }

        [UnityTest]
        public IEnumerator DragFromButtonMovesStripAndDoesNotClick()
        {
            var clicks = 0;
            var button = Root.Q<Button>("b1");
            button.clicked += () => clicks++;
            var strip = Frame<StripFrame>("strip");
            yield return MouseDrag(Centre(button), new Vector2(-120f, 0f));
            Assert.Greater(strip.Model.OffsetDp, 60f, "the strip followed the mouse");
            Assert.AreEqual(0, clicks, "the button lost the pointer to the frame");
            yield return Run();
            Assert.AreEqual(100f, strip.Model.OffsetDp, 0.5f, "snapped to a card");
        }

        [UnityTest]
        public IEnumerator ClickOnButtonStillClicks()
        {
            var clicks = 0;
            var button = Root.Q<Button>("b0");
            button.clicked += () => clicks++;
            Send(EventType.MouseDown, Centre(button));
            yield return null;
            Send(EventType.MouseUp, Centre(button));
            yield return null;
            Assert.AreEqual(1, clicks);
            Assert.AreEqual(0f, Frame<StripFrame>("strip").Model.OffsetDp);
            Assert.AreSame(Frame<StripFrame>("strip"), _set.Focused, "the press focused the frame");
        }

        [UnityTest]
        public IEnumerator SliderKeepsItsDrag()
        {
            var slider = Root.Q<Slider>("slider");
            var dragger = slider.Q(className: BaseSlider<float>.draggerUssClassName);
            var picked = Root.panel.Pick(Centre(dragger));
            Assert.IsTrue(slider.Contains(picked), $"pressing inside the slider (picked {picked?.name} {picked?.GetType().Name}, slider {slider.worldBound}, dragger {dragger.worldBound})");
            yield return MouseDrag(Centre(dragger), new Vector2(-40f, 0f));
            Assert.AreEqual(0f, Frame<StripFrame>("strip").Model.OffsetDp, "the strip did not move");
        }

        [UnityTest]
        public IEnumerator VerticalDragOnHorizontalStripIsLeftAlone()
        {
            var strip = Frame<StripFrame>("strip");
            yield return MouseDrag(Centre(Root.Q("strip")), new Vector2(0f, 60f));
            Assert.AreEqual(0f, strip.Model.OffsetDp);
        }

        [UnityTest]
        public IEnumerator VerticalStripDragsUpward()
        {
            var v = Frame<StripFrame>("vstrip");
            // Panel y is down: dragging up is negative y and reveals later rows.
            yield return MouseDrag(Centre(Root.Q("vstrip")) + new Vector2(0, 30), new Vector2(0f, -60f));
            Assert.Greater(v.Model.OffsetDp, 30f);
        }

        [UnityTest]
        public IEnumerator WheelScrollsStrip()
        {
            var strip = Frame<StripFrame>("strip");
            Send(EventType.ScrollWheel, Centre(Root.Q("strip")), new Vector2(0f, 3f));
            yield return Run();
            Assert.Greater(strip.Model.OffsetDp, 0f);
        }

        [UnityTest]
        public IEnumerator HandleClickTogglesDrawer()
        {
            var drawer = Frame<DrawerFrame>("drawer");
            var handle = Root.Q("handle");
            Send(EventType.MouseDown, Centre(handle));
            yield return null;
            Send(EventType.MouseUp, Centre(handle));
            yield return Run();
            Assert.IsTrue(drawer.IsOpen);
            Assert.IsTrue(Root.Q("drawer").ClassListContains(FrameTags.Open));
            Assert.AreEqual(0f, Root.Q("drawer").resolvedStyle.translate.x, 0.5f);
        }

        [UnityTest]
        public IEnumerator RescanPicksUpNewlyTaggedElements()
        {
            var extra = new VisualElement { name = "extra" };
            extra.style.position = Position.Absolute;
            extra.style.width = 50;
            extra.style.height = 50;
            extra.AddToClassList(FrameTags.CardDeck);
            Root.Add(extra);
            yield return null;
            _binder.Rescan();
            Assert.IsInstanceOf<DeckFrame>(_binder.FrameOf(extra));
            extra.RemoveFromClassList(FrameTags.CardDeck);
            _binder.Rescan();
            Assert.IsNull(_binder.FrameOf(extra));
            Assert.AreEqual(5, _set.Frames.Count);
        }

        // ---- Run-time UI and setup warnings ----

        [UnityTest]
        public IEnumerator UiAddedAtRunTimeIsFoundWithoutRescan()
        {
            var extra = new VisualElement { name = "late-deck" };
            extra.style.position = Position.Absolute;
            extra.style.width = 50;
            extra.style.height = 50;
            extra.AddToClassList(FrameTags.CardDeck);
            extra.Add(new VisualElement { name = "late-cards" });
            extra.Q("late-cards").AddToClassList(FrameTags.Cards);
            Root.Add(extra);
            Assert.IsNull(_binder.FrameOf(extra));
            yield return Run(0.7f);
            Assert.IsInstanceOf<DeckFrame>(_binder.FrameOf(extra), "found by the periodic scan");
        }

        [UnityTest]
        public IEnumerator FrameWithoutCardsIsReported()
        {
            var bad = new VisualElement { name = "no-cards" };
            bad.AddToClassList(FrameTags.CardStrip);
            Root.Add(bad);
            _binder.Rescan();
            yield return null;
            Assert.IsTrue(_warnings.Exists(w => w.Contains("'no-cards'") && w.Contains("uf-cards")), string.Join(" | ", _warnings));
            var count = _warnings.Count;
            _binder.Rescan();
            Assert.AreEqual(count, _warnings.Count, "reported once");
        }

        [UnityTest]
        public IEnumerator ControlsInNestedHiddenAreasStayUnfocusableUntilAllShow()
        {
            var drawer = Frame<DrawerFrame>("drawer");
            var item = Root.Q("d-item");
            Assert.IsFalse(item.focusable, "closed drawer: hidden");
            drawer.Element.SetVisible(false); // a second hidden area around it
            drawer.Execute(FrameCommand.Open);  // the drawer shows its contents again...
            yield return null;
            Assert.IsFalse(item.focusable, "...but it is still inside a hidden area");
            drawer.Element.SetVisible(true);
            Assert.IsTrue(item.focusable, "focusable once nothing hides it");
        }

        // ---- Axis classes ----

        [Test]
        public void AxisClassesAreSetFromTheProperty()
        {
            Assert.IsTrue(Root.Q("vstrip").ClassListContains(FrameTags.Vertical), "no class in the markup");
            Assert.IsTrue(Root.Q("strip").ClassListContains(FrameTags.Horizontal));
            Assert.AreEqual(FlexDirection.Column, Root.Q("vstrip").Q(className: FrameTags.Cards).resolvedStyle.flexDirection);
        }

        // ---- Keyboard / gamepad navigation in levels (the panel's own navigation events) ----

        void Navigate(NavigationMoveEvent.Direction direction)
        {
            using (var e = NavigationMoveEvent.GetPooled(direction)) Root.panel.visualTree.SendEvent(e);
        }

        void Submit()
        {
            using (var e = NavigationSubmitEvent.GetPooled()) Root.panel.visualTree.SendEvent(e);
        }

        void Cancel()
        {
            using (var e = NavigationCancelEvent.GetPooled()) Root.panel.visualTree.SendEvent(e);
        }

        VisualElement FocusedElement => Root.panel.focusController.focusedElement as VisualElement;
        VisualElement Selected => _nav.Selected?.Native as VisualElement;
        VisualElement StripCard(int i) => Root.Q("cards")[i];

        IEnumerator SelectStrip()
        {
            _nav.SelectElement(_binder.FrameOf(Root.Q("strip")).Element);
            yield return null;
        }

        [UnityTest]
        public IEnumerator EnterGoesFrameCardControlAndEscComesBack()
        {
            var clicks = 0;
            Root.Q<Button>("b1").clicked += () => clicks++;
            yield return SelectStrip();
            Assert.AreSame(Root.Q("strip"), FocusedElement, "the frame took focus");
            Assert.IsTrue(Root.Q("strip").ClassListContains(FrameTags.Selected));

            Submit();
            yield return null;
            Assert.AreSame(StripCard(0), Selected);
            Assert.AreSame(StripCard(0), FocusedElement);
            Assert.IsTrue(Root.Q("strip").ClassListContains(FrameTags.Entered));
            Assert.IsTrue(StripCard(0).ClassListContains(FrameTags.Selected));

            Navigate(NavigationMoveEvent.Direction.Right);
            yield return null;
            Assert.AreSame(StripCard(1), Selected);
            Submit(); // card 1 has one button: straight to it
            yield return null;
            Assert.AreSame(Root.Q("b1"), FocusedElement);
            Submit(); // the button's own Submit: it clicks
            yield return null;
            Assert.AreEqual(1, clicks);

            Cancel();
            yield return null;
            Assert.AreSame(StripCard(1), FocusedElement);
            Cancel();
            yield return null;
            Assert.AreSame(Root.Q("strip"), FocusedElement);
            Assert.AreEqual(0, _nav.Depth);
        }

        [UnityTest]
        public IEnumerator SliderTakesArrowsOnlyAfterEnter()
        {
            var slider = Root.Q<Slider>("slider");
            yield return SelectStrip();
            Submit();
            Navigate(NavigationMoveEvent.Direction.Right);
            Navigate(NavigationMoveEvent.Direction.Right);
            Submit(); // card 2: only the slider
            yield return null;
            Assert.AreSame(slider, Selected);
            Navigate(NavigationMoveEvent.Direction.Right);
            yield return null;
            Assert.AreEqual(5f, slider.value, "not adjusting yet: the arrow is the navigator's");

            Submit();
            yield return null;
            Assert.IsTrue(_nav.IsEditing);
            Assert.IsTrue(slider.ClassListContains(FrameTags.Editing));
            Navigate(NavigationMoveEvent.Direction.Right);
            yield return null;
            Assert.Greater(slider.value, 5f, "adjusting: the slider takes the arrow");
            Submit();
            yield return null;
            Assert.IsFalse(_nav.IsEditing);
        }

        [UnityTest]
        public IEnumerator TabStaysInTheLevelAndNeverReachesAClosedDrawer()
        {
            yield return SelectStrip();
            Assert.IsFalse(Root.Q("d-item").focusable, "a closed drawer's controls cannot take focus");
            var seen = new System.Collections.Generic.List<VisualElement>();
            for (var i = 0; i < 6; i++)
            {
                Navigate(NavigationMoveEvent.Direction.Next);
                yield return null;
                seen.Add(FocusedElement);
            }
            CollectionAssert.DoesNotContain(seen, Root.Q("d-item"));
            Assert.AreSame(Root.Q("strip"), seen[4], "wrapped round the five frames");
        }

        [UnityTest]
        public IEnumerator PlainCardsStepAndActivate()
        {
            IFrameElement activated = null;
            _nav.Activated += e => activated = e;
            var v = Frame<StripFrame>("vstrip");
            _nav.SelectElement(v.Element);
            Submit();
            for (var i = 0; i < 3; i++) Navigate(NavigationMoveEvent.Direction.Down);
            yield return Run();
            Assert.AreSame(v.Content.Children[3], _nav.Selected);
            Assert.AreEqual(100f, v.Model.OffsetDp, 0.5f, "scrolled to show row 3");
            Submit();
            Assert.AreSame(v.Content.Children[3], activated, "a row with nothing inside is activated");
        }

        [UnityTest]
        public IEnumerator FocusOnHiddenButtonEntersAndScrollsStripTheLeast()
        {
            var strip = Frame<StripFrame>("strip");
            Root.Q<Button>("b5").Focus();
            yield return Run();
            Assert.AreSame(strip, _set.Focused);
            Assert.AreEqual(2, _nav.Depth, "the frame and card around it were entered");
            Assert.AreEqual(300f, strip.Model.OffsetDp, 0.5f, "its whole card (500..600) in view");
        }

        [UnityTest]
        public IEnumerator ScrollingTheSelectedCardAwaySelectsOneInView()
        {
            var strip = Frame<StripFrame>("strip");
            Root.Q<Button>("b0").Focus();
            yield return null;
            strip.Execute(FrameCommand.End); // a drag or the wheel
            yield return Run();
            Assert.AreEqual(300f, strip.Model.OffsetDp, 0.5f);
            Assert.AreSame(StripCard(3), Selected, "first card in view");
            Assert.AreSame(StripCard(3), FocusedElement);
        }

        [UnityTest]
        public IEnumerator EnterOpensDrawerAndEscClosesIt()
        {
            var drawer = Frame<DrawerFrame>("drawer");
            _nav.SelectElement(drawer.Element);
            yield return null;
            Submit();
            yield return Run();
            Assert.IsTrue(drawer.IsOpen);
            Assert.AreSame(Root.Q("d-item"), FocusedElement, "focus moved into the drawer");
            Cancel();
            yield return Run();
            Assert.IsFalse(drawer.IsOpen);
            Assert.AreSame(Root.Q("drawer"), FocusedElement, "the drawer is selected again");
            Assert.IsFalse(Root.Q("d-item").focusable);
        }

        // ---- Transforms: the content under the pointer stays under it ----

        [UnityTest]
        public IEnumerator ScaledFrameFollowsPointerOneToOne()
        {
            var card = Root.Q("s-card");
            var before = card.worldBound.center;
            yield return MouseDrag(Centre(Root.Q("sstrip")), new Vector2(-60f, 0f));
            var moved = card.worldBound.center - before;
            Assert.AreEqual(-60f, moved.x, 1.5f);
            Assert.AreEqual(0f, moved.y, 1.5f);
            Assert.AreEqual(120f, Frame<StripFrame>("sstrip").Model.OffsetDp, 2f, "120 local at half scale");
        }

        [UnityTest]
        public IEnumerator RotatedFrameFollowsPointerOneToOne()
        {
            var card = Root.Q("r-card");
            var before = card.worldBound.center;
            // Rotated 90° clockwise: the strip's axis runs down the screen; drag up to scroll on.
            yield return MouseDrag(Centre(Root.Q("rstrip")), new Vector2(0f, -60f));
            var moved = card.worldBound.center - before;
            Assert.AreEqual(0f, moved.x, 1.5f);
            Assert.AreEqual(-60f, moved.y, 1.5f);
            Assert.Greater(Frame<StripFrame>("rstrip").Model.OffsetDp, 50f);
        }
    }
}
