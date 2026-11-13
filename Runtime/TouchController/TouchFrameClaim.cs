#if THREADS_UF_TOUCHCONTROLLER
using System;
using Threads.TouchController;
using Threads.TouchController.Intentions;

namespace Threads.UiFrames.TouchInput
{
    /// <summary>
    /// Keeps game intentions off fingers that are on UI frames: an Exclusive intention, bound to
    /// <see cref="PressGesture"/>, that claims every finger that goes down on a frame (or a
    /// drawer's handle). Joystick frames claim their own fingers and are left out. The UI still
    /// gets the touch: UI events read the device directly, not intentions.
    /// </summary>
    public sealed class TouchFrameClaim : IDisposable
    {
        readonly FrameSet _frames;

        /// <param name="priority">Above game intentions (0) and below joysticks (1000) by default.</param>
        public TouchFrameClaim(IntentionSystem intentions, FrameSet frames, int priority = 500)
        {
            if (intentions == null) throw new ArgumentNullException(nameof(intentions));
            _frames = frames ?? throw new ArgumentNullException(nameof(frames));
            Intention = new Intention("UI Frames", intentions)
                .Bind<PressGesture>()
                .InArea(new FuncArea(Accepts))
                .WithPriority(priority)
                .Exclusive();
            Intention.Enabled = true;
        }

        public Intention Intention { get; }

        /// <summary>Whether a finger at <paramref name="point"/> is on a frame that takes touch.</summary>
        public bool Accepts(IntentionPoint point)
        {
            foreach (var frame in _frames.Frames)
            {
                if (frame is JoystickFrame || !frame.Style.Accepts(FrameInputKinds.Touch)) continue;
                if (FrameScreen.Contains(frame.Element, point.PositionPx, point.ScreenSizePx)) return true;
                if (frame is DrawerFrame d && d.Handle != null && FrameScreen.Contains(d.Handle, point.PositionPx, point.ScreenSizePx)) return true;
            }
            return false;
        }

        public void Dispose() => Intention.Enabled = false;
    }
}
#endif
