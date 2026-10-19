using System;
using System.IO;
using System.Text;

namespace RPAWriter.Core.Services
{
    /// <summary>Core memory-bank file names, per the Memory Bank methodology.</summary>
    public static class MemoryBankFiles
    {
        public static readonly string[] Core =
        {
            "projectbrief.md",
            "productContext.md",
            "activeContext.md",
            "systemPatterns.md",
            "techContext.md",
            "progress.md"
        };
    }

    /// <summary>
    /// Reads and writes the project memory bank — a set of markdown files under a
    /// "memory-bank" directory that preserve long-term context between sessions.
    /// </summary>
    public static class MemoryBankService
    {
        public const string DirectoryName = "memory-bank";

        public static string DirectoryPath(string rootPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
                throw new ArgumentNullException(nameof(rootPath));
            return Path.Combine(rootPath, DirectoryName);
        }

        /// <summary>Creates the memory-bank directory and any missing core files with default templates.</summary>
        public static void EnsureInitialized(string rootPath)
        {
            var dir = DirectoryPath(rootPath);
            Directory.CreateDirectory(dir);
            foreach (var fileName in MemoryBankFiles.Core)
            {
                var path = Path.Combine(dir, fileName);
                if (!File.Exists(path))
                    LineEndings.WriteAllText(path, Template(fileName), Encoding.UTF8);
            }
        }

        /// <summary>Returns the concatenated content of all core memory-bank files, or "" when absent.</summary>
        public static string ReadAll(string rootPath)
        {
            var dir = DirectoryPath(rootPath);
            if (!Directory.Exists(dir))
                return "";

            var sb = new StringBuilder();
            foreach (var fileName in MemoryBankFiles.Core)
            {
                var path = Path.Combine(dir, fileName);
                if (!File.Exists(path))
                    continue;
                sb.Append("## ").Append(fileName).AppendLine();
                sb.AppendLine(File.ReadAllText(path, Encoding.UTF8));
                sb.AppendLine();
            }
            return sb.ToString().Trim();
        }

        /// <summary>Writes one core memory-bank file. Only core .md names are allowed.</summary>
        public static void WriteFile(string rootPath, string fileName, string content)
        {
            if (!IsAllowed(fileName))
                throw new ArgumentException("Only memory-bank core .md files can be written: " + string.Join(", ", MemoryBankFiles.Core), nameof(fileName));

            var dir = DirectoryPath(rootPath);
            Directory.CreateDirectory(dir);
            LineEndings.WriteAllText(Path.Combine(dir, fileName), content ?? "", Encoding.UTF8);
        }

        public static bool IsAllowed(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return false;
            foreach (var file in MemoryBankFiles.Core)
                if (string.Equals(file, fileName, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static string Template(string fileName)
        {
            switch (fileName)
            {
                case "projectbrief.md":
                    return "# Project Brief\n\nFoundation document with core requirements and goals.\n";
                case "productContext.md":
                    return "# Product Context\n\nWhy this project exists, the problems it solves, and UX goals.\n";
                case "activeContext.md":
                    return "# Active Context\n\nCurrent focus, recent changes, and next steps.\n";
                case "systemPatterns.md":
                    return "# System Patterns\n\nArchitecture, key technical decisions, and design patterns.\n";
                case "techContext.md":
                    return "# Tech Context\n\nTechnologies, development setup, constraints, and dependencies.\n";
                case "progress.md":
                    return "# Progress\n\nWhat works, what is left, and known issues.\n";
                default:
                    return "";
            }
        }
    }
}
