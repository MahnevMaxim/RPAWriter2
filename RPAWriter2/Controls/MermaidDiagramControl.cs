using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json;
using RPAWriter.Core.Services;
using RPAWriter2.Services;

namespace RPAWriter2.Controls
{
    /// <summary>
    /// Renders a Mermaid diagram offline using an embedded WebView2 and the bundled mermaid.js.
    /// Falls back to a plain-text code block when the WebView2 runtime is unavailable or rendering fails.
    /// </summary>
    public sealed class MermaidDiagramControl : ContentControl
    {
        private const string MermaidResourceName = "RPAWriter2.Assets.mermaid.min.js";
        private const string MermaidFileName = "mermaid.min.js";
        private const string AssetsHostName = "rpawriter.assets";

        private static readonly Lazy<string> MermaidJsFolder = new Lazy<string>(EnsureMermaidJsOnDisk);

        // Created once per process. Without an explicit user data folder WebView2 defaults to a
        // folder next to the host process (devenv.exe under Program Files), which is not writable
        // and makes CoreWebView2Environment.CreateAsync fail with E_ACCESSDENIED inside the VSIX.
        private static Task<CoreWebView2Environment> _webViewEnvironmentTask;
        private static readonly object WebViewEnvironmentLock = new object();

        private static Task<CoreWebView2Environment> GetWebViewEnvironmentAsync()
        {
            lock (WebViewEnvironmentLock)
            {
                if (_webViewEnvironmentTask == null)
                    _webViewEnvironmentTask = CreateEnvironmentAsync();
                return _webViewEnvironmentTask;
            }
        }

        private readonly string _code;
        private WebView2 _webView;
        private ScrollViewer _scrollOwner;
        private bool _updateScheduled;
        private bool _failed;

        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect);

        public MermaidDiagramControl(string code)
        {
            _code = code ?? string.Empty;
            MinHeight = 120;
            Margin = new Thickness(0, 4, 0, 4);
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        /// <summary>
        /// Copies the embedded mermaid.min.js to a per-machine temp folder once per process and
        /// returns that folder. The WebView2 <c>NavigateToString</c> path can only carry 2 MB of
        /// HTML, so the 3.4 MB script must be served from disk via a virtual host mapping instead
        /// of being inlined. Returns "" when the resource cannot be extracted.
        /// </summary>
        private static string EnsureMermaidJsOnDisk()
        {
            try
            {
                var folder = AppDataPaths.ResolveTemp("Assets");
                Directory.CreateDirectory(folder);
                var file = Path.Combine(folder, MermaidFileName);

                var assembly = Assembly.GetExecutingAssembly();
                using (var stream = assembly.GetManifestResourceStream(MermaidResourceName))
                {
                    if (stream == null)
                    {
                        LogService.Warn("MermaidDiagramControl: embedded resource not found: " + MermaidResourceName);
                        return string.Empty;
                    }

                    using (var output = new FileStream(file, FileMode.Create, FileAccess.Write))
                        stream.CopyTo(output);
                }

                LogService.Info("MermaidDiagramControl: mermaid.min.js extracted to " + file + " (" + new FileInfo(file).Length + " bytes).");
                return folder;
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "MermaidDiagramControl: failed to extract mermaid.min.js.");
                return string.Empty;
            }
        }

        private static async Task<CoreWebView2Environment> CreateEnvironmentAsync()
        {
            var userDataFolder = AppDataPaths.ResolveLocal("WebView2");
            Directory.CreateDirectory(userDataFolder);

            LogService.Info("MermaidDiagramControl: creating WebView2 environment (userDataFolder=" + userDataFolder + ").");
            return await CoreWebView2Environment.CreateAsync(null, userDataFolder, null);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (_scrollOwner != null)
            {
                _scrollOwner.ScrollChanged -= OnScrollOwnerChanged;
                _scrollOwner.SizeChanged -= OnScrollOwnerChanged;
                _scrollOwner = null;
            }

            if (_webView != null)
                _webView.SizeChanged -= OnWebViewSizeChanged;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;
            LogService.Info("MermaidDiagramControl: render requested, code length=" + (_code ?? string.Empty).Length);

            if (string.IsNullOrWhiteSpace(_code))
            {
                LogService.Warn("MermaidDiagramControl: empty diagram code, showing empty fallback.");
                ShowFallback(string.Empty, "empty code");
                return;
            }

            var assetsDir = MermaidJsFolder.Value;
            if (string.IsNullOrWhiteSpace(assetsDir) || !File.Exists(Path.Combine(assetsDir, MermaidFileName)))
            {
                LogService.Warn("MermaidDiagramControl: mermaid.min.js not available on disk (assetsDir=" + (assetsDir ?? "<null>") + "), showing fallback.");
                ShowFallback(_code, "missing mermaid.min.js");
                return;
            }

            try
            {
                LogService.Info("MermaidDiagramControl: creating WebView2 (assetsDir=" + assetsDir + ").");
                _webView = new WebView2
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    MinHeight = 120
                };
                Content = _webView;

                var environment = await GetWebViewEnvironmentAsync();
                await _webView.EnsureCoreWebView2Async(environment);
                LogService.Info("MermaidDiagramControl: CoreWebView2 ready.");

                _scrollOwner = FindParentScrollViewer(this);
                if (_scrollOwner != null)
                {
                    _scrollOwner.ScrollChanged += OnScrollOwnerChanged;
                    _scrollOwner.SizeChanged += OnScrollOwnerChanged;
                }
                _webView.SizeChanged += OnWebViewSizeChanged;
                ScheduleUpdateWindow();

                var settings = _webView.CoreWebView2.Settings;
                settings.AreDefaultContextMenusEnabled = false;
                settings.AreDevToolsEnabled = false;
                settings.IsStatusBarEnabled = false;
                settings.IsZoomControlEnabled = false;
                settings.AreBrowserAcceleratorKeysEnabled = false;
                settings.AreDefaultScriptDialogsEnabled = false;

                _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
                _webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;

                _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    AssetsHostName, assetsDir, CoreWebView2HostResourceAccessKind.DenyCors);
                LogService.Info("MermaidDiagramControl: virtual host '" + AssetsHostName + "' mapped to " + assetsDir + ".");

