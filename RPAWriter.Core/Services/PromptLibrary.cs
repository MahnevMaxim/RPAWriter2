using System;
using System.IO;
using System.Text;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Loads prompt template files from the deployed "Prompts" directory,
    /// falling back to built-in defaults when a file is missing or unreadable.
    /// </summary>
    public static class PromptLibrary
    {
        /// <summary>Prompt category folder names.</summary>
        public static class Categories
        {
            public const string System = "System";
            public const string Tasks = "Tasks";
        }

        /// <summary>
        /// Reads a prompt file relative to the prompts directory and returns its
        /// trimmed content, or <paramref name="fallback"/> when the file is absent/empty/unreadable.
        /// </summary>
        public static string Load(string promptsPath, string category, string fileName, string fallback)
        {
            if (!string.IsNullOrWhiteSpace(promptsPath))
            {
                try
                {
                    var path = Path.Combine(promptsPath, category ?? "", fileName ?? "");
                    if (File.Exists(path))
                    {
                        var text = File.ReadAllText(path, Encoding.UTF8);
                        if (!string.IsNullOrWhiteSpace(text))
                            return text.Trim();
                    }
                }
                catch
                {
                    // ignore read errors — fall back to the built-in default
                }
            }
            return fallback;
        }
    }
}
