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

        IEnumerator BuildUgui(InputActionAsset moduleKeys = null, InputActionAsset frameKeys = null)
        {
            var module = Keep(new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule)))
                .GetComponent<InputSystemUIInputModule>();
            if (moduleKeys == null) module.AssignDefaultActions();
            else
            {
                // A project's own UI actions, as set on the input module in the Inspector.
                module.actionsAsset = moduleKeys;
                module.move = InputActionReference.Create(moduleKeys.FindAction("UI/Navigate"));
                module.submit = InputActionReference.Create(moduleKeys.FindAction("UI/Submit"));
                module.cancel = InputActionReference.Create(moduleKeys.FindAction("UI/Cancel"));
            }
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
            _runner.navigationActions = frameKeys;
            go.SetActive(true);
            _runner.Environment.PixelsPerDp = 1f;
            _clicks = 0;
            yield return Wait(0.2f);
        }

        [UnityTest]
        public IEnumerator UguiPointerOnlyControlsAreNotKeyboardStops()
        {
            yield return BuildUgui();
            var card = GameObject.Find("Card 1").transform;
            var b = DefaultControls.CreateButton(new DefaultControls.Resources());
            b.name = "Pointer Button";
            b.transform.SetParent(card, false);
            b.AddComponent<UiFrameClasses>().SetClasses(FrameTags.PointerOnly);
            _runner.Rescan();
            Assert.AreEqual(Navigation.Mode.None, b.GetComponent<Button>().navigation.mode, "a click does not select it");

            var activated = new List<string>();
            _runner.onActivated.AddListener(activated.Add);
            yield return Tap(_kb.tabKey);
            yield return Tap(_kb.enterKey);
            Assert.AreEqual("Card 1", Selected);
            yield return Tap(_kb.enterKey);
            Assert.AreEqual("Card 1", Selected, "the button is not a stop");
            CollectionAssert.AreEqual(new[] { "Card 1" }, activated, "so Enter acts on the card");
        }

        [UnityTest]
        public IEnumerator UguiClickThatMovesTheSelectionDoesNotActOnTheNewOne()
        {
            yield return BuildUgui();
            IFrameElement Find(GameObject go)
            {
                IFrameElement found = null;
                void Walk(IFrameElement e)
                {
                    if (found != null) return;
                    if (ReferenceEquals(e.Native, go)) { found = e; return; }
                    foreach (var c in e.Children) Walk(c);
                }
                Walk(Nav.Host.Root);
                return found;
            }
            var card1 = GameObject.Find("Card 1");
            GameObject.Find("Button 4").GetComponent<Button>().onClick.AddListener(() => Nav.SelectElement(Find(card1)));
            var activated = new List<string>();
            _runner.onActivated.AddListener(activated.Add);
            yield return Tap(_kb.tabKey);
            yield return Tap(_kb.enterKey);
            for (var i = 0; i < 3; i++) yield return Tap(_kb.rightArrowKey);
            yield return Tap(_kb.enterKey);
            Assert.AreEqual("Button 4", Selected);
            yield return Tap(_kb.enterKey);
            Assert.AreEqual("Card 1", Selected, "the click moved the selection");
            CollectionAssert.IsEmpty(activated, "the same Enter did not also act on the card");
        }

        [UnityTest]
        public IEnumerator TopLevelEscGoesToTheHook()
        {
            yield return BuildUgui();
            var seen = new List<string>();
            Nav.TopLevelCancel += e => { seen.Add(e?.Name); return true; };
            yield return Tap(_kb.tabKey);
            yield return Tap(_kb.enterKey);
            yield return Tap(_kb.escapeKey);
            CollectionAssert.IsEmpty(seen, "leaving a level is not a top-level Esc");
            yield return Tap(_kb.escapeKey);
            CollectionAssert.AreEqual(new[] { "Photos" }, seen);
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

        IEnumerator BuildToolkit(bool withEventSystem, InputActionAsset keys = null, string mapName = "UiFrames")
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
            _runner.navigationActions = keys;
            _runner.navigationMapName = mapName;
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

        // ---------------- Key configuration through the Input System ----------------

        [UnityTest]
        public IEnumerator UguiKeysComeFromTheInputModuleAndYourAsset()
        {
            // The input module's UI actions (arrows/Enter/Esc for uGUI) remapped to WASD/E/Q...
            var ui = Keep(ScriptableObject.CreateInstance<InputActionAsset>());
            var uiMap = ui.AddActionMap("UI");
            uiMap.AddAction("Navigate", InputActionType.PassThrough, expectedControlLayout: "Vector2")
                .AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            uiMap.AddAction("Submit", InputActionType.Button, "<Keyboard>/e");
            uiMap.AddAction("Cancel", InputActionType.Button, "<Keyboard>/q");
            // ...and the runner's own keys (Tab is UI Frames', as uGUI has none) with Tab on R.
            var frames = Keep(ScriptableObject.CreateInstance<InputActionAsset>());
            frames.AddActionMap("UiFrames").AddAction(FrameNavigationController.TabAction, InputActionType.Button, "<Keyboard>/r");

            yield return BuildUgui(ui, frames);
            yield return Tap(_kb.rKey);
            Assert.AreEqual("Photos", Selected, "R is Tab");
            yield return Tap(_kb.eKey);
            Assert.AreEqual("Card 1", Selected, "E goes in (the module's Submit)");
            yield return Tap(_kb.dKey);
            Assert.AreEqual("Card 2", Selected, "D moves (the module's Navigate)");
            yield return Tap(_kb.rightArrowKey);
            Assert.AreEqual("Card 2", Selected, "the arrows are not bound");
            yield return Tap(_kb.qKey);
            Assert.AreEqual("Photos", Selected, "Q comes out (the module's Cancel)");
        }

        static InputActionAsset Remapped()
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            var map = asset.AddActionMap("UiFrames");
            var nav = map.AddAction(FrameNavigationController.NavigateAction, InputActionType.Value, expectedControlLayout: "Vector2");
            nav.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            map.AddAction(FrameNavigationController.SubmitAction, InputActionType.Button, "<Keyboard>/e");
            map.AddAction(FrameNavigationController.CancelAction, InputActionType.Button, "<Keyboard>/q");
            map.AddAction(FrameNavigationController.TabAction, InputActionType.Button, "<Keyboard>/r");
            return asset;
        }

        [UnityTest]
        public IEnumerator KeysComeFromYourActionsAsset()
        {
            var keys = Keep(Remapped());
            yield return BuildToolkit(withEventSystem: false, keys: keys);
            var cards = _doc.rootVisualElement.Q("cards");

            yield return Tap(_kb.tabKey);
            Assert.IsNull(Nav.Selected, "Tab is not bound in this asset");
            yield return Tap(_kb.rKey);
            Assert.AreEqual("strip", Selected, "R is this asset's Tab");
            yield return Tap(_kb.eKey);
            Assert.AreSame(cards[0], Nav.Selected.Native, "E goes in");
            yield return Tap(_kb.dKey);
            Assert.AreSame(cards[1], Nav.Selected.Native, "D moves right");
            yield return Tap(_kb.rightArrowKey);
            Assert.AreSame(cards[1], Nav.Selected.Native, "the arrows are not bound");
            yield return Tap(_kb.qKey);
            Assert.AreEqual("strip", Selected, "Q comes out");
        }

        [UnityTest]
        public IEnumerator BindingsCanBeOverriddenAtRunTime()
        {
            yield return BuildToolkit(withEventSystem: false);
            // What a rebinding screen does (PerformInteractiveRebinding ends in the same override).
            _runner.Navigation.Actions.FindAction(FrameNavigationController.SubmitAction).ApplyBindingOverride(0, "<Keyboard>/f");
            yield return Tap(_kb.tabKey);
            yield return Tap(_kb.enterKey);
            Assert.AreEqual(0, Nav.Depth, "Enter was rebound away");
            yield return Tap(_kb.fKey);
            Assert.AreEqual(1, Nav.Depth, "F now goes in");
        }

        [UnityTest]
        public IEnumerator ReverseIsAnActionToo()
        {
            yield return BuildToolkit(withEventSystem: false);
            yield return Tap(_kb.tabKey);
            yield return Tap(_kb.tabKey);
            Assert.AreEqual("vstrip", Selected);
            Press(_kb.leftShiftKey);
            yield return Tap(_kb.tabKey);
            Release(_kb.leftShiftKey);
            Assert.AreEqual("strip", Selected, "Shift+Tab goes back");

            _runner.Navigation.Actions.FindAction(FrameNavigationController.ReverseAction).ApplyBindingOverride(0, "<Keyboard>/ctrl");
            Press(_kb.leftShiftKey);
            yield return Tap(_kb.tabKey);
            Release(_kb.leftShiftKey);
            Assert.AreEqual("vstrip", Selected, "Shift no longer reverses");
            Press(_kb.leftCtrlKey);
            yield return Tap(_kb.tabKey);
            Release(_kb.leftCtrlKey);
            Assert.AreEqual("strip", Selected, "Ctrl does");
        }

        [UnityTest]
        public IEnumerator WrongMapNameIsReportedAndDefaultsUsed()
        {
            var keys = Keep(Remapped());
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("has no action map 'Nope'"));
            yield return BuildToolkit(withEventSystem: false, keys: keys, mapName: "Nope");
            yield return Tap(_kb.tabKey);
            Assert.AreEqual("strip", Selected, "the built-in keys");
        }

#if UNITY_EDITOR
        [Test]
        public void ShippedActionsAssetMatchesTheDefaults()
        {
            var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Packages/com.threads.uiframes/Runtime/Input/UiFramesNavigation.inputactions");
            Assert.IsNotNull(asset, "the package ships its keys as an editable asset");
            var shipped = asset.FindActionMap("UiFrames");
            var defaults = FrameNavigationController.CreateDefaultActions();
            try
            {
                Assert.AreEqual(defaults.actions.Count, shipped.actions.Count);
                foreach (var action in defaults.actions)
                {
                    var other = shipped.FindAction(action.name);
                    Assert.IsNotNull(other, action.name);
                    CollectionAssert.AreEqual(System.Linq.Enumerable.Select(action.bindings, b => b.path),
                        System.Linq.Enumerable.Select(other.bindings, b => b.path), action.name);
                }
            }
            finally { defaults.Dispose(); }
        }
#endif

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
