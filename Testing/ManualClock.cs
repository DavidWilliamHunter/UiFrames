namespace Threads.UiFrames.Testing
{
    /// <summary>A clock tests move by hand, so animations can be stepped through time.</summary>
    public sealed class ManualClock : IFrameClock
    {
        public double Now { get; set; }

        public void Advance(float seconds) => Now += seconds;
        public void AdvanceMs(float ms) => Now += ms / 1000.0;
    }
}
