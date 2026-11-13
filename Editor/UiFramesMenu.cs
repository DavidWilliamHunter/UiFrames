using Threads.UiFrames.Input;
using Threads.UiFrames.UGUI;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UnityEngine.UIElements;

namespace Threads.UiFrames.Editor
{
    /// <summary>GameObject > UI Frames: correctly set-up uGUI frames (built by <see cref="UguiFrameFactory"/>)
    /// and a UI Frames Runner wired to the scene's canvases and UI documents.</summary>
    static class UiFramesMenu
    {
        const string Root = "GameObject/UI Frames/";
        const int Priority = 2100;
        const string DefaultsUss = "Packages/com.threads.uiframes/Runtime/Toolkit/UiFrames.uss";
        const string ThemeUss = "Packages/com.threads.uiframes/Runtime/Toolkit/UiFrames-Theme.uss";

        [MenuItem(Root + "Card Strip", false, Priority)]
        static void CardStrip(MenuCommand cmd) => Finish(Fill(UguiFrameFactory.CardStrip(Parent(cmd), new Vector2(600, 170)), 8, new Vector2(160, 150)));

        [MenuItem(Root + "Vertical Card Strip", false, Priority + 1)]
        static void VerticalStrip(MenuCommand cmd) =>
            Finish(Fill(UguiFrameFactory.CardStrip(Parent(cmd), new Vector2(300, 400), FrameAxis.Vertical, 2f, "Vertical Card Strip"), 12, new Vector2(300, 48)));

        [MenuItem(Root + "Card Deck", false, Priority + 2)]
        static void CardDeck(MenuCommand cmd) =>
            Finish(Fill(UguiFrameFactory.CardDeck(Parent(cmd), new Vector2(3 * 160 + 2 * 12, 150)), 8, new Vector2(160, 150)));

        [MenuItem(Root + "Drawer (Left)", false, Priority + 20)]
        static void DrawerLeft(MenuCommand cmd) => Finish(UguiFrameFactory.Drawer(CanvasOf(Parent(cmd)), FrameEdge.Left, 280, out _));

        [MenuItem(Root + "Drawer (Right)", false, Priority + 21)]
        static void DrawerRight(MenuCommand cmd) => Finish(UguiFrameFactory.Drawer(CanvasOf(Parent(cmd)), FrameEdge.Right, 280, out _));

        [MenuItem(Root + "Drawer (Bottom)", false, Priority + 22)]
        static void DrawerBottom(MenuCommand cmd) => Finish(UguiFrameFactory.Drawer(CanvasOf(Parent(cmd)), FrameEdge.Bottom, 240, out _));

        [MenuItem(Root + "Add Card", false, Priority + 40)]
        static void AddCard()
        {
            var frame = SelectedCardFrame();
            var content = UguiFrameFactory.FindContent(frame);
            var size = content && content.childCount > 0 ? ((RectTransform)content.GetChild(content.childCount - 1)).sizeDelta : new Vector2(160, 150);
            Finish(UguiFrameFactory.AddCard(frame, size));
        }

        [MenuItem(Root + "Add Card", true)]
        static bool CanAddCard() => SelectedCardFrame();

        [MenuItem(Root + "Runner (wire this scene)", false, Priority + 60)]
        static void Runner()
        {
            var existing = Object.FindFirstObjectByType<UiFramesRunner>();
            var runner = existing ? existing : new GameObject("UI Frames").AddComponent<UiFramesRunner>();
            if (!existing) Undo.RegisterCreatedObjectUndo(runner.gameObject, "Create UI Frames Runner");
            Undo.RecordObject(runner, "Wire UI Frames Runner");
            foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (!canvas.isRootCanvas) continue;
                var rt = (RectTransform)canvas.transform;
                if (!runner.uguiRoots.Contains(rt)) runner.uguiRoots.Add(rt);
            }
            foreach (var doc in Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
                if (!runner.documents.Contains(doc)) runner.documents.Add(doc);
            if (!runner.toolkitDefaults) runner.toolkitDefaults = AssetDatabase.LoadAssetAtPath<StyleSheet>(DefaultsUss);
            if (!runner.toolkitTheme) runner.toolkitTheme = AssetDatabase.LoadAssetAtPath<StyleSheet>(ThemeUss);
            if (!runner.eventSystem) runner.eventSystem = Object.FindFirstObjectByType<EventSystem>();
            EditorUtility.SetDirty(runner);
            Selection.activeGameObject = runner.gameObject;
        }

        // ---- Helpers ----

        static RectTransform SelectedCardFrame()
        {
            var t = Selection.activeTransform;
            for (; t; t = t.parent)
            {
                var c = t.GetComponent<UiFrameClasses>();
                if (c && (c.HasClass(FrameTags.CardStrip) || c.HasClass(FrameTags.CardDeck))) return (RectTransform)t;
            }
            return null;
        }

        static RectTransform Fill(RectTransform frame, int count, Vector2 cardSize)
        {
            for (var i = 0; i < count; i++) UguiFrameFactory.AddCard(frame, cardSize);
            return frame;
        }

        static void Finish(RectTransform created)
        {
            Undo.RegisterCreatedObjectUndo(created.gameObject, "Create " + created.name);
            Selection.activeGameObject = created.gameObject;
        }

        /// <summary>The selected UI object, else a canvas in the scene, else a new canvas (with an
        /// EventSystem using the Input System UI module), as GameObject > UI does.</summary>
        static Transform Parent(MenuCommand cmd)
        {
            var selected = (cmd.context as GameObject)?.transform ?? Selection.activeTransform;
            if (selected && selected.GetComponentInParent<Canvas>()) return selected;
            var canvas = Object.FindFirstObjectByType<Canvas>();
            if (!canvas)
            {
                var go = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                go.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                Undo.RegisterCreatedObjectUndo(go, "Create Canvas");
                canvas = go.GetComponent<Canvas>();
            }
            if (!Object.FindFirstObjectByType<EventSystem>())
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                Undo.RegisterCreatedObjectUndo(es, "Create EventSystem");
            }
            return canvas.transform;
        }

        /// <summary>Drawers belong to the root canvas: they slide in from its edges.</summary>
        static Transform CanvasOf(Transform t)
        {
            var canvas = t.GetComponentInParent<Canvas>();
            return canvas ? canvas.rootCanvas.transform : t;
        }
    }
}
