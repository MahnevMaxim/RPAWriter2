using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using RPAWriter.Core.Services;

namespace RPAWriter2.Services
{
    /// <summary>Result of creating a robot solution from a template.</summary>
    public class RobotCreateResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string? SolutionPath { get; set; }
    }

    /// <summary>
    /// Creates a robot solution from an installed solution template (the template name is taken from settings).
    /// Solution name = "RPA-&lt;number&gt;". Only works when no solution is currently open.
    /// </summary>
    public class RobotTemplateService
    {
        /// <summary>GUID of the Package Manager Console window (PowerConsoleToolWindow).</summary>
        private static readonly Guid PowerConsoleWindowGuid = new Guid("0AD07096-BBA9-4900-A651-0598D26F6D24");

        /// <summary>How long to wait for the Package Manager Console PowerShell host to become ready.</summary>
        private static readonly TimeSpan PowerConsoleReadyTimeout = TimeSpan.FromSeconds(30);

        /// <summary>Number of projects expected in the solution after template expansion.</summary>
        private const int ExpectedProjectCount = 2;

        /// <summary>How long to wait for all solution projects to appear.</summary>
        private static readonly TimeSpan ProjectWaitTimeout = TimeSpan.FromSeconds(120);

        /// <summary>EnvDTE solution-folder Kind.</summary>
        private const string SolutionFolderKind = "{66A26722-8FB5-11D2-AA7E-00C04F688DDE}";

        public async Task<RobotCreateResult> CreateRobotAsync(
            string solutionName, string rootPath, string templateName,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            var dte = Package.GetGlobalService(typeof(SDTE)) as DTE2;
            if (dte == null)
                return Fail("Could not access Visual Studio (DTE).");

            var solution = dte.Solution;
            if (solution == null)
                return Fail("Could not access the solution (Solution).");
            if (solution.IsOpen)
                return Fail("Close the open solution first.");

            var root = ResolveRoot(dte, rootPath);
            var destDir = Path.Combine(root, solutionName);
            var slnFile = Path.Combine(destDir, solutionName + ".sln");

            // Guard against overwriting an existing solution.
            if (Directory.Exists(destDir) || File.Exists(slnFile))
                return Fail("Solution \"" + solutionName + "\" already exists: " + destDir);

            if (string.IsNullOrWhiteSpace(templateName))
                return Fail("Robot template name is not set (setting \"Robot template file name\").");

            var name = templateName.Trim();
            Report(progress, "Searching for template \"" + name + "\"…");

            var templatePath = FindTemplate(name, progress);
            if (templatePath == null)
                return Fail("Solution template \"" + name + "\" not found. Install the template and retry.");

            var vstPath = ResolveVstemplate(templatePath, progress);
            if (vstPath == null)
                return Fail("No .vstemplate file found in template \"" + name + "\".");

            try
            {
                Directory.CreateDirectory(destDir);
                solution.Create(destDir, solutionName);
                Report(progress, "Creating project from template: " + vstPath);
                solution.AddFromTemplate(vstPath, destDir, solutionName, false);
                solution.SaveAs(slnFile);
            }
            catch (Exception ex)
            {
                return Fail("Failed to create the solution from the template: " + ex.Message);
            }

            // Wait for the template to fully expand in the solution (2 projects by default);
            // otherwise reinstall may not reach every project.
            await WaitForProjectsAsync(dte, ExpectedProjectCount, progress, cancellationToken);

            // Reinstall NuGet packages after the solution is created.
            var message = "Solution created.";
            try
            {
                await RunPackageManagerCommandAsync("Update-Package -Reinstall", progress, cancellationToken);
            }
            catch (Exception ex)
            {
                message += " Update-Package -Reinstall failed: " + ex.Message;
            }
            return new RobotCreateResult { Success = true, Message = message, SolutionPath = slnFile };
        }

        private static RobotCreateResult Fail(string message) =>
            new RobotCreateResult { Success = false, Message = message };

        private static string ResolveRoot(DTE2 dte, string rootPath)
        {
            if (!string.IsNullOrWhiteSpace(rootPath))
                return rootPath.Trim();

            // Try to read the "default project location" from VS.
            try
            {
                var props = dte?.get_Properties("Environment", "ProjectsAndSolution");
                var value = props?.Item("ProjectsLocation")?.Value?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                    return value!;
            }
            catch
            {
                // The property may be unavailable until the options page has been opened.
            }

            // Fallback: standard VS 2017+ directory.
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "source", "repos");
        }

        private static string? FindTemplate(string name, IProgress<string>? progress)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) return null;

            var roots = new List<string>();

            // User templates: %USERPROFILE%\Documents\Visual Studio <ver>\Templates\ProjectTemplates
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            foreach (var vsDir in SafeEnumerateDirectories(docs, "Visual Studio*"))
            {
                var pt = Path.Combine(vsDir, "Templates", "ProjectTemplates");
                if (Directory.Exists(pt)) roots.Add(pt);
            }

            // Templates from the VS installation: <VSInstall>\Common7\IDE\ProjectTemplates
            var dte = Package.GetGlobalService(typeof(SDTE)) as DTE2;
            var devenvDir = Path.GetDirectoryName(dte?.FullName ?? "");
            if (!string.IsNullOrEmpty(devenvDir))
            {
                var ide = Path.Combine(devenvDir, "ProjectTemplates");
                if (Directory.Exists(ide)) roots.Add(ide);
            }

            Report(progress, "Search directories: " + (roots.Count == 0 ? "none found" : string.Join("; ", roots)));

            // The name may be given with an extension (.zip/.vstemplate) or without.
            var hasExtension = name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                               || name.EndsWith(".vstemplate", StringComparison.OrdinalIgnoreCase);
            var baseName = hasExtension ? Path.GetFileNameWithoutExtension(name) : name;

            foreach (var root in roots)
            {
                Report(progress, "Checking directory: " + root);

                // 1) Direct template file (.vstemplate or .zip) with a matching name (recursively).
                foreach (var f in SafeEnumerateFiles(root, "*.vstemplate", SearchOption.AllDirectories))
                    if (MatchesName(f, name, baseName, hasExtension))
                    {
                        Report(progress, "Template file found: " + f);
                        return f;
                    }
                foreach (var f in SafeEnumerateFiles(root, "*.zip", SearchOption.AllDirectories))
                    if (MatchesName(f, name, baseName, hasExtension))
                    {
                        Report(progress, "Template file found: " + f);
                        return f;
                    }

                // 2) Directory named after the template, containing a .vstemplate (or .zip).
                foreach (var dir in SafeEnumerateDirectories(root, baseName, SearchOption.AllDirectories))
                {
                    foreach (var vst in SafeEnumerateFiles(dir, "*.vstemplate", SearchOption.TopDirectoryOnly))
                    {
                        Report(progress, "Template file found: " + vst);
                        return vst;
                    }
                    foreach (var zip in SafeEnumerateFiles(dir, "*.zip", SearchOption.TopDirectoryOnly))
                    {
                        Report(progress, "Template file found: " + zip);
                        return zip;
                    }
                }
            }
            return null;
        }

        private static void Report(IProgress<string>? progress, string message)
        {
            progress?.Report(message);
        }

        /// <summary>
        /// Returns the path to the .vstemplate file. If a template archive (.zip) is found,
        /// extracts it to a temp directory and returns the .vstemplate inside.
        /// </summary>
        private static string? ResolveVstemplate(string templatePath, IProgress<string>? progress)
        {
            if (templatePath.EndsWith(".vstemplate", StringComparison.OrdinalIgnoreCase))
                return templatePath;

            if (!templatePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                return null;

            Report(progress, "Template is packed in .zip, extracting: " + templatePath);

            var extractDir = AppDataPaths.ResolveTemp("Templates", Path.GetFileNameWithoutExtension(templatePath));
            try
            {
                if (Directory.Exists(extractDir))
                    Directory.Delete(extractDir, true);
                Directory.CreateDirectory(extractDir);
                ZipFile.ExtractToDirectory(templatePath, extractDir);

                var vst = Directory.EnumerateFiles(extractDir, "*.vstemplate", SearchOption.AllDirectories)
                                   .FirstOrDefault();
                if (vst != null)
                    Report(progress, "Extracted .vstemplate file: " + vst);
                return vst;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Runs a command in the Package Manager Console (e.g. "Update-Package -Reinstall").
        /// The primary path is the canonical IPowerConsoleService (PMC window), which properly
        /// queues the command while the PowerShell host is still initializing. If the service
        /// cannot be obtained, restore commands fall back to a child-process MSBuild restore
        /// (never blocking the UI thread); other commands raise a descriptive error.
        /// </summary>
        public async Task RunPackageManagerCommandAsync(
            string command, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            // Use the DTE of this very VS instance (not the "active" object via GetActiveObject),
            // so the command does not leak into another open Visual Studio instance.
            var dte = Package.GetGlobalService(typeof(SDTE)) as DTE2
                ?? throw new InvalidOperationException("Could not access Visual Studio (DTE).");

            // Activate the VS main window so the console is guaranteed to be in the foreground.
            try { dte.MainWindow?.Activate(); }
            catch { /* window may have been unavailable */ }

            // Open/initialize the Package Manager Console.
            try { dte.ExecuteCommand("View.PackageManagerConsole"); }
            catch { /* window may already be open */ }

            Report(progress, "Waiting for the Package Manager Console to be ready…");

            // Primary path: canonical IPowerConsoleService (PMC window).
            if (await TrySendViaPowerConsoleAsync(command, cancellationToken))
            {
                Report(progress, "Command sent via IPowerConsoleService: " + command);
                return;
            }

            // Fallback: restore packages via a child process so the UI thread is never
            // blocked by the Package Manager Console PowerShell host ("Initializing PowerShell host…").
            Report(progress, "IPowerConsoleService unavailable — restoring packages via child process (fallback).");
            if (!IsRestoreCommand(command))
            {
                throw new InvalidOperationException(
                    "The Package Manager Console host is unavailable. Run the command manually in " +
                    "View → Package Manager Console: " + command);
            }
            await RestorePackagesViaProcessAsync(progress, cancellationToken);
            Report(progress, "Packages restored via child process (fallback).");
        }

        private static bool IsRestoreCommand(string command)
        {
            var trimmed = (command ?? string.Empty).TrimStart();
            return trimmed.StartsWith("Update-Package -Reinstall", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Restores packages of the open solution via MSBuild /t:Restore in a child process.
        /// Unlike the Package Manager Console, this never blocks the UI thread and has a timeout.
        /// </summary>
        private async Task RestorePackagesViaProcessAsync(IProgress<string>? progress, CancellationToken cancellationToken)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            var dte = Package.GetGlobalService(typeof(SDTE)) as DTE2
                ?? throw new InvalidOperationException("Could not access Visual Studio (DTE).");
            string? solutionFile = null;
            try { solutionFile = dte.Solution?.FullName; }
            catch { solutionFile = null; }
            if (string.IsNullOrWhiteSpace(solutionFile) || !File.Exists(solutionFile))
                throw new InvalidOperationException("Could not determine the solution file for package restore.");
            var msbuild = LocateMsBuild(dte);
            if (msbuild == null)
                throw new InvalidOperationException("Could not locate MSBuild.exe to restore packages.");
            var args = "\"" + solutionFile + "\" /t:Restore /p:RestorePackagesConfig=true /nologo /v:minimal";
            Report(progress, "Restoring packages via " + Path.GetFileName(msbuild) + "…");
            var shell = new ShellCommandService();
            var result = await shell.RunFileAsync(
                msbuild,
                args,
                Path.GetDirectoryName(solutionFile) ?? string.Empty,
                cancellationToken,
                timeoutSeconds: 600);
            if (!result.Success)
                throw new InvalidOperationException("Package restore failed: " + result.Output);
        }

        /// <summary>Locates MSBuild.exe relative to the Visual Studio installation (devenv.exe).</summary>
        private static string? LocateMsBuild(DTE2 dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var devenvDir = Path.GetDirectoryName(dte?.FullName ?? string.Empty);
            if (string.IsNullOrEmpty(devenvDir))
                return null;
            // devenvDir = <VSInstall>\Common7\IDE → msbuild = <VSInstall>\MSBuild\Current\Bin\MSBuild.exe
            var candidates = new[]
            {
                Path.Combine(devenvDir, "..", "..", "MSBuild", "Current", "Bin", "MSBuild.exe"),
                Path.Combine(devenvDir, "..", "..", "MSBuild", "15.0", "Bin", "MSBuild.exe")
            };
            foreach (var candidate in candidates)
            {
                string full;
                try { full = Path.GetFullPath(candidate); }
                catch { continue; }
                if (File.Exists(full))
                    return full;
            }
            return null;
        }

        /// <summary>
        /// Attempts to send a command via the canonical IPowerConsoleService (PMC window).
        /// Waits for the PowerShell host to initialize and returns true if the command is accepted.
        /// </summary>
        private static async Task<bool> TrySendViaPowerConsoleAsync(string command, CancellationToken cancellationToken)
        {
            var deadline = DateTime.UtcNow + PowerConsoleReadyTimeout;

            while (true)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                var console = ResolvePowerConsoleWindow();
                if (console == null)
                {
                    if (DateTime.UtcNow > deadline)
                        return false;
                    await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var execute = console.GetType().GetMethod(
                    "Execute",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new[] { typeof(string), typeof(object[]) },
                    null);
                if (execute == null)
                    return false;

                var isInitialized = console.GetType().GetMethod(
                    "IsHostSuccessfullyInitialized",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    Type.EmptyTypes,
                    null);

                bool ready = true;
                if (isInitialized != null)
                {
                    try { ready = (bool)(isInitialized.Invoke(console, null) ?? false); }
                    catch { ready = false; }
                }

                if (ready)
                {
                    bool accepted;
                    try
                    {
                        accepted = (bool)(execute.Invoke(console, new object[] { command, new object[0] }) ?? false);
                    }
                    catch
                    {
                        return false;
                    }

                    if (accepted)
                        return true;
                }

                if (DateTime.UtcNow > deadline)
                    return false;

                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>Locates the Package Manager Console window and returns its object (IPowerConsoleService).</summary>
        private static object? ResolvePowerConsoleWindow()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var uiShell = Package.GetGlobalService(typeof(SVsUIShell)) as IVsUIShell;
                if (uiShell == null)
                    return null;

                var slot = PowerConsoleWindowGuid;
                IVsWindowFrame? frame = null;
                int hr = uiShell.FindToolWindow((uint)__VSFINDTOOLWIN.FTW_fFindFirst, ref slot, out frame);
                if (hr != 0 || frame == null) // 0 = S_OK
                    return null;

                object? docView;
                hr = frame.GetProperty((int)__VSFPROPID.VSFPROPID_DocView, out docView);
                if (hr != 0 || docView == null) // 0 = S_OK
                    return null;

                return docView;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Waits until the expected number of projects appears in the solution (the template
        /// always expands a fixed number of projects).
        /// </summary>
        private static async Task WaitForProjectsAsync(
            DTE2 dte, int expectedCount, IProgress<string>? progress, CancellationToken cancellationToken)
        {
            var deadline = DateTime.UtcNow + ProjectWaitTimeout;
            int lastCount = -1;

            while (true)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                int count;
                try
                {
                    count = CountProjects(dte.Solution);
                }
                catch
                {
                    count = 0;
                }

                if (count != lastCount)
                {
                    Report(progress, "Waiting for solution projects to be created… (" + count + "/" + expectedCount + ")");
                    lastCount = count;
                }

                if (count >= expectedCount)
                {
                    Report(progress, "Solution projects created: " + count + ".");
                    return;
                }

                if (DateTime.UtcNow > deadline)
                {
                    Report(progress, "Did not get all solution projects (" + count + "/" + expectedCount +
                        "), continuing with what is available.");
                    return;
                }

                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>Recursively counts projects in the solution (including solution folders).</summary>
        private static int CountProjects(EnvDTE.Solution solution)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (solution == null || solution.Projects == null)
                return 0;

            int count = 0;
            foreach (EnvDTE.Project project in solution.Projects)
                count += CountProjects(project);
            return count;
        }

        private static int CountProjects(EnvDTE.Project project)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (project == null)
                return 0;

            if (string.Equals(project.Kind, SolutionFolderKind, StringComparison.OrdinalIgnoreCase))
            {
                int count = 0;
                if (project.ProjectItems != null)
                {
                    foreach (EnvDTE.ProjectItem item in project.ProjectItems)
                    {
                        EnvDTE.Project? sub = null;
                        try { sub = item.SubProject; }
                        catch { sub = null; }
                        if (sub != null)
                            count += CountProjects(sub);
                    }
                }
                return count;
            }

            return 1;
        }

        private static bool MatchesName(string filePath, string name, string baseName, bool hasExtension)
        {
            if (hasExtension)
                return string.Equals(Path.GetFileName(filePath), name, StringComparison.OrdinalIgnoreCase);
            return string.Equals(Path.GetFileNameWithoutExtension(filePath), baseName, StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> SafeEnumerateDirectories(string path, string pattern, SearchOption option = SearchOption.TopDirectoryOnly)
        {
            try { return Directory.EnumerateDirectories(path, pattern, option); }
            catch { return new string[0]; }
        }

        private static IEnumerable<string> SafeEnumerateFiles(string path, string pattern, SearchOption option = SearchOption.TopDirectoryOnly)
        {
            try { return Directory.EnumerateFiles(path, pattern, option); }
            catch { return new string[0]; }
        }
    }
}
