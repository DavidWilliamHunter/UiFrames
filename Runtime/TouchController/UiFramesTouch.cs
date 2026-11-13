#if THREADS_UF_TOUCHCONTROLLER
using Threads.TouchController.Intentions;
using Threads.UiFrames.Input;
using UnityEngine;

namespace Threads.UiFrames.TouchInput
{
    /// <summary>
    /// Connects UI Frames to TouchController. Touch still reaches frames through the UI systems
    /// (so buttons, sliders and scroll views keep their own touch behaviour); this adds:
    /// <list type="bullet">
    /// <item>The platform's touch slop and density (with OS accessibility values) for frame drags.</item>
    /// <item>A claim on fingers that go down on frames, so game intentions do not also act on them
    /// (<see cref="TouchFrameClaim"/>).</item>
    /// <item><c>.uf-joystick</c> frames driven by TouchController joysticks (<see cref="JoystickKind"/>).</item>
    /// </list>
    /// Put it next to a <see cref="UiFramesRunner"/> and an <see cref="IntentionRunner"/>. It attaches
    /// once both run, and again if either restarts.
    /// </summary>
    [AddComponentMenu("UI Frames/UI Frames Touch")]
    [DefaultExecutionOrder(-40)] // after IntentionRunner (-100) and UiFramesRunner (-50)
    public sealed class UiFramesTouch : MonoBehaviour
    {
        public UiFramesRunner frames;
        public IntentionRunner touch;
        [Tooltip("Claim fingers that go down on frames, so game intentions never get them.")]
        public bool claimFingers = true;
        [Tooltip("Above game intentions (0), below joysticks (1000).")]
        public int claimPriority = 500;
        [Tooltip("Register the uf-joystick frame kind.")]
        public bool joysticks = true;

        IntentionSystem _attached;
        FrameRegistry _registry;
        TouchFrameClaim _claim;
        JoystickKind _joystickKind;

        public bool IsAttached => _attached != null;
        public TouchFrameClaim Claim => _claim;

        void Reset()
        {
            frames = GetComponent<UiFramesRunner>();
            touch = GetComponent<IntentionRunner>();
        }

        void Update()
        {
            var ready = frames && touch && frames.IsRunning && touch.IsRunning;
            if (!ready) { Detach(); return; }
            if (_attached != touch.Intentions || _registry != frames.Registry)
            {
                Detach();
                Attach();
            }
            // Every frame: cheap, and the values can change (focus regained, another screen), and
            // the frames runner refreshes its own density on focus too.
            ApplySettings();
        }

        void OnDisable() => Detach();

        void Attach()
        {
            _attached = touch.Intentions;
            _registry = frames.Registry;
            ApplySettings();
            if (claimFingers) _claim = new TouchFrameClaim(_attached, frames.Frames, claimPriority);
            if (joysticks)
            {
                var runner = touch;
                _joystickKind = new JoystickKind(() => runner.IsRunning ? runner.Intentions : null, () => runner.TouchSticks);
                _registry.Add(_joystickKind); // the binders rescan and create joystick frames
            }
        }

        void Detach()
        {
            _claim?.Dispose();
            _claim = null;
            if (_joystickKind != null && _registry != null && _registry.FindKind(JoystickKind.Marker) == _joystickKind)
                _registry.Remove(JoystickKind.Marker);
            _joystickKind = null;
            _attached = null;
            _registry = null;
        }

        /// <summary>Slop and density from the running TouchSystem, so frames and gestures agree.</summary>
        void ApplySettings()
        {
            var env = frames.Environment;
            var t = touch.Touch;
            env.PixelsPerDp = t.Environment.PixelsPerDp;
            env.DragSlopDp = t.Settings.Data.Motion.touchSlopDp;
        }
    }
}
#endif
