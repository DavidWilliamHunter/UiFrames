using System.IO;
using Threads.UiFrames.Input;
using Threads.UiFrames.UGUI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.UIElements;
using Image = UnityEngine.UI.Image;
#if THREADS_UF_TOUCHCONTROLLER
using Threads.TouchController.Intentions;
using Threads.UiFrames.TouchInput;
#endif

namespace Threads.UiFrames.Demo.Editor
{
    /// <summary>Threads > UI Frames > Build Demo Scenes: (re)creates the UI Toolkit and uGUI demo scenes.</summary>
    public static class FramesDemoBuilder
    {
        const string PackageUss = "Packages/com.threads.uiframes/Runtime/Toolkit/UiFrames.uss";
        const string PackageTheme = "Packages/com.threads.uiframes/Runtime/Toolkit/UiFrames-Theme.uss";

        [MenuItem("Threads/UI Frames/Build Demo Scenes")]
        public static void BuildAll()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var dir = DemoFolder();
            if (dir == null)
            {
                Debug.LogError("UI Frames: import the Demo sample first (FramesDemo.uxml not found).");
                return;
            }
            Directory.CreateDirectory(Path.Combine(dir, "Scenes"));
            Directory.CreateDirectory(Path.Combine(dir, "Ugui"));
            var toolkit = BuildToolkitScene(dir);
            var ugui = BuildUguiScene(dir);
            AssetDatabase.SaveAssets();
            Debug.Log($"UI Frames: built {toolkit} and {ugui}.");
            EditorSceneManager.OpenScene(toolkit);
        }

        static string DemoFolder()
        {
            foreach (var guid in AssetDatabase.FindAssets("FramesDemo t:VisualTreeAsset"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("/Toolkit/FramesDemo.uxml")) return Path.GetDirectoryName(Path.GetDirectoryName(path)).Replace('\\', '/');
            }
            return null;
        }

        // ---------------- UI Toolkit ----------------

        static string BuildToolkitScene(string dir)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AddCamera();

            var settingsPath = $"{dir}/Toolkit/DemoPanelSettings.asset";
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(settingsPath);
            if (!settings)
            {
                settings = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(settings, settingsPath);
            }
            settings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>($"{dir}/Toolkit/DemoTheme.tss");
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            settings.scale = 1f;
            EditorUtility.SetDirty(settings);

            // Touch reaches UI Toolkit only through an EventSystem (keyboard and gamepad come
            // through the runner either way).
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            var go = new GameObject("UI Toolkit Frames");
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = settings;
            doc.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>($"{dir}/Toolkit/FramesDemo.uxml");

            var runner = go.AddComponent<UiFramesRunner>();
            runner.documents.Add(doc);
            runner.toolkitDefaults = AssetDatabase.LoadAssetAtPath<StyleSheet>(PackageUss);
            runner.toolkitTheme = AssetDatabase.LoadAssetAtPath<StyleSheet>(PackageTheme);

            var demo = go.AddComponent<FramesDemo>();
            demo.runner = runner;
            demo.document = doc;
            AddTouch(go, runner);

