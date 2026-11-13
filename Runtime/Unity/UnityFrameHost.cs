using UnityEngine;

namespace Threads.UiFrames.Unity
{
    /// <summary>Frame time from Unity's unscaled clock, so frames animate while the game is paused.</summary>
    public sealed class UnityFrameClock : IFrameClock
    {
        public double Now => Time.unscaledTimeAsDouble;
    }

    /// <summary>
    /// The one place UI Frames reads Unity's screen globals. Builds the <see cref="FrameEnvironment"/>
    /// with a Unity clock and the screen's pixels per dp.
    /// </summary>
    public static class UnityFrameHost
    {
        /// <summary>Density of 1 dp: 160 dpi on phones and tablets (Android/iOS convention),
        /// 96 dpi on desktops (Windows' 100 % scale).</summary>
        public static float ReferenceDpi =>
            Application.isMobilePlatform ? 160f : 96f;

        /// <summary>Screen pixels per dp, or 1 when the screen does not report its density.</summary>
        public static float PixelsPerDp()
        {
            var dpi = Screen.dpi;
            return dpi > 0f ? dpi / ReferenceDpi : 1f;
        }

        public static FrameEnvironment CreateEnvironment()
        {
            return new FrameEnvironment(new UnityFrameClock()) { PixelsPerDp = PixelsPerDp(), Warn = Debug.LogWarning };
        }

        /// <summary>Call when the app regains focus or the window moves between screens.</summary>
        public static void Refresh(FrameEnvironment environment) => environment.PixelsPerDp = PixelsPerDp();
    }
}
