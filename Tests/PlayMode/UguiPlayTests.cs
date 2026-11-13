using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Threads.UiFrames.Input;
using Threads.UiFrames.UGUI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Threads.UiFrames.Tests
{
    /// <summary>
    /// The uGUI adapter and the runner with a simulated mouse and keyboard through the real
    /// InputSystemUIInputModule: class lists, style sheet plus inline style, drags from buttons,
    /// slider and ScrollRect hand-off, wheel, handle clicks and keyboard navigation.
    /// </summary>
    public class UguiPlayTests : InputTestFixture
    {
        readonly List<Object> _created = new List<Object>();
        Mouse _mouse;
        Keyboard _keyboard;
        UiFramesRunner _runner;
        RectTransform _canvas;
        UiFrameStyleSheet _sheet;
        int _clicks;

        int _frameRate;

        public override void Setup()
        {
            base.Setup();
            // Run at Editor-like frame times: batch mode otherwise runs ~1 ms frames, which hides
            // anything that depends on how far an animation gets per frame.
            _frameRate = Application.targetFrameRate;
            Application.targetFrameRate = 60;
        }

        public override void TearDown()
        {
            Application.targetFrameRate = _frameRate;
            foreach (var o in _created) if (o) Object.Destroy(o);
            _created.Clear();
            base.TearDown();
        }

        T Keep<T>(T o) where T : Object
        {
            _created.Add(o);
            return o;
        }

        // ---- Scene ----

        RectTransform _strip, _button, _slider, _drawer, _handle, _scroll, _plain, _drawerButton;
        readonly RectTransform[] _buttons = new RectTransform[6];
        EventSystem _es;
        Camera _camera;

        enum Space { Overlay, Scaled, Rotated, World }

        IEnumerator Build(bool inScrollRect = false, Space space = Space.Overlay, bool plain = false)
        {
            _mouse = InputSystem.AddDevice<Mouse>();
            _keyboard = InputSystem.AddDevice<Keyboard>();
            var es = Keep(new GameObject("EventSystem", typeof(EventSystem)));
            es.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
            _es = es.GetComponent<EventSystem>();

            var canvasGo = Keep(new GameObject("Canvas", typeof(Canvas), typeof(GraphicRaycaster)));
            var canvas = canvasGo.GetComponent<Canvas>();
            _canvas = (RectTransform)canvasGo.transform;
            _camera = null;
            if (space == Space.World)
            {
                _camera = Keep(new GameObject("Camera", typeof(Camera))).GetComponent<Camera>();
                _camera.transform.position = new Vector3(0, 0, -10);
                _camera.fieldOfView = 60;
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.worldCamera = _camera;
                _canvas.sizeDelta = new Vector2(1000, 800);
                _canvas.position = Vector3.zero;
                _canvas.localScale = Vector3.one * 0.01f;
            }
            else canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            Transform stripParent = _canvas;
            if (space == Space.Scaled || space == Space.Rotated)
            {
                var panel = Rect("Panel", _canvas, Vector2.zero, new Vector2(400, 400));
                if (space == Space.Scaled) panel.localScale = Vector3.one * 0.5f;
                else panel.localRotation = Quaternion.Euler(0, 0, 90); // counter-clockwise: the strip's axis runs up the screen
                stripParent = panel;
            }
            if (inScrollRect)
            {
                _scroll = Rect("Scroll", _canvas, new Vector2(0, 0), new Vector2(320, 300), typeof(Image), typeof(ScrollRect), typeof(RectMask2D));
                var content = Rect("Content", _scroll, Vector2.zero, new Vector2(320, 1000));
                content.anchorMin = content.anchorMax = content.pivot = new Vector2(0.5f, 1f);
                content.anchoredPosition = Vector2.zero;
                var sr = _scroll.GetComponent<ScrollRect>();
                sr.content = content;
                sr.viewport = _scroll;
                sr.horizontal = false;
                sr.inertia = false;
                sr.movementType = ScrollRect.MovementType.Clamped;
                stripParent = content;
            }

            // A 300 × 100 strip of six 100 × 100 cards; card 1 has a button, card 2 a slider.
            _strip = Rect("Strip", stripParent, inScrollRect ? new Vector2(0, -150) : new Vector2(0, 100), new Vector2(300, 100), typeof(Image), typeof(RectMask2D));
            if (inScrollRect) _strip.anchorMin = _strip.anchorMax = new Vector2(0.5f, 1f); // 150 below the content's top: in view
            _strip.GetComponent<Image>().color = new Color(1, 1, 1, 0.05f);
            Classes(_strip, "uf-card-strip", "photos").SetInline("--uf-duration", "100ms");
            var cards = Rect("Cards", _strip, Vector2.zero, new Vector2(600, 100));
            cards.anchorMin = cards.anchorMax = cards.pivot = new Vector2(0, 1);
            cards.anchoredPosition = Vector2.zero;
            Classes(cards, "uf-cards");
            for (var i = 0; i < 6; i++)
            {
                var card = Rect($"Card {i}", cards, Vector2.zero, new Vector2(100, 100), typeof(Image));
                card.anchorMin = card.anchorMax = card.pivot = new Vector2(0, 1);
                card.anchoredPosition = new Vector2(i * 100, 0);
                if (i == 2) { _slider = MakeSlider(card); continue; }
                if (i == 0) continue;
                _buttons[i] = Rect($"Button {i}", card, Vector2.zero, new Vector2(80, 40), typeof(Image), typeof(Button));
                if (i == 1)
                {
                    _button = _buttons[i];
                    _button.GetComponent<Button>().onClick.AddListener(() => _clicks++);
                }
            }

            if (plain)
            {
                // A strip of plain picture cards: nothing selectable inside, so the frame is.
                _plain = Rect("Plain", _canvas, new Vector2(0, -100), new Vector2(300, 100), typeof(Image), typeof(RectMask2D));
                Classes(_plain, "uf-card-strip");
                var pc = Rect("Cards", _plain, Vector2.zero, new Vector2(600, 100));
                pc.anchorMin = pc.anchorMax = pc.pivot = new Vector2(0, 1);
                pc.anchoredPosition = Vector2.zero;
                Classes(pc, "uf-cards");
                for (var i = 0; i < 6; i++)
                {
                    var card = Rect($"Picture {i}", pc, Vector2.zero, new Vector2(100, 100), typeof(Image));
                    card.anchorMin = card.anchorMax = card.pivot = new Vector2(0, 1);
                    card.anchoredPosition = new Vector2(i * 100, 0);
                }
            }

            // A right-edge drawer with its handle sticking out on the left.
            _drawer = Rect("Drawer", _canvas, Vector2.zero, new Vector2(150, 0), typeof(Image));
            _drawer.anchorMin = new Vector2(1, 0);
            _drawer.anchorMax = new Vector2(1, 1);
            _drawer.pivot = new Vector2(1, 0.5f);
            _drawer.anchoredPosition = Vector2.zero;
            Classes(_drawer, "uf-drawer");
            _handle = Rect("Handle", _drawer, Vector2.zero, new Vector2(30, 40), typeof(Image));
            _handle.anchorMin = _handle.anchorMax = new Vector2(0, 0.5f);
            _handle.pivot = new Vector2(1, 0.5f);
            _handle.anchoredPosition = Vector2.zero;
            Classes(_handle, "uf-drawer-handle");
            _drawerButton = Rect("Drawer Button", _drawer, new Vector2(0, 50), new Vector2(120, 30), typeof(Image), typeof(Button));

            _sheet = Keep(ScriptableObject.CreateInstance<UiFrameStyleSheet>());
            _sheet.AddRule(".photos", new UiFrameDeclaration("--uf-snap", "card"), new UiFrameDeclaration("--uf-duration", "300ms"));
            _sheet.AddRule(".uf-drawer", new UiFrameDeclaration("--uf-edge", "auto"));

            var runnerGo = Keep(new GameObject("Runner"));
            runnerGo.SetActive(false);
            _runner = runnerGo.AddComponent<UiFramesRunner>();
            _runner.uguiRoots.Add(_canvas);
            _runner.uguiStyleSheets.Add(_sheet);
            runnerGo.SetActive(true);
            _runner.Environment.PixelsPerDp = 1f; // canvas units = px = dp in these tests
            _clicks = 0;
            yield return null;
            yield return null;
        }

        static RectTransform Rect(string name, Transform parent, Vector2 pos, Vector2 size, params System.Type[] components)
        {
            var go = new GameObject(name, typeof(RectTransform));
            foreach (var c in components) go.AddComponent(c);
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            return rt;
        }

        static UiFrameClasses Classes(RectTransform rt, params string[] classes)
        {
            var c = rt.gameObject.AddComponent<UiFrameClasses>();
            c.SetClasses(classes);
            return c;
        }

        static RectTransform MakeSlider(RectTransform card)
        {
            var slider = Rect("Slider", card, Vector2.zero, new Vector2(90, 30), typeof(Image), typeof(Slider)).GetComponent<Slider>();
            var area = Rect("Area", slider.transform, Vector2.zero, Vector2.zero);
            area.anchorMin = Vector2.zero;
            area.anchorMax = Vector2.one;
            var handle = Rect("Handle", area, Vector2.zero, new Vector2(20, 30), typeof(Image));
            slider.handleRect = handle;
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.SetValueWithoutNotify(0.5f);
            return (RectTransform)slider.transform;
        }

        Vector2 Centre(Component c)
        {
            Canvas.ForceUpdateCanvases();
            return RectTransformUtility.WorldToScreenPoint(_camera, c.transform.position);
        }

        IFrameBehaviour FrameOf(Component c) => _runner.Frames.Find(c.gameObject);

        IEnumerator Run(float seconds = 0.5f)
        {
            var end = Time.unscaledTime + seconds;
            while (Time.unscaledTime < end) yield return null;
        }

        // ---- Mouse ----

        IEnumerator Drag(Vector2 from, Vector2 by, int steps = 10)
        {
            Set(_mouse.position, from);
            yield return null;
            Press(_mouse.leftButton);
            yield return null;
            for (var i = 1; i <= steps; i++)
            {
                Set(_mouse.position, from + by * ((float)i / steps));
                yield return null;
            }
            // Hold still so the release is not a fling (batch frames are only ~1 ms apart).
            var end = Time.unscaledTime + 0.1f;
            while (Time.unscaledTime < end) yield return null;
            Release(_mouse.leftButton);
            yield return null;
            yield return null;
        }

        IEnumerator Click(Vector2 at)
        {
            Set(_mouse.position, at);
            yield return null;
            Press(_mouse.leftButton);
            yield return null;
            Release(_mouse.leftButton);
            yield return null;
            yield return null;
        }

        // ---- Tests ----

        [UnityTest]
        public IEnumerator FrameAddedAtRunTimeIsFoundWithoutRescan()
        {
            yield return Build();
            var late = Rect("Late Deck", _canvas, new Vector2(0, -250), new Vector2(200, 100), typeof(Image), typeof(RectMask2D));
            var cards = Rect("Cards", late, Vector2.zero, new Vector2(400, 100));
            Classes(cards, "uf-cards");
            Classes(late, "uf-card-deck");
            Assert.IsNull(FrameOf(late));
            yield return Settle(0.7f);
            Assert.IsInstanceOf<DeckFrame>(FrameOf(late), "found by the periodic scan");
        }

        [UnityTest]
        public IEnumerator BrokenSetupIsReported()
        {
            yield return Build();
            // In the order they are checked.
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("'Bad Strip' .*has no child with class uf-cards"));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("'Bad Strip' has no RectMask2D"));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("'Bad Strip' has no raycast target"));
            var bad = Rect("Bad Strip", _canvas, new Vector2(0, -250), new Vector2(200, 100));
            Classes(bad, "uf-card-strip");
            _runner.Rescan();
            yield return null;
        }

        [UnityTest]
        public IEnumerator ClassesStyleSheetAndInlineStyle()
        {
            yield return Build();
            var strip = (StripFrame)FrameOf(_strip);
            Assert.IsNotNull(strip);
            Assert.AreEqual(FrameSnap.Card, strip.Style.Get(FrameProperties.Snap, FrameSnap.None), "from the sheet");
            Assert.AreEqual(0.1f, strip.Style.Get(FrameProperties.Duration, 0f), 1e-5f, "inline wins over the sheet");
            Assert.AreEqual(300f, strip.Model.MaxOffsetDp, 0.5f, "measured from the cards");
            var drawer = (DrawerFrame)FrameOf(_drawer);
            Assert.AreEqual(FrameEdge.Right, drawer.Edge, "auto: the handle sticks out on the left");
        }

        [UnityTest]
        public IEnumerator DragFromButtonMovesStripAndDoesNotClick()
        {
            yield return Build();
            var strip = (StripFrame)FrameOf(_strip);
            yield return Drag(Centre(_button), new Vector2(-120, 0));
            Assert.Greater(strip.Model.OffsetDp, 60f);
            Assert.AreEqual(0, _clicks);
            yield return Run();
            Assert.AreEqual(100f, strip.Model.OffsetDp, 0.5f, "snapped to a card");
            Assert.AreEqual(-100f, ((RectTransform)_strip.Find("Cards")).anchoredPosition.x, 0.5f);
        }

        [UnityTest]
        public IEnumerator ClickOnButtonClicksAndFocusesNothingElse()
        {
            yield return Build();
            yield return Click(Centre(_button));
            Assert.AreEqual(1, _clicks);
            Assert.AreEqual(0f, ((StripFrame)FrameOf(_strip)).Model.OffsetDp);
        }

        [UnityTest]
        public IEnumerator SliderKeepsItsDrag()
        {
            yield return Build();
            var slider = _slider.GetComponent<Slider>();
            yield return Drag(Centre(slider.handleRect), new Vector2(-30, 0));
            Assert.Less(slider.value, 0.5f);
            Assert.AreEqual(0f, ((StripFrame)FrameOf(_strip)).Model.OffsetDp);
        }

        [UnityTest]
        public IEnumerator CrossDragIsHandedToOuterScrollRect()
        {
            yield return Build(inScrollRect: true);
            var content = _scroll.GetComponent<ScrollRect>().content;
            var before = content.anchoredPosition.y;
            yield return Drag(Centre(_strip), new Vector2(0, 80));
            Assert.Greater(content.anchoredPosition.y, before + 30f, "the ScrollRect took the vertical drag");
            Assert.AreEqual(0f, ((StripFrame)FrameOf(_strip)).Model.OffsetDp);
        }

        [UnityTest]
        public IEnumerator WheelScrollsStrip()
        {
            yield return Build();
            Set(_mouse.position, Centre(_strip));
            yield return null;
            Set(_mouse.scroll, new Vector2(0, -120f)); // wheel down
            yield return null;
            Set(_mouse.scroll, Vector2.zero);
            yield return Run();
            Assert.Greater(((StripFrame)FrameOf(_strip)).Model.OffsetDp, 0f);
        }

        [UnityTest]
        public IEnumerator HandleClickTogglesDrawer()
        {
            yield return Build();
            var drawer = (DrawerFrame)FrameOf(_drawer);
            Assert.AreEqual(150f, _drawer.anchoredPosition.x, 0.5f, "closed: moved off to the right");
            yield return Click(Centre(_handle));
            yield return Run();
            Assert.IsTrue(drawer.IsOpen);
            Assert.IsTrue(_drawer.GetComponent<UiFrameClasses>().HasClass(FrameTags.Open));
            Assert.AreEqual(0f, _drawer.anchoredPosition.x, 0.5f);
        }

        // ---- Focus and navigation: frames follow the EventSystem's selection ----

        /// <summary>Holds a key for one frame: the UI module reads Navigate's value each frame, so a
        /// press and release in the same update would never be seen.</summary>
        IEnumerator Tap(UnityEngine.InputSystem.Controls.KeyControl key)
        {
            Press(key);
            yield return null;
            Release(key);
            yield return null;
        }

        IEnumerator Settle(float seconds = 0.6f)
        {
            var end = Time.unscaledTime + seconds;
            while (Time.unscaledTime < end) yield return null;
        }

        FrameNavigator Nav => _runner.Navigators[0];
        GameObject Selected => _es.currentSelectedGameObject;
        GameObject StripCard(int i) => _strip.Find("Cards").GetChild(i).gameObject;

        IEnumerator SelectStrip()
        {
            Nav.SelectElement(FrameOf(_strip).Element);
            yield return null;
        }

        [UnityTest]
        public IEnumerator SelectingAHiddenButtonEntersAndRevealsIt()
        {
            yield return Build();
            var strip = (StripFrame)FrameOf(_strip);
            _es.SetSelectedGameObject(_buttons[3].gameObject); // card 3: 300..400, out of view
            yield return Settle();
            Assert.AreSame(strip, _runner.Frames.Focused);
            Assert.AreEqual(2, Nav.Depth);
            Assert.AreEqual(100f, strip.Model.OffsetDp, 0.5f, "the least scroll that shows its whole card (300..400)");
        }

        [UnityTest]
        public IEnumerator EnterGoesFrameCardControlAndEscComesBack()
        {
            yield return Build();
            yield return SelectStrip();
            Assert.AreSame(_strip.gameObject, Selected, "the frame can be selected");
            Assert.IsTrue(_strip.GetComponent<UiFrameClasses>().HasClass(FrameTags.Selected));

            yield return Tap(_keyboard.enterKey);
            Assert.AreSame(StripCard(0), Selected);
            Assert.IsTrue(_strip.GetComponent<UiFrameClasses>().HasClass(FrameTags.Entered));
            yield return Tap(_keyboard.rightArrowKey);
            Assert.AreSame(StripCard(1), Selected);
            yield return Tap(_keyboard.enterKey); // card 1 has one button: straight to it
            Assert.AreSame(_button.gameObject, Selected);
            yield return Tap(_keyboard.enterKey); // the button's own Submit
            Assert.AreEqual(1, _clicks);

            yield return Tap(_keyboard.escapeKey);
            Assert.AreSame(StripCard(1), Selected);
            yield return Tap(_keyboard.escapeKey);
            Assert.AreSame(_strip.gameObject, Selected);
            Assert.AreEqual(0, Nav.Depth);
        }

        [UnityTest]
        public IEnumerator SliderTakesArrowsOnlyAfterEnter()
        {
            yield return Build();
            var slider = _slider.GetComponent<Slider>();
            yield return SelectStrip();
            yield return Tap(_keyboard.enterKey);
            yield return Tap(_keyboard.rightArrowKey);
            yield return Tap(_keyboard.rightArrowKey);
            yield return Tap(_keyboard.enterKey); // card 2: only the slider
            Assert.AreSame(_slider.gameObject, Selected);
            yield return Tap(_keyboard.rightArrowKey);
            Assert.AreEqual(0.5f, slider.value, 1e-4f, "not adjusting yet");
            yield return Tap(_keyboard.enterKey);
            Assert.IsTrue(Nav.IsEditing);
            yield return Tap(_keyboard.rightArrowKey);
            Assert.Greater(slider.value, 0.5f, "adjusting: the slider takes the arrow");
            yield return Tap(_keyboard.enterKey);
            Assert.IsFalse(Nav.IsEditing);
            yield return Tap(_keyboard.escapeKey);
            Assert.AreSame(StripCard(2), Selected);
        }

        [UnityTest]
        public IEnumerator TabStaysInTheLevelAndNeverReachesAClosedDrawer()
        {
            yield return Build();
            Assert.IsFalse(_drawerButton.GetComponent<Button>().IsInteractable(), "closed: its contents cannot be selected");
            var seen = new List<GameObject>();
            for (var i = 0; i < 4; i++)
            {
                yield return Tap(_keyboard.tabKey);
                seen.Add(Selected);
            }
            CollectionAssert.AreEqual(new[] { _strip.gameObject, _drawer.gameObject, _strip.gameObject, _drawer.gameObject }, seen);
        }

        [UnityTest]
        public IEnumerator EndKeyAndMouseMoveTheCardSelection()
        {
            yield return Build();
            yield return SelectStrip();
            yield return Tap(_keyboard.enterKey);
            Assert.AreSame(StripCard(0), Selected);
            yield return Tap(_keyboard.endKey);
            yield return Settle();
            Assert.AreSame(StripCard(3), Selected, "End scrolled card 0 away: first card in view");

            yield return Drag(Centre(_strip) + new Vector2(0, 40), new Vector2(250, 0)); // back towards the start
            yield return Settle();
            var strip = (StripFrame)FrameOf(_strip);
            Assert.AreEqual(1, Nav.Depth);
            Assert.IsTrue(strip.IsShown(Nav.Selected), "the selected card is in view after the drag");
            Assert.AreSame(Nav.Selected.Native, Selected);
        }

        [UnityTest]
        public IEnumerator EnterOpensDrawerAndEscClosesIt()
        {
            yield return Build();
            var drawer = (DrawerFrame)FrameOf(_drawer);
            Nav.SelectElement(drawer.Element);
            yield return null;
            yield return Tap(_keyboard.enterKey);
            yield return Settle();
            Assert.IsTrue(drawer.IsOpen);
            Assert.AreSame(_drawerButton.gameObject, Selected, "selection moved into the drawer");
            yield return Tap(_keyboard.escapeKey);
            yield return Settle();
            Assert.IsFalse(drawer.IsOpen);
            Assert.AreSame(_drawer.gameObject, Selected, "the drawer is selected again");
        }

        [UnityTest]
        public IEnumerator SelectionGetsTheDefaultLook()
        {
            yield return Build();
            yield return SelectStrip();
            var visual = _strip.GetComponent<UiFrameStateVisual>();
            Assert.IsTrue(visual, "added when the keyboard first selected it");
            var outline = _strip.Find("UF uf-selected");
            Assert.IsTrue(outline && outline.gameObject.activeSelf, "selected outline shown");
            yield return Tap(_keyboard.enterKey);
            Assert.IsFalse(outline.gameObject.activeSelf, "the frame is now entered, not selected");
            Assert.IsTrue(_strip.Find("UF uf-entered").gameObject.activeSelf);
            Assert.IsTrue(StripCard(0).transform.Find("UF uf-selected").gameObject.activeSelf, "the card shows the selection");
        }

        [UnityTest]
        public IEnumerator RunnerEventsForDesigners()
        {
            yield return Build();
            var activated = new List<string>();
            var opened = new List<string>();
            var selected = new List<string>();
            _runner.onActivated.AddListener(activated.Add);
            _runner.onDrawerOpened.AddListener(opened.Add);
            _runner.onSelectionChanged.AddListener(selected.Add);
            yield return SelectStrip();
            yield return Tap(_keyboard.enterKey);  // card 0: nothing inside
            yield return Tap(_keyboard.enterKey);
            CollectionAssert.AreEqual(new[] { "Card 0" }, activated);
            CollectionAssert.Contains(selected, "Strip");
            ((DrawerFrame)FrameOf(_drawer)).Execute(FrameCommand.Open);
            CollectionAssert.AreEqual(new[] { "Drawer" }, opened);
        }

        [UnityTest]
        public IEnumerator F6SelectsTheNextFrame()
        {
            yield return Build();
            yield return Tap(_keyboard.f6Key);
            Assert.AreSame(_strip.gameObject, Selected);
            yield return Tap(_keyboard.f6Key);
            Assert.AreSame(_drawer.gameObject, Selected);
        }

        // ---- Transforms: the card under the pointer stays under it ----

        /// <summary>
        /// Drags by <paramref name="drag"/> and checks, while the pointer is still held, that a card
        /// moved exactly as far as the pointer. Measured before release on purpose: the test strip
        /// snaps to cards, and after release its snap animation moves the card on at a speed that
        /// depends on the frame rate (batch runs and the Editor differ).
        /// </summary>
        IEnumerator AssertFollows(Vector2 drag, float expectedOffsetDp = float.NaN)
        {
            var card = _strip.Find("Cards").GetChild(1);
            var before = Centre(card);
            var from = Centre(_strip) + new Vector2(5, 5);
            Set(_mouse.position, from);
            yield return null;
            Press(_mouse.leftButton);
            yield return null;
            const int steps = 10;
            for (var i = 1; i <= steps; i++)
            {
                Set(_mouse.position, from + drag * ((float)i / steps));
                yield return null;
            }
            var moved = Centre(card) - before;
            var offset = ((StripFrame)FrameOf(_strip)).Model.OffsetDp;
            Release(_mouse.leftButton);
            yield return null;
            Assert.AreEqual(drag.x, moved.x, 2f, $"moved {moved}");
            Assert.AreEqual(drag.y, moved.y, 2f, $"moved {moved}");
            if (!float.IsNaN(expectedOffsetDp)) Assert.AreEqual(expectedOffsetDp, offset, 2f, "offset while held");
        }

        [UnityTest]
        public IEnumerator ScaledParentFollowsPointerOneToOne()
        {
            yield return Build(space: Space.Scaled);
            yield return AssertFollows(new Vector2(-60, 0), expectedOffsetDp: 120f); // 120 local at half scale
        }

        [UnityTest]
        public IEnumerator RotatedStripFollowsPointerOneToOne()
        {
            yield return Build(space: Space.Rotated);
            yield return AssertFollows(new Vector2(0, -60), expectedOffsetDp: 60f);
        }

        [UnityTest]
        public IEnumerator WorldSpaceCanvasFollowsPointerOneToOne()
        {
            yield return Build(space: Space.World);
            yield return AssertFollows(new Vector2(-60, 0));
        }
    }
}