            var path = $"{dir}/Scenes/UiFramesToolkitDemo.unity";
            EditorSceneManager.SaveScene(scene, path);
            return path;
        }

        // ---------------- uGUI ----------------

        static Font _font;
        static Font Font => _font ? _font : _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        static string BuildUguiScene(string dir)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AddCamera();

            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGo.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            var root = (RectTransform)canvasGo.transform;

            var bg = Rect("Background", root, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            bg.gameObject.AddComponent<Image>().color = new Color32(22, 26, 34, 255);

            Label(root, "Title", "UI Frames: uGUI", 22, new Vector2(72, -16), new Vector2(600, 30), FontStyle.Bold, Color.white);
            var status = Label(root, "Status", "", 14, new Vector2(72, -46), new Vector2(1100, 22), FontStyle.Normal, new Color32(255, 196, 64, 255));
            Label(root, "Help",
                "Mouse: drag a frame (also from its buttons), wheel to scroll, click the ≡ handle. Keyboard/gamepad: arrows/d-pad or Tab choose, Enter/A goes in " +
                "(frame → card → control; Enter presses a button, or starts/stops adjusting a slider), Esc/B comes back out. Page Up/Down, Home/End, F6 jumps to the next frame; Y toggles the drawer. Touch: everything above, plus the joystick (bottom right) with TouchController installed.",
                13, new Vector2(72, -70), new Vector2(1000, 40), FontStyle.Normal, new Color32(170, 180, 200, 255));

            // Card strip
            Label(root, "Strip Label", "Card strip: inertia, snaps to cards (class uf-card-strip + photos; the style sheet sets --uf-snap: card)",
                14, new Vector2(72, -118), new Vector2(1000, 22), FontStyle.Normal, new Color32(200, 210, 230, 255));
            // Frames come from UguiFrameFactory (also behind GameObject > UI Frames).
            var strip = Place(UguiFrameFactory.CardStrip(root, new Vector2(1000, 170), name: "Photos"), new Vector2(72, -142));
            strip.GetComponent<UiFrameClasses>().SetClasses("uf-card-strip", "photos");
            for (var i = 1; i <= 10; i++)
            {
                var card = Card(strip, $"Card {i}", i == 3 ? "3: slider keeps its drag" : i == 7 ? "7: uf-no-drag" : i.ToString(), new Color32(58, 92, 140, 255));
                if (i == 3) Slider(card);
                else if (i == 7)
                {
                    var nd = Rect("No Drag", card, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 10), new Vector2(-20, 60));
                    nd.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.3f);
                    Classes(nd, "uf-no-drag");
                }
                else Button(card, $"Press {i}");
            }

            // Card deck
            Label(root, "Deck Label", "Card deck: steps one card, always rests on whole cards (uf-card-deck)",
                14, new Vector2(72, -324), new Vector2(1000, 22), FontStyle.Normal, new Color32(200, 210, 230, 255));
            var deck = Place(UguiFrameFactory.CardDeck(root, new Vector2(3 * 160 + 2 * 12, 150), name: "Deck"), new Vector2(72, -348));
            foreach (var letter in "ABCDEFGH") Card(deck, $"Deck {letter}", letter.ToString(), new Color32(120, 70, 130, 255));

            // Vertical strip
            Label(root, "List Label", "Vertical strip: free scrolling (class list; the style sheet sets --uf-axis: vertical)",
                14, new Vector2(72, -510), new Vector2(1000, 22), FontStyle.Normal, new Color32(200, 210, 230, 255));
            // A vertical strip; its axis comes from the style sheet's ".list" rule rather than inline.
            var list = Place(UguiFrameFactory.CardStrip(root, new Vector2(504, 200), FrameAxis.Vertical, 1f, "List"), new Vector2(72, -534));
            var listClasses = list.GetComponent<UiFrameClasses>();
            listClasses.SetClasses("uf-card-strip", "list");
            listClasses.RemoveInline("--uf-axis"); // the rule decides
            for (var i = 1; i <= 20; i++)
            {
                var row = UguiFrameFactory.AddCard(list, new Vector2(504, 40), new Color32(46, 54, 70, 255), $"Row {i}");
                Label(row, "Text", $"Row {i}", 14, new Vector2(10, 0), new Vector2(400, 40), FontStyle.Normal, Color.white).alignment = TextAnchor.MiddleLeft;
            }

            // Drawer, laid out open against the left edge; the handle sticks out on the right.
            var drawer = UguiFrameFactory.Drawer(root, FrameEdge.Left, 280, out var handle, "Menu");
            var drawerClasses = drawer.GetComponent<UiFrameClasses>();
            drawerClasses.SetInline("--uf-edge", "auto");       // found from the handle
            drawerClasses.SetInline("--uf-easing", "ease-out"); // inline style wins over the sheet
            // The package look (outlines) plus: light the handle while the closed drawer is selected.
            var drawerVisual = drawer.gameObject.AddComponent<UiFrameStateVisual>();
            drawerVisual.entries.Clear();
            drawerVisual.AddDefaults();
            Label(drawer, "Menu Title", "Drawer (--uf-edge: auto)", 18, new Vector2(16, -16), new Vector2(250, 26), FontStyle.Bold, Color.white);
            for (var i = 0; i < 3; i++)
            {
                var b = Button(drawer, $"Menu {(char)('A' + i)}");
                var rt = (RectTransform)b.transform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
                rt.anchoredPosition = new Vector2(16, -56 - i * 44);
                rt.sizeDelta = new Vector2(248, 36);
            }
            Label(drawer, "Menu Note", "Drag me, fling me, or click the handle.", 13, new Vector2(16, -196), new Vector2(250, 40), FontStyle.Normal, new Color32(170, 180, 200, 255));
            handle.SetAsLastSibling();
            // Lit while the (closed) drawer is the keyboard selection.
            var lit = Rect("Selected Highlight", handle, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var litImage = lit.gameObject.AddComponent<Image>();
            litImage.color = Highlight;
            litImage.raycastTarget = false;
            lit.gameObject.SetActive(false);
            drawerVisual.entries.Add(new UiFrameStateVisual.Entry
            {
                className = FrameTags.Selected,
                kind = UiFrameStateVisual.Kind.Show,
                showTarget = lit.gameObject,
            });
            var hl = Label(handle, "Label", "≡", 26, Vector2.zero, new Vector2(44, 64), FontStyle.Bold, Color.white);
            hl.alignment = TextAnchor.MiddleCenter;
            hl.raycastTarget = false;

            // Style sheet asset: the uGUI counterpart of the USS file.
            var sheetPath = $"{dir}/Ugui/DemoFrames.asset";
            var sheet = AssetDatabase.LoadAssetAtPath<UiFrameStyleSheet>(sheetPath);
            if (!sheet)
            {
                sheet = ScriptableObject.CreateInstance<UiFrameStyleSheet>();
                AssetDatabase.CreateAsset(sheet, sheetPath);
            }
            sheet.rules.Clear();
            sheet.AddRule(".photos", new UiFrameDeclaration("--uf-snap", "card"), new UiFrameDeclaration("--uf-deceleration", "0.135"));
            sheet.AddRule(".uf-card-deck", new UiFrameDeclaration("--uf-deck-step", "card"), new UiFrameDeclaration("--uf-duration", "180ms"),
                new UiFrameDeclaration("--uf-visible-cards", "3"));
            sheet.AddRule(".list", new UiFrameDeclaration("--uf-axis", "vertical"));
            sheet.AddRule(".uf-drawer", new UiFrameDeclaration("--uf-edge", "auto"), new UiFrameDeclaration("--uf-duration", "220ms"));
            EditorUtility.SetDirty(sheet);

            // An on-screen joystick (uf-joystick): driven by TouchController when it is installed.
            var stick = Rect("Joystick", root, new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0), new Vector2(-40, 40), new Vector2(180, 180));
            var stickImage = stick.gameObject.AddComponent<Image>();
            stickImage.color = new Color(1f, 1f, 1f, 0.12f);
            Classes(stick, "uf-joystick");
            var knob = Rect("Knob", stick, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(64, 64));
            knob.gameObject.AddComponent<Image>().color = Highlight;
            Classes(knob, "uf-joystick__knob");

            var runnerGo = new GameObject("UI Frames");
            var runner = runnerGo.AddComponent<UiFramesRunner>();
            runner.uguiRoots.Add(root);
            runner.uguiStyleSheets.Add(sheet);
            runner.eventSystem = es.GetComponent<EventSystem>();
            var demo = runnerGo.AddComponent<FramesDemo>();
            demo.runner = runner;
            demo.uguiStatus = status;
            demo.uguiRoot = root;
            AddTouch(runnerGo, runner);

            var path = $"{dir}/Scenes/UiFramesUguiDemo.unity";
            EditorSceneManager.SaveScene(scene, path);
            return path;
        }

        /// <summary>With TouchController installed: its runner and the bridge (finger claiming,
        /// platform slop, joysticks). Editor touch simulation stays off so the mouse keeps its
        /// wheel; turn on Simulate Touch In Editor on the Intention Runner to try the joystick with a mouse.</summary>
        static void AddTouch(GameObject go, UiFramesRunner runner)
        {
#if THREADS_UF_TOUCHCONTROLLER
            var touch = go.AddComponent<IntentionRunner>();
            touch.SimulateTouchInEditor = false;
            touch.CreateTouchSticks = true;
            var bridge = go.AddComponent<UiFramesTouch>();
            bridge.frames = runner;
            bridge.touch = touch;
#endif
        }

        static void AddCamera()
        {
            var cam = new GameObject("Main Camera", typeof(Camera)).GetComponent<Camera>();
            cam.tag = "MainCamera";
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color32(22, 26, 34, 255);
        }

        static RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        static Text Label(Transform parent, string name, string text, int size, Vector2 topLeft, Vector2 box, FontStyle style, Color color)
        {
            var rt = Rect(name, parent, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), topLeft, box);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            return t;
        }

        static UiFrameClasses Classes(RectTransform rt, params string[] classes)
        {
            var c = rt.gameObject.AddComponent<UiFrameClasses>();
            c.SetClasses(classes);
            return c;
        }

        /// <summary>Anchors a frame at the top left of the screen.</summary>
        static RectTransform Place(RectTransform frame, Vector2 topLeft)
        {
            frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(0, 1);
            frame.anchoredPosition = topLeft;
            return frame;
        }

        static RectTransform Card(RectTransform frame, string name, string title, Color color)
        {
            var rt = UguiFrameFactory.AddCard(frame, new Vector2(160, 150), color, name);
            Label(rt, "Title", title, 18, new Vector2(10, -10), new Vector2(140, 60), FontStyle.Bold, Color.white);
            return rt;
        }

        static UnityEngine.UI.Button Button(Transform parent, string name)
        {
            var go = DefaultControls.CreateButton(new DefaultControls.Resources());
            go.name = name;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 0);
            rt.pivot = new Vector2(0.5f, 0);
            rt.anchoredPosition = new Vector2(0, 10);
            rt.sizeDelta = new Vector2(-20, 36);
            var text = go.GetComponentInChildren<Text>();
            text.font = Font;
            text.text = "Press";
            var button = go.GetComponent<UnityEngine.UI.Button>();
            button.colors = SelectedColors(button.colors);
            return button;
        }

        static void Slider(RectTransform card)
        {
            var go = DefaultControls.CreateSlider(new DefaultControls.Resources());
            var rt = (RectTransform)go.transform;
            rt.SetParent(card, false);
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 0);
            rt.pivot = new Vector2(0.5f, 0);
            rt.anchoredPosition = new Vector2(0, 20);
            rt.sizeDelta = new Vector2(-20, 20);
            var slider = go.GetComponent<UnityEngine.UI.Slider>();
            slider.value = 0.5f;
            slider.colors = SelectedColors(slider.colors);
        }

        static readonly Color Highlight = new Color32(255, 196, 64, 255);

        /// <summary>A keyboard-selected control turns bright, so the selection is obvious.</summary>
        static ColorBlock SelectedColors(ColorBlock colors)
        {
            colors.selectedColor = Highlight;
            colors.highlightedColor = new Color(1f, 0.9f, 0.6f);
            return colors;
        }
    }
}
