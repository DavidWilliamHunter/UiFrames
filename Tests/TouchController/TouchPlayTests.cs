using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Threads.TouchController;
using Threads.TouchController.Intentions;
using Threads.UiFrames.Input;
using Threads.UiFrames.TouchInput;
using Threads.UiFrames.UGUI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.UIElements;
using Image = UnityEngine.UI.Image;

namespace Threads.UiFrames.Tests
{
    /// <summary>
    /// UI Frames with TouchController and a simulated touchscreen: fingers on frames are claimed
    /// away from game intentions, two fingers drag two frames, and joystick frames move their knob
    /// and write the TouchSticks device, in uGUI and UI Toolkit.
    /// </summary>
    public class TouchPlayTests : InputTestFixture
    {
        readonly List<Object> _created = new List<Object>();
        UiFramesRunner _frames;
        IntentionRunner _touch;
        UiFramesTouch _bridge;
        RectTransform _canvas;
        int _frameRate;

        public override void Setup()
        {
            base.Setup();
            InputSystem.AddDevice<Touchscreen>();
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

        T Keep<T>(T o) where T : Object { _created.Add(o); return o; }

        IEnumerator Wait(float seconds = 0.3f)
        {
            var end = Time.unscaledTime + seconds;
            while (Time.unscaledTime < end) yield return null;
        }

        /// <summary>An EventSystem, an overlay canvas, both runners and the bridge.</summary>
        IEnumerator Build(System.Action<RectTransform> addUi = null, UIDocument document = null)
        {
            Keep(new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule)))
                .GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            var canvasGo = Keep(new GameObject("Canvas", typeof(Canvas), typeof(GraphicRaycaster)));
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas = (RectTransform)canvasGo.transform;
            addUi?.Invoke(_canvas);

            var go = Keep(new GameObject("Runners"));
            go.SetActive(false);
            _touch = go.AddComponent<IntentionRunner>();
            _touch.SimulateTouchInEditor = false;
            _touch.PlatformOverride = TouchPlatform.Android;
            _touch.CreateTouchSticks = true;
            _frames = go.AddComponent<UiFramesRunner>();
            _frames.uguiRoots.Add(_canvas);
            if (document) _frames.documents.Add(document);
            _bridge = go.AddComponent<UiFramesTouch>();
            _bridge.frames = _frames;
            _bridge.touch = _touch;
            go.SetActive(true);
            yield return Wait(0.2f);
            Assert.IsTrue(_bridge.IsAttached, "the bridge attached to both runners");
        }

        IEnumerator FingerDrag(int finger, Vector2 from, Vector2 by, int steps = 12)
        {
            BeginTouch(finger, from);
            yield return null;
            for (var i = 1; i <= steps; i++) { MoveTouch(finger, from + by * ((float)i / steps)); yield return null; }
            yield return Wait(0.1f);
            EndTouch(finger, from + by);
            yield return null;
            yield return null;
        }

