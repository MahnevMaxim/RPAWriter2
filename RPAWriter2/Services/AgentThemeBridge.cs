using Agent.Models;
using Agent.Rpa.Services;
using Agent.Toolkit.Wpf.Theming;
using Microsoft.VisualStudio.PlatformUI;

namespace RPAWriter2.Services
{
    /// <summary>
    /// Bridges Visual Studio's color theme to the shared AgentToolkit theme.
    /// <para>
    /// In <see cref="AgentThemeMode.Auto"/> the host owns the toolkit theme, so this bridge
    /// subscribes to <see cref="VSColorTheme.ThemeChanged"/> and forwards the resolved Dark/Light
    /// value to <see cref="AgentThemeManager"/>. The SettingsControl "Theme" combo only reflects
    /// the current theme in this mode and never overrides the host.
    /// </para>
    /// </summary>
    public static class AgentThemeBridge
    {
        private static bool _initialized;

        /// <summary>
        /// Starts syncing the toolkit theme with the Visual Studio theme. Idempotent; call once
        /// from package initialization on the UI thread.
        /// </summary>
        public static void Initialize()
        {
            if (_initialized)
                return;
            _initialized = true;

            AgentThemeManager.SetMode(AgentThemeMode.Auto);
            VSColorTheme.ThemeChanged += OnThemeChanged;
            ApplyCurrentVsTheme();
        }

        private static void OnThemeChanged(ThemeChangedEventArgs e)
        {
            ApplyCurrentVsTheme();
        }

        private static void ApplyCurrentVsTheme()
        {
            var background = VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowBackgroundColorKey);
            bool light = ThemeColorMath.IsLight(background.R, background.G, background.B);
            AgentThemeManager.Apply(light ? "Light" : "Dark");
        }
    }
}
