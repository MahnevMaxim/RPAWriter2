using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace RPAWriter.Core.Services
{
    /// <summary>A group of changed files belonging to one robot solution or one project.</summary>
    public sealed class CommitGroup
    {
        /// <summary>Robot name (e.g. "RPA-1111") or project name (e.g. "RPAWriter.Core").</summary>
        public string Name { get; set; } = "";

        /// <summary>Folder containing the group, relative to the repository root (forward slashes).</summary>
        public string RelativePath { get; set; } = "";

        /// <summary>Normalized changed-file paths inside this group.</summary>
        public List<string> Files { get; } = new List<string>();
    }

    /// <summary>Result of splitting the working-tree changes into robots, projects and leftovers.</summary>
    public sealed class CommitChangePlan
    {
        public List<CommitGroup> Robots { get; } = new List<CommitGroup>();

        public List<CommitGroup> Projects { get; } = new List<CommitGroup>();

        public List<string> OtherFiles { get; } = new List<string>();
    }

    /// <summary>
    /// Pure, testable commit-message assembly logic. Groups changed files into robot solutions
    /// (a folder RPA-&lt;n&gt; containing RPA-&lt;n&gt;.sln and a RPA-&lt;n&gt;.csproj somewhere inside)
    /// and ordinary projects (walk up until a *.csproj is found). The robot/project/chore prefixes
    /// are always produced programmatically, never by the AI model.
    /// </summary>
    public static class CommitMessageBuilder
    {
        private static readonly HashSet<string> CommitTypes =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "fix", "feat", "chore", "refactor", "docs", "test", "build",
                "ci", "perf", "style", "revert", "hotfix", "wip", "breaking",
            };

        private static readonly Regex ParenTypeColonRegex = new Regex(
            @"^\(\s*(?<t>[A-Za-z]+)\s*\)\s*(?:\([^)]*\))?\s*:\s*",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex BareTypeColonRegex = new Regex(
            @"^(?<t>[A-Za-z]+)\s*(?:\([^)]*\))?\s*:\s*",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex ParenTypeRegex = new Regex(
            @"^\(\s*(?<t>[A-Za-z]+)\s*\)\s*",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex LeadingColonTokenRegex = new Regex(
            @"^([A-Za-z][A-Za-z0-9._-]*)\s*:\s*",
            RegexOptions.CultureInvariant);

        /// <summary>
        /// Cleans a path returned by <c>git status --porcelain</c>: takes the new name of a rename,
        /// unwraps surrounding quotes and normalizes separators to forward slashes.
        /// </summary>
        public static string NormalizeGitPath(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            var path = raw.Trim();
            var arrow = path.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrow >= 0)
                path = path.Substring(arrow + 4).Trim();

            if (path.Length >= 2 && path[0] == '"' && path[path.Length - 1] == '"')
            {
                path = path.Substring(1, path.Length - 2);
                path = path.Replace("\\\"", "\"").Replace("\\\\", "\\");
            }

            return path.Replace('\\', '/');
        }

        /// <summary>True when the directory name looks like a robot solution name (RPA-…).</summary>
        public static bool IsRobotFolderName(string name)
        {
            return !string.IsNullOrEmpty(name)
                && name.Length > 4
                && name.StartsWith("RPA-", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Walks up from the changed file until it finds a robot solution folder: a directory
        /// RPA-&lt;n&gt; that contains RPA-&lt;n&gt;.sln and a RPA-&lt;n&gt;.csproj somewhere inside.
        /// </summary>
        public static CommitGroup FindRobotSolutionFolder(string repoRoot, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(repoRoot) || string.IsNullOrWhiteSpace(relativePath))
                return null;

            var rootFull = Path.GetFullPath(repoRoot);
            var dir = GetStartDirectory(repoRoot, relativePath);

            while (!string.IsNullOrEmpty(dir) && IsUnder(rootFull, dir))
            {
                var name = Path.GetFileName(dir);
                if (IsRobotFolderName(name)
                    && File.Exists(Path.Combine(dir, name + ".sln"))
                    && ContainsRobotProject(dir, name))
                {
                    return new CommitGroup
                    {
                        Name = name,
                        RelativePath = MakeRelative(rootFull, dir),
                    };
                }

                var parent = Path.GetDirectoryName(dir);
                if (parent == null || string.Equals(parent, dir, StringComparison.OrdinalIgnoreCase))
                    break;
                dir = parent;
            }

            return null;
        }

        /// <summary>
        /// Walks up from the changed file until it finds a directory that directly contains a
        /// *.csproj file; the project name is the csproj file name without the extension.
        /// </summary>
        public static CommitGroup FindProjectFolder(string repoRoot, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(repoRoot) || string.IsNullOrWhiteSpace(relativePath))
                return null;

            var rootFull = Path.GetFullPath(repoRoot);
            var dir = GetStartDirectory(repoRoot, relativePath);

            while (!string.IsNullOrEmpty(dir) && IsUnder(rootFull, dir))
            {
                string csproj = null;
                try
                {
                    csproj = Directory.GetFiles(dir, "*.csproj", SearchOption.TopDirectoryOnly)
                        .FirstOrDefault();
                }
                catch
                {
                    // Unreadable directory — keep walking up.
                }

                if (!string.IsNullOrEmpty(csproj))
                {
                    return new CommitGroup
                    {
                        Name = Path.GetFileNameWithoutExtension(csproj),
                        RelativePath = MakeRelative(rootFull, dir),
                    };
                }

                var parent = Path.GetDirectoryName(dir);
                if (parent == null || string.Equals(parent, dir, StringComparison.OrdinalIgnoreCase))
                    break;
                dir = parent;
            }

            return null;
        }

        /// <summary>
        /// Splits changed paths into robot solutions, ordinary projects and files that belong to
        /// neither. Robots and projects are ordered by name; leftover files are ordered by path.
        /// </summary>
        public static CommitChangePlan GroupChangedFiles(string repoRoot, IEnumerable<string> changedPaths)
        {
            var plan = new CommitChangePlan();
            if (changedPaths == null)
                return plan;

            var robots = new Dictionary<string, CommitGroup>(StringComparer.OrdinalIgnoreCase);
            var projects = new Dictionary<string, CommitGroup>(StringComparer.OrdinalIgnoreCase);

            foreach (var raw in changedPaths)
            {
                var rel = NormalizeGitPath(raw);
                if (rel.Length == 0)
                    continue;

                var robot = FindRobotSolutionFolder(repoRoot, rel);
                if (robot != null)
                {
                    if (!robots.TryGetValue(robot.Name, out var group))
                    {
                        group = robot;
                        robots[robot.Name] = group;
                    }
                    group.Files.Add(rel);
                    continue;
                }

                var project = FindProjectFolder(repoRoot, rel);
                if (project != null)
                {
                    if (!projects.TryGetValue(project.Name, out var group))
                    {
                        group = project;
                        projects[project.Name] = group;
                    }
                    group.Files.Add(rel);
                    continue;
                }

                plan.OtherFiles.Add(rel);
            }

            foreach (var group in robots.Values.Concat(projects.Values))
                group.Files.Sort(StringComparer.OrdinalIgnoreCase);

            plan.Robots.AddRange(robots.Values.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase));
            plan.Projects.AddRange(projects.Values.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase));
            plan.OtherFiles.Sort(StringComparer.OrdinalIgnoreCase);
            return plan;
        }

        /// <summary>
        /// Removes anything the AI model may have added around the requested bare summary:
        /// code fences, quotes, a conventional-commit "type(scope):" token and a leading
        /// "Name:" / "RPA-…:" token. The caller then prepends the prefix itself.
        /// </summary>
        public static string SanitizeSummary(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            var s = raw.Trim();
            s = StripCodeFences(s);
            s = TrimWrappingQuotes(s);
            s = StripCommitTypePrefix(s);
            s = StripLeadingColonToken(s);
            s = TrimWrappingQuotes(s);
            return s.Trim();
        }

        /// <summary>Builds "RPA-&lt;n&gt;: summary" with the prefix added exactly once.</summary>
        public static string FormatRobotLine(string name, string summary)
        {
            var clean = SanitizeSummary(summary);
            if (clean.Length == 0)
                clean = "update";
            return name + ": " + clean;
        }

        /// <summary>Builds "ProjectName: summary" with the prefix added exactly once.</summary>
        public static string FormatProjectLine(string name, string summary)
        {
            var clean = SanitizeSummary(summary);
            if (clean.Length == 0)
                clean = "update";
            return name + ": " + clean;
        }

        /// <summary>Builds "chore: summary" with the prefix added exactly once.</summary>
        public static string FormatChoreLine(string summary)
        {
            var clean = SanitizeSummary(summary);
            if (clean.Length == 0)
                clean = "update";
            return "chore: " + clean;
        }

        /// <summary>Joins non-empty lines with the environment newline.</summary>
        public static string JoinLines(IEnumerable<string> lines)
        {
            if (lines == null)
                return string.Empty;
            return string.Join(Environment.NewLine, lines.Where(l => !string.IsNullOrWhiteSpace(l)));
        }

        private static string GetStartDirectory(string repoRoot, string relativePath)
        {
            var fullPath = Path.GetFullPath(Path.IsPathRooted(relativePath)
                ? relativePath
                : Path.Combine(repoRoot, relativePath));
            return Directory.Exists(fullPath) ? fullPath : Path.GetDirectoryName(fullPath);
        }

        private static bool ContainsRobotProject(string dir, string name)
        {
            try
            {
                return SearchForFile(dir, name + ".csproj");
            }
            catch
            {
                return false;
            }
        }

        private static bool SearchForFile(string dir, string fileName)
        {
            if (string.IsNullOrEmpty(dir))
                return false;

            try
            {
                if (File.Exists(Path.Combine(dir, fileName)))
                    return true;
            }
            catch
            {
                return false;
            }

            try
            {
                foreach (var sub in Directory.GetDirectories(dir))
                {
                    var subName = Path.GetFileName(sub);
                    if (subName == ".git" || subName == ".vs" || subName == "bin" || subName == "obj")
                        continue;
                    if (SearchForFile(sub, fileName))
                        return true;
                }
            }
            catch
            {
                // Unreadable directory.
            }

            return false;
        }

        private static string StripCodeFences(string s)
        {
            var lines = s.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n').ToList();
            while (lines.Count > 0 && lines[0].Trim().StartsWith("```", StringComparison.Ordinal))
                lines.RemoveAt(0);
            while (lines.Count > 0 && lines[lines.Count - 1].Trim() == "```")
                lines.RemoveAt(lines.Count - 1);
            return string.Join(" ", lines.Select(l => l.Trim())).Trim();
        }

        private static string TrimWrappingQuotes(string s)
        {
            s = s.Trim();
            while (s.Length >= 2
                   && ((s[0] == '"' && s[s.Length - 1] == '"')
                       || (s[0] == '\'' && s[s.Length - 1] == '\'')
                       || (s[0] == '`' && s[s.Length - 1] == '`')))
            {
                s = s.Substring(1, s.Length - 2).Trim();
            }
            return s;
        }

        private static string StripCommitTypePrefix(string s)
        {
            var changed = true;
            while (changed)
            {
                changed = false;

                var trimmed = s.TrimStart();
                var leading = s.Substring(0, s.Length - trimmed.Length);
                var body = trimmed;

                var match = ParenTypeColonRegex.Match(body);
                if (!match.Success)
                    match = BareTypeColonRegex.Match(body);
                if (!match.Success)
                    match = ParenTypeRegex.Match(body);

                if (match.Success && IsCommitType(match.Groups["t"].Value))
                {
                    s = leading + body.Substring(match.Length);
                    changed = true;
                }
            }
            return s;
        }

        private static string StripLeadingColonToken(string s)
        {
            var changed = true;
            while (changed)
            {
                changed = false;

                var trimmed = s.TrimStart();
                var leading = s.Substring(0, s.Length - trimmed.Length);
                var body = trimmed;

                var match = LeadingColonTokenRegex.Match(body);
                if (match.Success)
                {
                    s = leading + body.Substring(match.Length);
                    changed = true;
                }
            }
            return s;
        }

        private static bool IsCommitType(string type)
        {
            return !string.IsNullOrEmpty(type) && CommitTypes.Contains(type);
        }

        private static bool IsUnder(string root, string path)
        {
            var rootFull = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var pathFull = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return string.Equals(rootFull, pathFull, StringComparison.OrdinalIgnoreCase)
                || pathFull.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static string MakeRelative(string root, string full)
        {
            var rootFull = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullPath = Path.GetFullPath(full);

            if (string.Equals(rootFull, fullPath, StringComparison.OrdinalIgnoreCase))
                return ".";

            if (fullPath.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return fullPath.Substring(rootFull.Length + 1).Replace('\\', '/');

            return fullPath.Replace('\\', '/');
        }
    }
}
