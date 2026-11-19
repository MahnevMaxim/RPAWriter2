using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Agent.Core.Models;
using Material.Icons;

namespace RPAWriter2.Controls
{
    /// <summary>
    /// Domain "Controls" gallery tab: sample notifications, sample chat transcript, SVG icon search
    /// and the color palette. Fully self-contained view; "send to chat input" is forwarded to the
    /// shell through <see cref="SendToInputRequested"/>.
    /// </summary>
    public partial class ControlsGalleryControl : UserControl
    {
        private readonly ObservableCollection<MessageSample> _messageSamples = new ObservableCollection<MessageSample>();
        private readonly DispatcherTimer _notificationAutoHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private ICollectionView? _svgView;

        public ControlsGalleryControl()
        {
            InitializeComponent();
            _notificationAutoHideTimer.Tick += OnNotificationAutoHideTick;
            ControlsInnerTabs.SelectionChanged += OnControlsInnerTabsSelectionChanged;
        }

        /// <summary>Set by the shell: true when the host Controls tab is visible and selected.</summary>
        public bool IsHostTabVisible { get; set; }

        /// <summary>Raised when a gallery sample asks to append text to the chat input.</summary>
        public event EventHandler<string>? SendToInputRequested;

        /// <summary>Fills the gallery with sample data for every chat message type and svg icon.</summary>
        public void PopulateGallery()
        {
            ControlsChatList.ItemsSource = CreateSampleChatMessages();
            ControlsMessagesList.ItemsSource = _messageSamples;
            RebuildMessageSamples(ParseNotificationAutoHideSeconds());

            _svgView = CollectionViewSource.GetDefaultView(CreateSvgSamples());
            _svgView.Filter = FilterSvgIcon;
            ControlsSvgList.ItemsSource = _svgView;

            var palettes = new ListCollectionView(CreatePaletteSamples());
            palettes.GroupDescriptions.Add(new PropertyGroupDescription(nameof(PaletteSample.Group)));
            ControlsPaletteList.ItemsSource = palettes;
        }

        private void RebuildMessageSamples(int autoHideSeconds)
        {
            _notificationAutoHideTimer.Stop();
            _messageSamples.Clear();
            foreach (var sample in CreateMessageSamples(autoHideSeconds))
                _messageSamples.Add(sample);
        }

        private void StartNotificationAutoHideDemo()
        {
            _notificationAutoHideTimer.Stop();
            if (AnyAutoHide())
                _notificationAutoHideTimer.Start();
        }

        private bool AnyAutoHide()
        {
            foreach (var s in _messageSamples)
                if (s.AutoHideSeconds > 0)
                    return true;
            return false;
        }

        private int ParseNotificationAutoHideSeconds()
        {
            if (int.TryParse((NotificationAutoHideBox?.Text ?? string.Empty).Trim(), out var value) && value >= 0)
                return value;
            return 5;
        }

        private void ReplayNotificationSamples()
        {
            RebuildMessageSamples(ParseNotificationAutoHideSeconds());
            StartNotificationAutoHideDemo();
        }

        private void OnReplayNotifications(object sender, RoutedEventArgs e)
        {
            ReplayNotificationSamples();
        }

        private void OnNotificationAutoHideTick(object? sender, EventArgs e)
        {
            bool anyPending = false;
            for (int i = _messageSamples.Count - 1; i >= 0; i--)
            {
                var sample = _messageSamples[i];
                if (sample.AutoHideSeconds <= 0)
                    continue;

                sample.RemainingSeconds--;
                if (sample.RemainingSeconds <= 0)
                    _messageSamples.RemoveAt(i);
                else
                    anyPending = true;
            }

            if (!anyPending)
                _notificationAutoHideTimer.Stop();
        }

