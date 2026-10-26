using System;
using System.IO;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Single source of truth for the extension's per-user data directory.
    ///
    /// Visual Studio 2022 (17.x) and Visual Studio 2026 (18.x) can be installed side by side,
    /// so persisted state must not be shared between them. All state is therefore stored under
    /// <c>%AppData%\RPAWriter2\&lt;version&gt;</c>, where <c>&lt;version&gt;</c> is the Visual
    /// Studio major version ("17" or "18") reported by the host.
    /// </summary>
    public static class AppDataPaths
    {
        private static readonly string AppData =
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        private static readonly string LocalAppData =
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        private static readonly string TempRoot = Path.Combine(Path.GetTempPath(), "RPAWriter2");

        /// <summary>
        /// Major Visual Studio version the extension is running in ("17" or "18").
        /// Empty until the VSIX host initializes it; an empty value falls back to the
        /// legacy unversioned folder so tests and early startup keep working.
        /// </summary>
        public static string VersionScope { get; private set; } = "";

        /// <summary>Legacy shared root, used before per-version separation and for migration.</summary>
        public static string Root => Path.Combine(AppData, "RPAWriter2");

        /// <summary>Current versioned root (<c>%AppData%\RPAWriter2\&lt;version&gt;</c>).</summary>
        public static string ScopedRoot =>
            string.IsNullOrWhiteSpace(VersionScope) ? Root : Path.Combine(Root, VersionScope);

        /// <summary>Legacy shared local root (<c>%LocalAppData%\RPAWriter2</c>).</summary>
        public static string LocalRoot => Path.Combine(LocalAppData, "RPAWriter2");

        /// <summary>Current versioned local root (<c>%LocalAppData%\RPAWriter2\&lt;version&gt;</c>).</summary>
        public static string LocalScopedRoot =>
            string.IsNullOrWhiteSpace(VersionScope) ? LocalRoot : Path.Combine(LocalRoot, VersionScope);

        /// <summary>Current versioned temp root (<c>%Temp%\RPAWriter2\&lt;version&gt;</c>).</summary>
        public static string TempScopedRoot =>
            string.IsNullOrWhiteSpace(VersionScope) ? TempRoot : Path.Combine(TempRoot, VersionScope);

        /// <summary>Sets the version scope once from the host's DTE version string (e.g. "17.13.2").</summary>
        public static void InitializeScope(string vsVersion)
        {
            if (!string.IsNullOrWhiteSpace(VersionScope))
                return;

            if (!string.IsNullOrWhiteSpace(vsVersion))
            {
                var parts = vsVersion.Split('.');
                if (parts.Length > 0 && int.TryParse(parts[0], out var major) && major > 0)
                {
                    VersionScope = major.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    return;
                }
            }

            VersionScope = "";
        }

        /// <summary>Builds a path under the versioned roaming root.</summary>
        public static string Resolve(params string[] segments) => CombinePath(ScopedRoot, segments);

        /// <summary>Builds a path under the versioned local root.</summary>
        public static string ResolveLocal(params string[] segments) => CombinePath(LocalScopedRoot, segments);

        /// <summary>Builds a path under the versioned temp root.</summary>
        public static string ResolveTemp(params string[] segments) => CombinePath(TempScopedRoot, segments);

        private static string CombinePath(string root, string[] segments)
        {
            var path = root;
            foreach (var segment in segments)
                path = Path.Combine(path, segment);
            return path;
        }

        /// <summary>
        /// Copies legacy data from the shared <c>%AppData%\RPAWriter2</c> folder into the current
        /// versioned folder. Runs once per version: files and folders are copied only when the
        /// destination does not already exist, and version-scope subfolders ("17"/"18") are skipped.
        /// </summary>
        public static void MigrateLegacyData()
        {
            if (string.IsNullOrWhiteSpace(VersionScope))
                return;

            var legacy = Root;
            var scoped = ScopedRoot;

            if (!Directory.Exists(legacy))
                return;

            Directory.CreateDirectory(scoped);

            foreach (var file in Directory.GetFiles(legacy))
            {
                var destination = Path.Combine(scoped, Path.GetFileName(file));
                if (!File.Exists(destination))
                {
                    try { File.Copy(file, destination); }
                    catch { /* migration is best-effort */ }
                }
            }

            foreach (var directory in Directory.GetDirectories(legacy))
            {
                var name = Path.GetFileName(directory);
                if (IsVersionScopeFolder(name))
                    continue;

                var destination = Path.Combine(scoped, name);
                if (!Directory.Exists(destination))
                {
                    try { CopyDirectory(directory, destination); }
                    catch { /* migration is best-effort */ }
                }
            }
        }

        private static bool IsVersionScopeFolder(string name)
        {
            return int.TryParse(name, out _);
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.GetFiles(source))
            {
                var target = Path.Combine(destination, Path.GetFileName(file));
                if (!File.Exists(target))
                    File.Copy(file, target);
            }
            foreach (var directory in Directory.GetDirectories(source))
                CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }
}
