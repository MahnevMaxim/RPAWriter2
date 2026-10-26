using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Loads empty Workflow Foundation activity snippets (text files with {{placeholder}}
    /// markers) from the activity library's "wf" folder.
    /// </summary>
    public class WfSnippetLibrary
    {
        private readonly string _directory;

        public WfSnippetLibrary() : this(ActivityLibraryPaths.ResolveWfSnippetsDirectory())
        {
        }

        public WfSnippetLibrary(string directory)
        {
            _directory = directory ?? "";
        }

        /// <summary>
        /// Reads the snippet <paramref name="name"/>.xaml and returns its trimmed content,
        /// or <paramref name="fallback"/> when the file is absent/empty/unreadable.
        /// </summary>
        public string Load(string name, string fallback)
        {
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(_directory))
            {
                try
                {
                    var path = Path.Combine(_directory, name + ".xaml");
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

        /// <summary>
        /// Enumerates every deployed WF snippet (name + trimmed XAML template), sorted by name.
        /// </summary>
        public IReadOnlyList<WfSnippet> GetAll()
        {
            var result = new List<WfSnippet>();
            if (string.IsNullOrWhiteSpace(_directory) || !Directory.Exists(_directory))
                return result;

            foreach (var path in Directory.GetFiles(_directory, "*.xaml"))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                var content = string.Empty;
                try
                {
                    var text = File.ReadAllText(path, Encoding.UTF8);
                    if (!string.IsNullOrWhiteSpace(text))
                        content = text.Trim();
                }
                catch
                {
                    // ignore read errors
                }

                result.Add(new WfSnippet { Name = name, Content = content });
            }

            return result.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    /// <summary>A deployed Workflow Foundation activity snippet (XAML template).</summary>
    public class WfSnippet
    {
        /// <summary>Snippet file name without the .xaml extension (e.g. "Assign").</summary>
        public string Name { get; set; }

        /// <summary>Trimmed XAML template content with {{placeholder}} markers.</summary>
        public string Content { get; set; }
    }
}