        private void OnControlsInnerTabsSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsNotificationGalleryVisible())
                ReplayNotificationSamples();
        }

        private bool IsNotificationGalleryVisible()
        {
            return IsHostTabVisible
                && ControlsInnerTabs != null
                && NotificationsTab != null
                && ReferenceEquals(ControlsInnerTabs.SelectedItem, NotificationsTab);
        }

        private void OnCopyMessageSample(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: MessageSample sample } && !string.IsNullOrEmpty(sample.Text))
            {
                try { Clipboard.SetText(sample.Text); }
                catch { /* clipboard busy — ignore */ }
            }
        }

        private void OnSendMessageSample(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: MessageSample sample } && !string.IsNullOrWhiteSpace(sample.Text))
                SendToInputRequested?.Invoke(this, sample.Text);
        }

        private bool FilterSvgIcon(object item)
        {
            var query = (SvgSearchBox?.Text ?? string.Empty).Trim();
            if (query.Length == 0)
                return true;

            return item is SvgIconSample sample &&
                   sample.Identifier.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void OnSvgSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            _svgView?.Refresh();
        }

        private void OnCopySvgIdentifier(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: SvgIconSample sample })
            {
                try { Clipboard.SetText(sample.Identifier); }
                catch { /* clipboard busy — ignore */ }
            }
        }

        private void OnSendSvgIdentifier(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: SvgIconSample sample })
                SendToInputRequested?.Invoke(this, sample.Identifier);
        }

        private static List<SvgIconSample> CreateSvgSamples()
        {
            var samples = new List<SvgIconSample>();
            foreach (MaterialIconKind kind in Enum.GetValues(typeof(MaterialIconKind)))
                samples.Add(new SvgIconSample { Kind = kind });
            return samples;
        }

        private static List<PaletteSample> CreatePaletteSamples()
        {
            return new List<PaletteSample>
            {
                new PaletteSample { Group = "Mode", Name = "Plan", Hex = "#64B5F6", Brush = Rgb(0x64, 0xB5, 0xF6) },
                new PaletteSample { Group = "Mode", Name = "Act", Hex = "#4CAF50", Brush = Rgb(0x4C, 0xAF, 0x50) },
                new PaletteSample { Group = "Mode", Name = "Help", Hex = "#F44336", Brush = Rgb(0xF4, 0x43, 0x36) },

                new PaletteSample { Group = "Buttons", Name = "Button background", Hex = "#333337", Brush = Rgb(0x33, 0x33, 0x37) },
                new PaletteSample { Group = "Buttons", Name = "Button border", Hex = "#3F3F46", Brush = Rgb(0x3F, 0x3F, 0x46) },
                new PaletteSample { Group = "Buttons", Name = "Button hover", Hex = "#3E3E42", Brush = Rgb(0x3E, 0x3E, 0x42) },
                new PaletteSample { Group = "Buttons", Name = "Button pressed", Hex = "#264F78", Brush = Rgb(0x26, 0x4F, 0x78) },
                new PaletteSample { Group = "Buttons", Name = "Primary / accent", Hex = "#264F78", Brush = Rgb(0x26, 0x4F, 0x78) },
                new PaletteSample { Group = "Buttons", Name = "Icon button", Hex = "Transparent", Brush = Brushes.Transparent },

                new PaletteSample { Group = "Theme", Name = "Window", Hex = "#2D2D30", Brush = Rgb(0x2D, 0x2D, 0x30) },
                new PaletteSample { Group = "Theme", Name = "Panel", Hex = "#252526", Brush = Rgb(0x25, 0x25, 0x26) },
                new PaletteSample { Group = "Theme", Name = "Control", Hex = "#333337", Brush = Rgb(0x33, 0x33, 0x37) },
                new PaletteSample { Group = "Theme", Name = "Border", Hex = "#3F3F46", Brush = Rgb(0x3F, 0x3F, 0x46) },
                new PaletteSample { Group = "Theme", Name = "Text", Hex = "#F1F1F1", Brush = Rgb(0xF1, 0xF1, 0xF1) },
                new PaletteSample { Group = "Theme", Name = "Error text", Hex = "#F48771", Brush = Rgb(0xF4, 0x87, 0x71) },

                new PaletteSample { Group = "Chat", Name = "User border", Hex = "#CC64B5F6", Brush = Argb(0xCC, 0x64, 0xB5, 0xF6) },
                new PaletteSample { Group = "Chat", Name = "User background", Hex = "#3364B5F6", Brush = Argb(0x33, 0x64, 0xB5, 0xF6) },
                new PaletteSample { Group = "Chat", Name = "Last act border", Hex = "#4CAF50", Brush = Rgb(0x4C, 0xAF, 0x50) },
                new PaletteSample { Group = "Chat", Name = "Last act background", Hex = "#334CAF50", Brush = Argb(0x33, 0x4C, 0xAF, 0x50) },
                new PaletteSample { Group = "Chat", Name = "Last plan border", Hex = "#FF8C00", Brush = Rgb(0xFF, 0x8C, 0x00) },
                new PaletteSample { Group = "Chat", Name = "Last plan background", Hex = "#4DFF8C00", Brush = Argb(0x4D, 0xFF, 0x8C, 0x00) },

                new PaletteSample { Group = "Notifications", Name = "Trace", Hex = "#FFFFFF", Brush = Brushes.White },
                new PaletteSample { Group = "Notifications", Name = "Info", Hex = "#64B5F6", Brush = Rgb(0x64, 0xB5, 0xF6) },
                new PaletteSample { Group = "Notifications", Name = "Success", Hex = "#4CAF50", Brush = Rgb(0x4C, 0xAF, 0x50) },
                new PaletteSample { Group = "Notifications", Name = "Warning", Hex = "#FF8C00", Brush = Rgb(0xFF, 0x8C, 0x00) },
                new PaletteSample { Group = "Notifications", Name = "Error", Hex = "#F44336", Brush = Rgb(0xF4, 0x43, 0x36) },
            };
        }

        private static List<ChatMessage> CreateSampleChatMessages()
        {
            return new List<ChatMessage>
            {
                new ChatMessage { Kind = ChatMessageKind.User, Text = "Import the spec and generate a robot for SAP transaction MM01." },
                new ChatMessage
                {
                    Kind = ChatMessageKind.AgentText,
                    Text = "Plan:\n\n- Read the specification\n- Design the workflow\n- Generate the XAML and C# helpers\n\n```csharp\nvar robot = new Robot();\n```",
                    Reasoning = "The user wants a robot generated from an imported spec. I will decompose it into workflow steps first."
                },
                new ChatMessage { Kind = ChatMessageKind.ToolCall, Payload = new ToolCallPayload { ToolName = "read_file", Result = "FILE: spec.docx (ok, 2340 chars)" } },
                new ChatMessage { Kind = ChatMessageKind.ToolBlocked, Payload = new ToolCallPayload { ToolName = "generate_robot" } },
                new ChatMessage { Kind = ChatMessageKind.Error, Text = "Failed to reach the AI provider: request timed out after 300 s." },
                new ChatMessage { Kind = ChatMessageKind.SystemNotice, Text = "Knowledge base directory is not configured." },
                new ChatMessage { Kind = ChatMessageKind.Compaction, Text = "Conversation compacted: 12 earlier messages summarized." },
                new ChatMessage { Kind = ChatMessageKind.ModeSwitch, Mode = "Act", Text = "Mode: Act" },
                new ChatMessage
                {
                    Kind = ChatMessageKind.Question,
                    Payload = new QuestionPayload
                    {
                        Question = "Which robot template should I use?",
                        Options = new List<string> { "SAP", "Web", "Excel" }
                    }
                },
                new ChatMessage
                {
                    Kind = ChatMessageKind.Subagent,
                    Payload = new SubagentPayload
                    {
                        Item = new SubagentStatusItem
                        {
                            Prompt = "Parse the UiPath XAML and reconstruct the specification.",
                            Status = "completed",
                            ToolCalls = 3,
                            LatestToolCall = "read_file",
                            Result = "Reconstructed 5 tasks."
                        }
                    }
                },
                new ChatMessage
                {
                    Kind = ChatMessageKind.ResultAnswerPlan,
                    Mode = "Plan",
                    Text = "## Plan\n\n- Read the specification\n- Design the workflow\n- Generate the XAML and C# helpers\n\n```csharp\nvar robot = new Robot();\n```"
                },
                new ChatMessage
                {
                    Kind = ChatMessageKind.ResultAnswerAct,
                    Mode = "Act",
                    Text = "## Done\n\nCreated the robot project and generated the XAML files."
                },
                new ChatMessage
                {
                    Kind = ChatMessageKind.DiffEdit,
                    Text = "--- a/Robot.xaml\n+++ b/Robot.xaml\n@@ -1,3 +1,3 @@\n- <Sequence>\n+ <Sequence DisplayName=\"Main\">"
                }
            };
        }

        private static List<MessageSample> CreateMessageSamples(int autoHideSeconds)
        {
            var blue = Rgb(0x64, 0xB5, 0xF6);
            var green = Rgb(0x4C, 0xAF, 0x50);
            var orange = Rgb(0xFF, 0x8C, 0x00);
            var red = Rgb(0xF4, 0x43, 0x36);
            var samples = new List<MessageSample>();
            AddMessageSamples(samples, "trace", Brushes.White, Brushes.White, new Thickness(1), autoHideSeconds, showSend: false);
            AddMessageSamples(samples, "info", blue, blue, new Thickness(1), autoHideSeconds, showSend: false);
            AddMessageSamples(samples, "success", green, green, new Thickness(1), autoHideSeconds, showSend: false);
            AddMessageSamples(samples, "warning", orange, orange, new Thickness(1), autoHideSeconds, showSend: true);
            AddMessageSamples(samples, "error", red, red, new Thickness(1), autoHideSeconds, showSend: true);
            return samples;
        }

        private static void AddMessageSamples(List<MessageSample> samples, string colorName, Brush accent, Brush foreground, Thickness borderThickness, int autoHideSeconds, bool showSend)
        {
            samples.Add(new MessageSample { Text = "notification-" + colorName + "-copy-close", Accent = accent, Foreground = foreground, BorderThickness = borderThickness, ShowCopy = true, ShowSend = showSend, ShowClose = true, AutoHideSeconds = autoHideSeconds, RemainingSeconds = autoHideSeconds });
            samples.Add(new MessageSample { Text = "notification-" + colorName + "-copy", Accent = accent, Foreground = foreground, BorderThickness = borderThickness, ShowCopy = true, ShowSend = showSend, ShowClose = false, AutoHideSeconds = autoHideSeconds, RemainingSeconds = autoHideSeconds });
            samples.Add(new MessageSample { Text = "notification-" + colorName + "-close", Accent = accent, Foreground = foreground, BorderThickness = borderThickness, ShowCopy = false, ShowSend = showSend, ShowClose = true, AutoHideSeconds = autoHideSeconds, RemainingSeconds = autoHideSeconds });
            samples.Add(new MessageSample { Text = "notification-" + colorName, Accent = accent, Foreground = foreground, BorderThickness = borderThickness, ShowCopy = false, ShowSend = showSend, ShowClose = false, AutoHideSeconds = autoHideSeconds, RemainingSeconds = autoHideSeconds });
        }

        private static SolidColorBrush Rgb(byte r, byte g, byte b)
        {
            return new SolidColorBrush(Color.FromRgb(r, g, b));
        }

        private static SolidColorBrush Argb(byte a, byte r, byte g, byte b)
        {
            return new SolidColorBrush(Color.FromArgb(a, r, g, b));
        }
    }

    /// <summary>Demo item for the Controls -> Notifications gallery.</summary>
    public sealed class MessageSample
    {
        public string Text { get; set; }
        public Brush Accent { get; set; }
        public Brush Foreground { get; set; }
        public Thickness BorderThickness { get; set; } = new Thickness(1);
        public bool ShowClose { get; set; }
        public bool ShowCopy { get; set; }
        public bool ShowSend { get; set; }

        /// <summary>Seconds after which the demo notification disappears; 0 means it never disappears.</summary>
        public int AutoHideSeconds { get; set; }

        /// <summary>Internal countdown for the auto-hide demo (not bound to UI).</summary>
        public int RemainingSeconds { get; set; }
    }

    /// <summary>Demo item for the Controls -> svg gallery.</summary>
    public sealed class SvgIconSample
    {
        public MaterialIconKind Kind { get; set; }

        public string Identifier => Kind.ToString();
    }

    /// <summary>Demo item for the Controls -> palette gallery.</summary>
    public sealed class PaletteSample
    {
        public string Group { get; set; } = "";
        public string Name { get; set; } = "";
        public string Hex { get; set; } = "";
        public Brush Brush { get; set; } = Brushes.Transparent;
    }
}
