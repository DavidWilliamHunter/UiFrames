using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Threads.UiFrames.Input;
using Threads.UiFrames.UGUI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.UI;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.UIElements;
using Slider = UnityEngine.UI.Slider;
using Button = UnityEngine.UI.Button;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Threads.UiFrames.Tests
{
    /// <summary>
    /// End to end with a simulated keyboard, as a player would use the demo: select a frame, enter it,
    /// choose a card, enter it, adjust a slider, back out; a deck; a drawer. uGUI scenes are built
    /// with <see cref="UguiFrameFactory"/> (so the factory's setup is checked too); the UI Toolkit
    /// scene uses the test UXML, with and without an EventSystem.
    /// </summary>
    public class KeyboardFlowTests : InputTestFixture
    {
        readonly List<Object> _created = new List<Object>();
        readonly List<string> _warnings = new List<string>();
        Keyboard _kb;
        UiFramesRunner _runner;
        int _frameRate;

        public override void Setup()
        {
            base.Setup();
            _kb = InputSystem.AddDevice<Keyboard>();
            _frameRate = Application.targetFrameRate;
            Application.targetFrameRate = 60;
            _warnings.Clear();
            Application.logMessageReceived += OnLog;
        }

        public override void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            Application.targetFrameRate = _frameRate;
            foreach (var o in _created) if (o) Object.Destroy(o);
            _created.Clear();
            base.TearDown();
        }

        void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Warning && message.Contains("UI Frames")) _warnings.Add(message);
        }

        T Keep<T>(T o) where T : Object { _created.Add(o); return o; }

        IEnumerator Tap(KeyControl key)
        {
            Press(key);
            yield return null;
            Release(key);
            yield return null;
            yield return null;
        }

        IEnumerator Wait(float seconds = 0.5f)
        {
            var end = Time.unscaledTime + seconds;
            while (Time.unscaledTime < end) yield return null;
        }

        FrameNavigator Nav => _runner.Navigators[0];
        string Selected => Nav.Selected?.Name;

        // ---------------- uGUI, built with the factory ----------------

        Slider _slider;
        int _clicks;

        IEnumerator BuildUgui()
        {
            Keep(new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule)))
                .GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            var canvas = Keep(new GameObject("Canvas", typeof(Canvas), typeof(GraphicRaycaster)));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var root = (RectTransform)canvas.transform;

            var strip = UguiFrameFactory.CardStrip(root, new Vector2(500, 120), name: "Photos");
            ((RectTransform)strip).anchoredPosition = new Vector2(0, 150);
            for (var i = 1; i <= 6; i++)
            {
                var card = UguiFrameFactory.AddCard(strip, new Vector2(100, 100), name: $"Card {i}");
                if (i == 3)
                {
                    var s = DefaultControls.CreateSlider(new DefaultControls.Resources());
                    s.transform.SetParent(card, false);
                    ((RectTransform)s.transform).sizeDelta = new Vector2(80, 20);
                    _slider = s.GetComponent<Slider>();
                    _slider.value = 0.5f;
                }
                else if (i > 1)
                {
                    var b = DefaultControls.CreateButton(new DefaultControls.Resources());
                    b.name = $"Button {i}";
                    b.transform.SetParent(card, false);
                    ((RectTransform)b.transform).sizeDelta = new Vector2(80, 30);
                    b.GetComponent<Button>().onClick.AddListener(() => _clicks++);
                }
            }
            var deck = UguiFrameFactory.CardDeck(root, new Vector2(3 * 100 + 2 * 12, 100), name: "Deck");
            deck.anchoredPosition = new Vector2(0, -50);
            for (var i = 0; i < 6; i++) UguiFrameFactory.AddCard(deck, new Vector2(100, 100), name: $"Deck {(char)('A' + i)}");
            var drawer = UguiFrameFactory.Drawer(root, FrameEdge.Left, 200, out _, "Menu");
            for (var i = 0; i < 2; i++)
            {
                var b = DefaultControls.CreateButton(new DefaultControls.Resources());
                b.name = $"Menu {(char)('A' + i)}";
                b.transform.SetParent(drawer, false);
                ((RectTransform)b.transform).anchoredPosition = new Vector2(0, 40 - i * 40);
            }

            var go = Keep(new GameObject("Runner"));
            go.SetActive(false);
            _runner = go.AddComponent<UiFramesRunner>();
            _runner.uguiRoots.Add(root);
            go.SetActive(true);
            _runner.Environment.PixelsPerDp = 1f;
            _clicks = 0;
            yield return Wait(0.2f);
        }

        [UnityTest]
        public IEnumerator UguiFlowFromFactoryBuiltScene()
        {
            yield return BuildUgui();
            CollectionAssert.IsEmpty(_warnings, "the factory builds frames without setup problems");

            yield return Tap(_kb.tabKey);
            Assert.AreEqual("Photos", Selected);
            yield return Tap(_kb.enterKey);
            Assert.AreEqual("Card 1", Selected);
            yield return Tap(_kb.rightArrowKey);
            yield return Tap(_kb.rightArrowKey);
            Assert.AreEqual("Card 3", Selected);
            yield return Tap(_kb.enterKey);
            Assert.AreSame(_slider.gameObject, Nav.Selected.Native);
            var v0 = _slider.value;
            yield return Tap(_kb.rightArrowKey);
            Assert.AreEqual(v0, _slider.value, 1e-4f, "not adjusting before Enter");
            yield return Tap(_kb.enterKey);
            yield return Tap(_kb.rightArrowKey);
            Assert.Greater(_slider.value, v0, "adjusting after Enter");
            yield return Tap(_kb.escapeKey);
            yield return Tap(_kb.escapeKey);
            Assert.AreEqual("Card 3", Selected);
            yield return Tap(_kb.rightArrowKey);
            yield return Tap(_kb.enterKey); // card 4: one button, straight to it
            Assert.AreEqual("Button 4", Selected);
            yield return Tap(_kb.enterKey);
            Assert.AreEqual(1, _clicks, "Enter presses the button");
            yield return Tap(_kb.escapeKey);
            yield return Tap(_kb.escapeKey);
            Assert.AreEqual("Photos", Selected);

            yield return Tap(_kb.downArrowKey);
            Assert.AreEqual("Deck", Selected);
            var activated = new List<string>();
            _runner.onActivated.AddListener(activated.Add);
            yield return Tap(_kb.enterKey);
            yield return Tap(_kb.enterKey);
            CollectionAssert.AreEqual(new[] { "Deck A" }, activated);
            yield return Tap(_kb.escapeKey);

            yield return Tap(_kb.f6Key);
            Assert.AreEqual("Menu", Selected);
            yield return Tap(_kb.enterKey);
            yield return Wait();
            Assert.AreEqual("Menu A", Selected);
            yield return Tap(_kb.escapeKey);
            yield return Wait();
            Assert.AreEqual("Menu", Selected);
            for (var i = 0; i < 4; i++)
            {
                yield return Tap(_kb.tabKey);
                StringAssert.DoesNotStartWith("Menu ", Selected, "a closed drawer's buttons are never reached");
            }
        }

        // ---------------- UI Toolkit, keys through the runner ----------------

        UIDocument _doc;

        IEnumerator BuildToolkit(bool withEventSystem)
        {
#if UNITY_EDITOR
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Packages/com.threads.uiframes/Tests/PlayMode/TestAssets/TestFrames.uxml");
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>("Packages/com.threads.uiframes/Tests/PlayMode/TestAssets/TestTheme.tss");
#else
            VisualTreeAsset tree = null;
            ThemeStyleSheet theme = null;
#endif
            if (tree == null) Assert.Ignore("Test UXML is only loadable in the Editor.");
            if (withEventSystem)
                Keep(new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule)))
                    .GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            var settings = Keep(ScriptableObject.CreateInstance<PanelSettings>());
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            settings.themeStyleSheet = theme;
            var go = Keep(new GameObject("Doc"));
            go.SetActive(false);
            _doc = go.AddComponent<UIDocument>();
            _doc.panelSettings = settings;
            _doc.visualTreeAsset = tree;
            _runner = go.AddComponent<UiFramesRunner>();
            _runner.documents.Add(_doc);
            go.SetActive(true);
            _runner.Environment.PixelsPerDp = 1f;
            yield return Wait(0.2f);
        }

        IEnumerator ToolkitFlow()
        {
            var clicks = 0;
            _doc.rootVisualElement.Q<UnityEngine.UIElements.Button>("b1").clicked += () => clicks++;
            yield return Tap(_kb.tabKey);
            Assert.AreEqual("strip", Selected);
            yield return Tap(_kb.enterKey);
            yield return Tap(_kb.rightArrowKey);
            var cards = _doc.rootVisualElement.Q("cards");
            Assert.AreSame(cards[1], Nav.Selected.Native, "one key, one step");
            yield return Tap(_kb.enterKey);
            Assert.AreEqual("b1", Selected);
            yield return Tap(_kb.enterKey);
            Assert.AreEqual(1, clicks, "Enter presses the button, once");
            yield return Tap(_kb.escapeKey);
            yield return Tap(_kb.escapeKey);
            Assert.AreEqual("strip", Selected);
            Assert.AreEqual(0, Nav.Depth);
        }

        [UnityTest]
        public IEnumerator ToolkitSliderStepsByArrowStep()
        {
            yield return BuildToolkit(withEventSystem: false);
            var slider = _doc.rootVisualElement.Q<UnityEngine.UIElements.Slider>("slider");
            yield return Tap(_kb.tabKey);
            yield return Tap(_kb.enterKey);
            yield return Tap(_kb.rightArrowKey);
            yield return Tap(_kb.rightArrowKey);
            yield return Tap(_kb.enterKey); // card 2: the slider
            yield return Tap(_kb.rightArrowKey);
            Assert.AreEqual(5f, slider.value, 1e-4f, "not adjusting yet");
            yield return Tap(_kb.enterKey);
            yield return Tap(_kb.rightArrowKey);
            Assert.AreEqual(7f, slider.value, 1e-3f, "--uf-arrow-step: 0.2 of 0..10");
        }

        [UnityTest]
        public IEnumerator ToolkitFlowWithoutEventSystem()
        {
            yield return BuildToolkit(withEventSystem: false);
            yield return ToolkitFlow();
        }

        [UnityTest]
        public IEnumerator ToolkitFlowWithEventSystem()
        {
            yield return BuildToolkit(withEventSystem: true);
            yield return ToolkitFlow();
        }
    }
}