                var scriptUrl = "http://" + AssetsHostName + "/" + MermaidFileName;
                var html = MermaidHtmlBuilder.Build(scriptUrl, _code, "dark");
                LogService.Info("MermaidDiagramControl: HTML built (" + html.Length + " chars), navigating.");
                _webView.NavigateToString(html);
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "MermaidDiagramControl: failed to render diagram.");
                ShowFallback(_code, "exception: " + ex.Message);
            }
        }

        private void OnNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            try
            {
                LogService.Info("MermaidDiagramControl: navigation completed, IsSuccess=" + e.IsSuccess +
                    ", WebErrorStatus=" + e.WebErrorStatus + ", HttpStatusCode=" + e.HttpStatusCode);
            }
            catch
            {
                // Logging must never crash the control.
            }
        }

        private void OnWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var payload = e.TryGetWebMessageAsString();
                if (string.IsNullOrWhiteSpace(payload))
                {
                    LogService.Info("MermaidDiagramControl: empty web message received.");
                    return;
                }

                LogService.Info("MermaidDiagramControl: web message received: " + payload);

                var size = JsonConvert.DeserializeObject<MermaidSize>(payload);
                if (size == null)
                {
                    LogService.Warn("MermaidDiagramControl: could not deserialize web message.");
                    return;
                }

                // The embedded page forwards mouse-wheel events via postMessage so that the
                // outer WPF ScrollViewer keeps scrolling when the cursor is over the WebView2.
                if (size.Wheel.HasValue)
                {
                    HandleWheel(size.Wheel.Value);
                    return;
                }

                if (!string.IsNullOrEmpty(size.Error))
                {
                    LogService.Warn("MermaidDiagramControl: mermaid.render reported error: " + size.Error);
                    ShowFallback(_code, "mermaid error: " + size.Error);
                    return;
                }

                if (size.W > 0 && size.H > 0)
                {
                    Height = Math.Max(size.H, MinHeight);
                    LogService.Info("MermaidDiagramControl: rendered size w=" + size.W + ", h=" + size.H + ".");
                }
                else
                {
                    LogService.Warn("MermaidDiagramControl: rendered with zero size (w=" + size.W + ", h=" + size.H + ").");
                }
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "MermaidDiagramControl: failed to process web message.");
            }
        }

        /// <summary>
        /// Scrolls the nearest parent <see cref="ScrollViewer"/> by the given wheel delta, so
        /// hovering over the WebView2 still scrolls the surrounding chat/docs list. The WebView2
        /// consumes wheel events at the HWND level, which is why the delta is forwarded from the
        /// embedded page via <c>postMessage</c> instead of relying on WPF routed events.
        /// </summary>
        private void HandleWheel(double deltaY)
        {
            if (Math.Abs(deltaY) < 0.5)
                return;

            var scroll = FindParentScrollViewer(this);
            if (scroll == null)
                return;

            scroll.ScrollToVerticalOffset(scroll.VerticalOffset + deltaY);

            // The scroll offset is already applied at this point, so update the native window
            // synchronously to keep the diagram glued to the text instead of lagging one frame behind.
            UpdateWindowPositionAndClip();
        }

        private static ScrollViewer FindParentScrollViewer(DependencyObject element)
        {
            var parent = VisualTreeHelper.GetParent(element);
            while (parent != null)
            {
                if (parent is ScrollViewer scrollViewer)
                    return scrollViewer;

                parent = VisualTreeHelper.GetParent(parent);
            }

            return null;
        }

        private void OnScrollOwnerChanged(object sender, EventArgs e)
        {
            ScheduleUpdateWindow();
        }

        private void OnWebViewSizeChanged(object sender, SizeChangedEventArgs e)
        {
            ScheduleUpdateWindow();
        }

        private void ScheduleUpdateWindow()
        {
            if (_webView == null || _updateScheduled)
                return;

            _updateScheduled = true;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
            {
                _updateScheduled = false;
                if (IsLoaded)
                    UpdateWindowPositionAndClip();
            }));
        }

        /// <summary>
        /// Keeps the native WebView2 window aligned with its WPF layout position and clipped to the
        /// owning <see cref="ScrollViewer"/> viewport. WebView2 is an HwndHost: it does not follow the
        /// ScrollViewer's render transform nor clip to WPF bounds, so without this it would float
        /// over the surrounding UI while the rest of the content scrolls.
        /// </summary>
        private void UpdateWindowPositionAndClip()
        {
            if (_webView == null || _webView.Handle == IntPtr.Zero)
                return;

            var scroll = _scrollOwner ?? FindParentScrollViewer(this);
            if (scroll == null)
                return;

            if (!(PresentationSource.FromVisual(_webView) is HwndSource source) || source.RootVisual == null)
                return;

            // Child HWNDs are positioned in the root window's client coordinates, so measure
            // everything against the HwndSource root visual and convert to device pixels.
            var toDevice = source.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
            Point ToDevicePixels(Point p) => new Point(p.X * toDevice.M11, p.Y * toDevice.M22);

            var root = source.RootVisual;

            var webTopLeft = ToDevicePixels(_webView.TransformToAncestor(root).Transform(new Point(0, 0)));
            var webBottomRight = ToDevicePixels(_webView.TransformToAncestor(root).Transform(new Point(_webView.ActualWidth, _webView.ActualHeight)));

            var viewportTopLeft = ToDevicePixels(scroll.TransformToAncestor(root).Transform(new Point(0, 0)));
            var viewportBottomRight = ToDevicePixels(scroll.TransformToAncestor(root).Transform(new Point(scroll.ViewportWidth, scroll.ViewportHeight)));

            int webLeft = (int)Math.Round(webTopLeft.X);
            int webTop = (int)Math.Round(webTopLeft.Y);
            int webRight = (int)Math.Round(webBottomRight.X);
            int webBottom = (int)Math.Round(webBottomRight.Y);
            int webWidth = Math.Max(0, webRight - webLeft);
            int webHeight = Math.Max(0, webBottom - webTop);

            // Position the child HWND where WPF laid the element out (fixes "floating" while scrolling).
            SetWindowPos(_webView.Handle, IntPtr.Zero, webLeft, webTop, webWidth, webHeight,
                SwpNoZOrder | SwpNoActivate);

            int visibleLeft = Math.Max(webLeft, (int)Math.Round(viewportTopLeft.X));
            int visibleTop = Math.Max(webTop, (int)Math.Round(viewportTopLeft.Y));
            int visibleRight = Math.Min(webRight, (int)Math.Round(viewportBottomRight.X));
            int visibleBottom = Math.Min(webBottom, (int)Math.Round(viewportBottomRight.Y));

            // Clamp the native window to the visible viewport.
            if (visibleRight <= visibleLeft || visibleBottom <= visibleTop)
            {
                SetWindowRgn(_webView.Handle, CreateRectRgn(0, 0, 0, 0), true);
            }
            else if (visibleLeft <= webLeft && visibleTop <= webTop &&
                     visibleRight >= webRight && visibleBottom >= webBottom)
            {
                SetWindowRgn(_webView.Handle, IntPtr.Zero, true);
            }
            else
            {
                var region = CreateRectRgn(
                    visibleLeft - webLeft,
                    visibleTop - webTop,
                    visibleRight - webLeft,
                    visibleBottom - webTop);
                SetWindowRgn(_webView.Handle, region, true);
            }
        }

        private void ShowFallback(string text, string reason)
        {
            if (_failed)
                return;
            _failed = true;

            LogService.Warn("MermaidDiagramControl: showing text fallback. Reason: " + (reason ?? "unknown"));

            Content = new TextBlock
            {
                Text = text,
                FontFamily = new FontFamily("Consolas"),
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(0xF1, 0xF1, 0xF1)),
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
                Padding = new Thickness(8, 6, 8, 6)
            };
        }

        private sealed class MermaidSize
        {
            public double W { get; set; }
            public double H { get; set; }
            public string Error { get; set; }
            public double? Wheel { get; set; }
        }
    }
}
