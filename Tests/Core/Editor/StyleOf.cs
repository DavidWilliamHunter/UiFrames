namespace Threads.UiFrames.Tests
{
    static class StyleOf
    {
        public static FrameStyle With<T>(FrameProperty<T> property, T value)
        {
            var s = new FrameStyle();
            s.Set(property, value);
            return s;
        }

        public static FrameStyle And<T>(this FrameStyle s, FrameProperty<T> property, T value)
        {
            s.Set(property, value);
            return s;
        }
    }
}
