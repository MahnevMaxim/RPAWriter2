using System;
using System.IO;
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
    }
}