        static Vector2 Centre(RectTransform rt) => RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));

        RectTransform Strip(RectTransform parent, Vector2 position, string name)
        {
            var strip = UguiFrameFactory.CardStrip(parent, new Vector2(240, 100), name: name);
            strip.anchoredPosition = position;
            for (var i = 0; i < 8; i++) UguiFrameFactory.AddCard(strip, new Vector2(100, 100));
            return strip;
        }

        Intention GameDrag(out List<string> started)
        {
            var log = new List<string>();
            var game = new Intention("Game camera", _touch.Intentions).Bind<DragGesture>();
            game.Started += e => log.Add("started");
            game.Enabled = true;
            started = log;
            return game;
        }

        // ---- Claiming fingers on frames ----

        [UnityTest]
        public IEnumerator FingerOnAFrameIsNotGivenToGameIntentions()
        {
            RectTransform strip = null;
            yield return Build(c => strip = Strip(c, new Vector2(0, 100), "Strip"));
            var game = GameDrag(out var started);
            var frame = (StripFrame)_frames.Frames.Find(strip.gameObject);

            yield return FingerDrag(1, Centre(strip), new Vector2(-120, 0));
            Assert.Greater(frame.Model.OffsetDp, 0f, "the strip moved");
            CollectionAssert.IsEmpty(started, "the game did not see the finger on the frame");

            yield return FingerDrag(2, new Vector2(40, 40), new Vector2(120, 0)); // empty screen
            CollectionAssert.AreEqual(new[] { "started" }, started, "but does see fingers elsewhere");
            game.Enabled = false;
        }

        [UnityTest]
        public IEnumerator SlopAndDensityComeFromTouchController()
        {
            yield return Build();
            Assert.AreEqual(_touch.Touch.Settings.Data.Motion.touchSlopDp, _frames.Environment.DragSlopDp);
            Assert.AreEqual(_touch.Touch.Environment.PixelsPerDp, _frames.Environment.PixelsPerDp);
        }

        // ---- Several fingers ----

        [UnityTest]
        public IEnumerator TwoFingersDragTwoStripsAtOnce()
        {
            RectTransform a = null, b = null;
            yield return Build(c =>
            {
                a = Strip(c, new Vector2(0, 120), "A");
                b = Strip(c, new Vector2(0, -60), "B");
            });
            var fa = (StripFrame)_frames.Frames.Find(a.gameObject);
            var fb = (StripFrame)_frames.Frames.Find(b.gameObject);
            Vector2 pa = Centre(a), pb = Centre(b);
            BeginTouch(1, pa);
            BeginTouch(2, pb);
            yield return null;
            for (var i = 1; i <= 12; i++)
            {
                MoveTouch(1, pa + new Vector2(-10 * i, 0));
                MoveTouch(2, pb + new Vector2(-6 * i, 0));
                yield return null;
            }
            Assert.Greater(fa.Model.OffsetDp, 60f, "finger 1 dragged A");
            Assert.Greater(fb.Model.OffsetDp, 30f, "finger 2 dragged B at the same time");
            yield return Wait(0.1f);
            EndTouch(1, pa + new Vector2(-120, 0));
            EndTouch(2, pb + new Vector2(-72, 0));
            yield return null;
        }

        // ---- Joysticks ----

        [UnityTest]
        public IEnumerator UguiJoystickMovesItsKnobAndWritesTheStick()
        {
            RectTransform stick = null, knob = null;
            yield return Build(c =>
            {
                stick = new GameObject("Stick", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
                stick.SetParent(c, false);
                stick.sizeDelta = new Vector2(160, 160);
                stick.anchoredPosition = new Vector2(-150, -100);
                stick.gameObject.AddComponent<UiFrameClasses>().SetClasses(JoystickKind.Marker);
                knob = new GameObject("Knob", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
                knob.SetParent(stick, false);
                knob.sizeDelta = new Vector2(50, 50);
                knob.gameObject.AddComponent<UiFrameClasses>().SetClasses(JoystickKind.KnobClass);
            });
            var frame = _frames.Frames.Find(stick.gameObject) as JoystickFrame;
            Assert.IsNotNull(frame, "uf-joystick became a joystick frame");
            var ppd = _touch.Touch.Environment.PixelsPerDp;
            var centre = Centre(stick);
            var move = new Vector2(30f * ppd, 0f); // 30 dp, inside the 60 dp radius

            BeginTouch(1, centre);
            yield return null;
            for (var i = 1; i <= 6; i++) { MoveTouch(1, centre + move * (i / 6f)); yield return null; }
            Assert.IsTrue(frame.Joystick.IsHeld);
            Assert.AreEqual(move.x, knob.anchoredPosition.x, 2f, "the knob follows the thumb 1:1");
            yield return null; // the stick device is written on the next input update
            Assert.AreEqual((0.5f - 0.1f) / 0.9f, _touch.TouchSticks.LeftStick.ReadValue().x, 0.03f, "30 of 60 dp, after the 10 % dead zone");
            EndTouch(1, centre + move);
            yield return null;
            yield return null;
            Assert.AreEqual(0f, knob.anchoredPosition.x, 0.5f, "back to the centre");
            Assert.AreEqual(Vector2.zero, _touch.TouchSticks.LeftStick.ReadValue());
        }

        [UnityTest]
        public IEnumerator ToolkitJoystickMovesItsKnob()
        {
            var settings = Keep(ScriptableObject.CreateInstance<PanelSettings>());
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            var docGo = Keep(new GameObject("Doc"));
            var doc = docGo.AddComponent<UIDocument>();
            doc.panelSettings = settings;
            yield return null;
            var stick = new VisualElement { name = "stick" };
            stick.style.position = Position.Absolute;
            stick.style.left = 60;
            stick.style.top = 60;
            stick.style.width = 160;
            stick.style.height = 160;
            stick.AddToClassList(JoystickKind.Marker);
            var knob = new VisualElement { name = "knob" };
            knob.style.position = Position.Absolute;
            knob.style.left = 55;
            knob.style.top = 55;
            knob.style.width = 50;
            knob.style.height = 50;
            knob.AddToClassList(JoystickKind.KnobClass);
            stick.Add(knob);
            doc.rootVisualElement.Add(stick);
            yield return null;

            yield return Build(document: doc);
            yield return Wait(0.2f);
            var frame = _frames.Frames.Find(stick) as JoystickFrame;
            Assert.IsNotNull(frame, "uf-joystick became a joystick frame");
            var ppd = _touch.Touch.Environment.PixelsPerDp;
            var centre = new Vector2(140f, Screen.height - 140f); // panel (140, 140), y flipped
            var move = new Vector2(0f, 30f * ppd); // up the screen

            BeginTouch(1, centre);
            yield return null;
            for (var i = 1; i <= 6; i++) { MoveTouch(1, centre + move * (i / 6f)); yield return null; }
            yield return null;
            Assert.IsTrue(frame.Joystick.IsHeld, "pressed on the joystick");
            var t = knob.resolvedStyle.translate;
            Assert.AreEqual(0f, t.x, 1f);
            Assert.AreEqual(-move.y, t.y, 2f, "the knob moved up (panel y is down)");
            Assert.Greater(_touch.TouchSticks.LeftStick.ReadValue().y, 0.3f);
            EndTouch(1, centre + move);
            yield return null;
        }
    }
}
