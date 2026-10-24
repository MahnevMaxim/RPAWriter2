using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using Microsoft.Win32;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;
using RPAWriter2.Controls;
using RPAWriter2.Services;

namespace RPAWriter2
{
    /// <summary>
    /// Robot converter UI hosted by the Converter tool window: UiPath/PIX → WF conversion,
    /// activity-library import and the PIX → MWF mapping table.
    /// </summary>
    public partial class ConverterToolWindowControl : UserControl
    {
        private readonly ObservableCollection<ChatMessage> _converterMessages = new ObservableCollection<ChatMessage>();

        public ConverterToolWindowControl()
        {
            InitializeComponent();

            ConverterMessages.Messages = _converterMessages;
            ConverterMessages.CopyMessageRequested += OnMessageCopyRequested;
            PixMwfMapControl.CloseRequested += OnPixMwfMapClosed;
            PixMwfMapControl.Log = AppendConverterText;

            var settings = SettingsStore.Load();
            PixMwfMapControl.AiProvider = AiProviderFactory.Create(settings);
        }

        private void AppendConverterMessage(ChatMessage message)
        {
            // Route through the dispatcher queue so appends from background tasks keep FIFO order.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ConverterMessages.Append(message);
                CopyConverterAllButton.Visibility = Visibility.Visible;
            }));
        }

        private void AppendConverterText(string text)
        {
            AppendConverterMessage(new ChatMessage { Kind = ChatMessageKind.SystemNotice, Text = text });
        }

        private void SetConverterOutput(string text)
        {
            AppendConverterText(text);
        }

        private void ShowError(Exception ex)
        {
            if (ex == null) return;
            LogService.Error(ex);
            AppendConverterText(ex.Message);
            AppendConverterText(ex.ToString());
        }

        private void OnMessageCopyRequested(object? sender, ChatMessage message)
        {
            try { Clipboard.SetText(FormatMessageForCopy(message)); }
            catch { /* clipboard busy — ignore */ }
        }

        private static string FormatMessageForCopy(ChatMessage message)
        {
            var sb = new StringBuilder();
            sb.Append(message.DisplayText);
            if (!string.IsNullOrWhiteSpace(message.Reasoning))
            {
                sb.AppendLine();
                sb.AppendLine("[Reasoning]");
                sb.Append(message.Reasoning);
            }
            return sb.ToString();
        }

        private void OnCopyConverterOutput(object sender, RoutedEventArgs e)
        {
            var sb = new StringBuilder();
            foreach (var m in _converterMessages)
            {
                if (sb.Length > 0) sb.AppendLine().AppendLine();
                sb.Append(FormatMessageForCopy(m));
            }
            if (sb.Length == 0) return;
            try { Clipboard.SetText(sb.ToString()); }
            catch { /* clipboard busy — ignore */ }
        }

        private void OnClearConverter(object sender, RoutedEventArgs e)
        {
            _converterMessages.Clear();
            CopyConverterAllButton.Visibility = Visibility.Collapsed;
        }

        private void OnOpenPixMwfMap(object sender, RoutedEventArgs e)
        {
            PixMwfMapControl.LoadData();
            ShowPixMwfMap();
        }

        private void ShowPixMwfMap()
        {
            PixMwfMapHost.Visibility = Visibility.Visible;
            PixMwfMapSplitter.Visibility = Visibility.Visible;
            PixMwfMapRow.MinHeight = 120;
            PixMwfMapRow.Height = new GridLength(380);
        }

        private void OnPixMwfMapClosed(object sender, EventArgs e)
        {
            PixMwfMapHost.Visibility = Visibility.Collapsed;
            PixMwfMapSplitter.Visibility = Visibility.Collapsed;
            PixMwfMapRow.MinHeight = 0;
            PixMwfMapRow.Height = new GridLength(0);
        }

        private void OnConvertUiPath(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "UiPath robot",
                Filter = "UiPath workflow (*.xaml)|*.xaml|All files (*.*)|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var xaml = File.ReadAllText(dlg.FileName);
                var result = new UiPathToWfConverter(new RobotGenerator()).Convert(xaml);
                var dir = Path.GetDirectoryName(dlg.FileName);
                var baseName = Path.GetFileNameWithoutExtension(dlg.FileName);
                File.WriteAllText(Path.Combine(dir, baseName + ".wf.xaml"), result.Xaml);
                File.WriteAllText(Path.Combine(dir, baseName + ".wf.cs"), result.CSharpCode);
                SetConverterOutput(result.Xaml + "\n\n--- C# ---\n\n" + result.CSharpCode);
                AppendConverterText("Conversion completed. Warnings: " + result.Warnings.Count);
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private void OnConvertPix(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "PIX robot",
                Filter = "PIX robot (*.pix)|*.pix|All files (*.*)|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var pix = File.ReadAllText(dlg.FileName);
                var mappings = PixMwfMapControl.Store.GetAll();
                var result = new PixToWfConverter(new PixActivityLibrary(), new WfSnippetLibrary(), mappings).Convert(pix);

                var solutionDir = GetOpenSolutionDirectory();
                var outDir = solutionDir != null
                    ? Path.Combine(solutionDir, "temp")
                    : Path.GetDirectoryName(dlg.FileName);
                Directory.CreateDirectory(outDir);

                var baseName = Path.GetFileNameWithoutExtension(dlg.FileName);
                var xamlPath = Path.Combine(outDir, baseName + ".wf.xaml");
                var csPath = Path.Combine(outDir, baseName + ".wf.cs");
                var reportPath = Path.Combine(outDir, baseName + ".wf.txt");

                File.WriteAllText(xamlPath, result.Xaml);
                File.WriteAllText(csPath, result.CSharpCode);

                var sb = new StringBuilder();
                sb.AppendLine("PIX to WF conversion report");
                sb.AppendLine("Source: " + dlg.FileName);
                sb.AppendLine("Output XAML: " + xamlPath);
                sb.AppendLine("Output C#: " + csPath);
                sb.AppendLine("Warnings (" + result.Warnings.Count + "):");
                foreach (var w in result.Warnings)
                    sb.AppendLine("- " + w);
                sb.AppendLine("Applied mappings (" + result.Mappings.Count + "):");
                foreach (var m in result.Mappings)
                    sb.AppendLine("- " + m);
                File.WriteAllText(reportPath, sb.ToString());

                SetConverterOutput(result.Xaml + "\n\n--- C# ---\n\n" + result.CSharpCode);
                AppendConverterText("Conversion completed → " + outDir + ". Warnings: " + result.Warnings.Count);
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private async void OnGetAllActivitiesFromPix(object sender, RoutedEventArgs e)
        {
            var folder = PickPixRobotsFolder();
            if (folder == null) return;

            var solutionDir = GetOpenSolutionDirectory();

            try
            {
                AppendConverterText("Started scanning PIX robots folder: " + folder);

                var result = await Task.Run(() => ScanPixRobots(folder));

                AppendConverterText("Finished scanning. Robot files: " + result.FileCount +
                                    ", distinct activities: " + result.Occurrences.Count +
                                    ", parse errors: " + result.ParseErrors.Count + ".");

                var library = new PixActivityLibrary();
                var known = library.GetKnownActivityNames();
                var missing = result.Occurrences
                    .Where(kv => !known.Contains(kv.Key))
                    .OrderByDescending(kv => kv.Value)
                    .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // Append activities used by the robots but not yet known to the library, so
                // they show up in the PIX → MWF mapping table and can be mapped.
                var added = 0;
                foreach (var activity in result.Occurrences.Keys)
                {
                    if (known.Contains(activity))
                        continue;

                    library.EnsureDescriptor(new PixActivityDescriptor
                    {
                        ActivityFullName = activity,
                        DefaultDisplayName = activity,
                        Role = "statement"
                    });
                    added++;
                }

                AppendConverterText(BuildPixScanSummaryText(folder, result, missing));
                if (added > 0)
                    AppendConverterText("Appended " + added + " new activity descriptor(s) to the PIX library.");

                foreach (var m in missing)
                    LogService.Warn("Missing PIX activity: " + m.Key + " (occurrences: " + m.Value + ")");
                LogService.Info("PIX activity scan completed: " + result.FileCount + " files, " +
                                result.Occurrences.Count + " distinct activities, " + missing.Count + " missing.");

                var reportPath = WritePixScanReport(solutionDir, folder, BuildPixScanReportMarkdown(folder, result, missing));
                if (reportPath != null)
                {
                    AppendConverterText("Report written to: " + reportPath);
                    LogService.Info("PIX activities report written to: " + reportPath);
                }

                // Show the mapping table filtered to only the activities found in this folder.
                PixMwfMapControl.LoadData();
                PixMwfMapControl.SetFolderActivitiesFilter(result.Occurrences.Keys);
                ShowPixMwfMap();
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }

        private async void OnImportPixActivities(object sender, RoutedEventArgs e)
        {
            var pixDir = ResolvePixInstallDirectory();
            if (pixDir == null)
            {
                AppendConverterText("Import cancelled: PIX installation folder was not selected.");
                return;
            }

            var targetDir = ActivityLibraryPaths.ResolvePixActivitiesDirectory();
            var sourceDir = TryFindSourcePixLibrary(GetOpenSolutionDirectory());

            try
            {
                AppendConverterText("Started importing PIX activities from: " + pixDir);
                AppendConverterText("Runtime library target: " + targetDir);
                if (sourceDir != null && !string.Equals(sourceDir, targetDir, StringComparison.OrdinalIgnoreCase))
                    AppendConverterText("Source library target: " + sourceDir);

                var result = await Task.Run(() =>
                    new PixActivityLibraryImporter().Import(pixDir, targetDir, AppendConverterText));

                var summary = BuildImportSummary(pixDir, targetDir, result);
                AppendConverterText(summary);
                LogService.Info("PIX activity import: " + result.ActivitiesImported + " activities, " +
                                result.FilesWritten + " files written, " + result.Errors.Count + " errors.");

                if (sourceDir != null && !string.Equals(sourceDir, targetDir, StringComparison.OrdinalIgnoreCase))
                {
                    var sourceResult = await Task.Run(() =>
                        new PixActivityLibraryImporter().Import(pixDir, sourceDir, AppendConverterText));
                    AppendConverterText(BuildImportSummary(pixDir, sourceDir, sourceResult));
                    LogService.Info("PIX activity import (source mirror): " + sourceResult.ActivitiesImported + " activities.");
                }
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }

        private async void OnGetAllMwfActivities(object sender, RoutedEventArgs e)
        {
            var activitiesDir = PickMwfActivitiesFolder();
            if (activitiesDir == null)
            {
                AppendConverterText("Get All MWF Activities cancelled: activities folder was not selected.");
                return;
            }

            var targetDir = ActivityLibraryPaths.ResolveMwfActivitiesDirectory();

            try
            {
                AppendConverterText("Started importing MWF activities from: " + activitiesDir);
                AppendConverterText("Runtime library target: " + targetDir);

                var result = await Task.Run(() =>
                    new MwfActivityLibraryImporter().Import(activitiesDir, targetDir, AppendConverterText));

                AppendConverterText(BuildMwfImportSummary(activitiesDir, targetDir, result));
                LogService.Info("MWF activity import: " + result.ActivitiesImported + " activities, " +
                                result.FilesWritten + " files written, " + result.Errors.Count + " errors.");
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }

        private string? PickMwfActivitiesFolder()
        {
            var owner = GetHostWindowHandle();
            return FolderPicker.PickFolder("Select the folder that contains Workflow Foundation activities", string.Empty, owner);
        }

        private static string BuildMwfImportSummary(string activitiesDir, string targetDir, WfActivityLibraryImportResult result)
        {
            var sb = new StringBuilder();
            sb.AppendLine("MWF activity import finished.");
            sb.AppendLine("Source: " + activitiesDir);
            sb.AppendLine("Target: " + targetDir);
            sb.AppendLine("Assemblies scanned: " + result.DllsScanned);
            sb.AppendLine("Activities imported: " + result.ActivitiesImported);
            sb.AppendLine("Descriptor files written: " + result.FilesWritten);
            sb.AppendLine("Obsolete files deleted: " + result.FilesDeleted);
            if (result.Errors.Count > 0)
            {
                sb.AppendLine("Errors (" + result.Errors.Count + "):");
                foreach (var error in result.Errors)
                    sb.AppendLine("- " + error);
            }
            return sb.ToString().TrimEnd();
        }

        private static string BuildImportSummary(string pixDir, string targetDir, PixActivityLibraryImportResult result)
        {
            var sb = new StringBuilder();
            sb.AppendLine("PIX activity import finished.");
            sb.AppendLine("Source: " + pixDir);
            sb.AppendLine("Target: " + targetDir);
            sb.AppendLine("Assemblies scanned: " + result.DllsScanned);
            sb.AppendLine("Activities imported: " + result.ActivitiesImported);
            sb.AppendLine("Descriptor files written: " + result.FilesWritten);
            sb.AppendLine("Obsolete files deleted: " + result.FilesDeleted);
            if (result.Errors.Count > 0)
            {
                sb.AppendLine("Errors (" + result.Errors.Count + "):");
                foreach (var error in result.Errors)
                    sb.AppendLine("- " + error);
            }
            return sb.ToString().TrimEnd();
        }

        private string? ResolvePixInstallDirectory()
        {
            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PIX"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "PIX")
            };

            foreach (var candidate in candidates)
                if (Directory.Exists(candidate))
                    return candidate;

            return FolderPicker.PickFolder(
                "Select the PIX installation folder",
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                GetHostWindowHandle());
        }

        private static string? TryFindSourcePixLibrary(string? solutionDir)
        {
            if (string.IsNullOrWhiteSpace(solutionDir))
                return null;

            var candidate = Path.Combine(solutionDir, "RPAWriter2", "ActivityLibrary", "pix");
            return Directory.Exists(candidate) ? candidate : null;
        }

        private string? PickPixRobotsFolder()
        {
            var owner = GetHostWindowHandle();
            var configured = (SettingsStore.Load().RobotsRootPath ?? "").Trim();
            var initial = !string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured)
                ? configured
                : string.Empty;

            return FolderPicker.PickFolder("Select the folder that contains PIX robots", initial, owner);
        }

        private IntPtr GetHostWindowHandle()
        {
            try
            {
                var window = System.Windows.Window.GetWindow(this);
                if (window != null)
                    return new System.Windows.Interop.WindowInteropHelper(window).Handle;
            }
            catch
            {
                // Fall through to IntPtr.Zero (no owner).
            }
            return IntPtr.Zero;
        }

        private string? GetOpenSolutionDirectory()
        {
            try
            {
                var dte = Package.GetGlobalService(typeof(SDTE)) as DTE2;
                if (dte?.Solution == null || !dte.Solution.IsOpen)
                    return null;
                var slnPath = dte.Solution.FullName;
                return string.IsNullOrWhiteSpace(slnPath) ? null : Path.GetDirectoryName(slnPath);
            }
            catch
            {
                return null;
            }
        }

        private PixScanResult ScanPixRobots(string folder)
        {
            var result = new PixScanResult();
            var files = SafeEnumeratePixFiles(folder).ToList();
            result.FileCount = files.Count;

            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                AppendConverterText("Parsing " + fileName + " ...");
                try
                {
                    var doc = XDocument.Load(file);
                    var count = 0;
                    foreach (var element in doc.Descendants())
                    {
                        if (element.Name.LocalName != "_activityFullName") continue;
                        var value = element.Value.Trim();
                        if (value.Length == 0) continue;
                        result.Occurrences[value] = result.Occurrences.TryGetValue(value, out var c) ? c + 1 : 1;
                        count++;
                    }
                    AppendConverterText("Parsed " + fileName + " — " + count + " activities.");
                }
                catch (Exception ex)
                {
                    result.ParseErrors.Add(fileName + ": " + ex.Message);
                    AppendConverterText("Failed to parse " + fileName + ".");
                    LogService.Warn("Failed to parse PIX robot file " + file + ": " + ex.Message);
                }
            }

            return result;
        }

        private static IEnumerable<string> SafeEnumeratePixFiles(string root)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                yield break;

            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var dir = stack.Pop();

                string[] files;
                try { files = Directory.GetFiles(dir, "*.pix"); }
                catch { files = new string[0]; }
                foreach (var f in files) yield return f;

                string[] subdirs;
                try { subdirs = Directory.GetDirectories(dir); }
                catch { subdirs = new string[0]; }
                foreach (var s in subdirs) stack.Push(s);
            }
        }

        private static string BuildPixScanSummaryText(string folder, PixScanResult result, List<KeyValuePair<string, int>> missing)
        {
            var sb = new StringBuilder();
            sb.AppendLine("PIX activity scan complete.");
            sb.AppendLine("Folder: " + folder);
            sb.AppendLine("Robot files scanned: " + result.FileCount);
            sb.AppendLine("Distinct activities found: " + result.Occurrences.Count);
            sb.AppendLine("Missing activities: " + missing.Count);
            if (missing.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Missing activities (rating = occurrences across all robots):");
                foreach (var m in missing)
                    sb.AppendLine("- " + m.Key + " — " + m.Value);
            }
            return sb.ToString().TrimEnd();
        }

        private static string BuildPixScanReportMarkdown(string folder, PixScanResult result, List<KeyValuePair<string, int>> missing)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# PIX activities missing from the converter library");
            sb.AppendLine();
            sb.AppendLine("- Scanned folder: " + folder);
            sb.AppendLine("- Robot files scanned: " + result.FileCount);
            sb.AppendLine("- Distinct activities found: " + result.Occurrences.Count);
            sb.AppendLine("- Missing activities: " + missing.Count);
            if (result.ParseErrors.Count > 0)
            {
                sb.AppendLine("- Parse errors: " + result.ParseErrors.Count);
                foreach (var err in result.ParseErrors)
                    sb.AppendLine("  - " + err);
            }
            sb.AppendLine();
            if (missing.Count == 0)
            {
                sb.AppendLine("All activities are already present in the PIX-MWF converter library.");
                return sb.ToString();
            }

            sb.AppendLine("## Missing activities");
            sb.AppendLine();
            sb.AppendLine("Rating = total number of occurrences across all PIX robots.");
            sb.AppendLine();
            sb.AppendLine("| Activity | Rating |");
            sb.AppendLine("| --- | ---: |");
            foreach (var m in missing)
                sb.AppendLine("| " + m.Key + " | " + m.Value + " |");
            return sb.ToString();
        }

        private static string? WritePixScanReport(string? solutionDir, string robotsFolder, string report)
        {
            var outDir = !string.IsNullOrWhiteSpace(solutionDir)
                ? Path.Combine(solutionDir, "temp")
                : Path.Combine(robotsFolder, "temp");

            try
            {
                Directory.CreateDirectory(outDir);
                var path = Path.Combine(outDir, "PixMissingActivities.md");
                File.WriteAllText(path, report, Encoding.UTF8);
                return path;
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "Failed to write PIX activities report.");
                return null;
            }
        }

        private sealed class PixScanResult
        {
            public int FileCount;
            public readonly Dictionary<string, int> Occurrences = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            public readonly List<string> ParseErrors = new List<string>();
        }
    }
}
