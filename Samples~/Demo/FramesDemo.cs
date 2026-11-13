using System.Text;
using Threads.UiFrames.Input;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;
using Button = UnityEngine.UIElements.Button;

namespace Threads.UiFrames.Demo
{
    /// <summary>
    /// Demo glue for both scenes: fills the vertical list, logs button presses, and shows which frame
    /// has focus and where the deck is. It only reads the runner's frames; all frame behaviour comes
    /// from the tags.
    /// </summary>
    public sealed class FramesDemo : MonoBehaviour
    {
        public UiFramesRunner runner;
        [Header("UI Toolkit scene")]
        public UIDocument document;
        [Header("uGUI scene")]
        public Text uguiStatus;
        public RectTransform uguiRoot;

        Label _status;
        string _lastPress = "";
        bool _wired;
        bool _hooked;
        readonly StringBuilder _sb = new StringBuilder();

        void OnEnable()
        {
            if (document && document.rootVisualElement != null) WireToolkit(document.rootVisualElement);
            if (uguiRoot)
                foreach (var b in uguiRoot.GetComponentsInChildren<UnityEngine.UI.Button>(true))
                {
                    var name = b.name;
                    b.onClick.AddListener(() => Pressed(name));
                }
        }

        void WireToolkit(VisualElement root)
        {
            if (_wired) return;
            _wired = true;
            _status = root.Q<Label>("status");
            var rows = root.Q("list-rows");
            if (rows != null && rows.childCount == 0)
            {
                for (var i = 1; i <= 20; i++)
                {
                    var row = new Label($"Row {i}") { name = $"Row {i}" };
                    row.AddToClassList("row");
                    rows.Add(row);
                }
            }
            root.Query<Button>().ForEach(b =>
            {
                var name = b.name;
                b.clicked += () => Pressed(name);
            });
            // The rows are picked up automatically (FrameEnvironment.AutoRescanSeconds); call
            // runner.Rescan() to have them at once.
        }

        void Pressed(string name) => _lastPress = $"pressed {name} at {Time.time:0.0}s";

        static readonly string[] s_levels = { "frames", "cards", "controls", "controls" };

        void Update()
        {
            if (document && !_wired && document.rootVisualElement != null) WireToolkit(document.rootVisualElement);
            if (!runner || !runner.IsRunning) return;
            if (!_hooked)
            {
                _hooked = true;
                // The same is available to designers as the runner's onActivated UnityEvent.
                runner.Activated += e => _lastPress = $"activated {e.Name} at {Time.time:0.0}s";
            }
            _sb.Clear();
            var nav = runner.Navigators.Count > 0 ? runner.Navigators[0] : null;
            if (nav != null && nav.Selected != null)
            {
                _sb.Append("keyboard: ").Append(s_levels[Mathf.Min(nav.Depth, s_levels.Length - 1)])
                   .Append(" › ").Append(nav.Selected.Name);
                if (nav.IsEditing) _sb.Append(" (adjusting: Enter or Esc to finish)");
            }
            else _sb.Append("keyboard: press an arrow key or Tab");
            foreach (var frame in runner.Frames.Frames)
                if (frame is DeckFrame deck) _sb.Append("   deck: ").Append(deck.Index + 1).Append('/').Append(deck.Model.Count);
            if (_lastPress.Length > 0) _sb.Append("   ").Append(_lastPress);
            var text = _sb.ToString();
            if (_status != null) _status.text = text;
            if (uguiStatus) uguiStatus.text = text;
        }
    }
}
