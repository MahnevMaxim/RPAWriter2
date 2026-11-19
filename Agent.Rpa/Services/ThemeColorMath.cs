namespace Agent.Rpa.Services
{
    /// <summary>
    /// Pure color math used to classify a background color as a light or dark theme.
    /// Kept free of WPF, WinForms and Visual Studio dependencies so it can be unit-tested
    /// without a UI runtime.
    /// </summary>
    public static class ThemeColorMath
    {
        /// <summary>
        /// Returns <c>true</c> when the RGB background reads as a light theme.
        /// Uses ITU-R BT.601 luma (0.299 R + 0.587 G + 0.114 B), the same perceived-brightness
        /// weights used for contrast decisions; values at or above 0.5 are treated as light.
        /// </summary>
        public static bool IsLight(byte r, byte g, byte b)
        {
            double luma = (0.299 * r + 0.587 * g + 0.114 * b) / 255.0;
            return luma >= 0.5;
        }
    }
}
