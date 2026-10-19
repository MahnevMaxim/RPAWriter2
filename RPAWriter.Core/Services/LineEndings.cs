using System;
using System.IO;
using System.Text;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Normalizes line endings so files written by the extension never contain mixed
    /// CRLF/LF endings. Mixed endings trigger Visual Studio's "Inconsistent Line
    /// Endings" dialog on load, which blocks the UI and any automation flow.
    /// </summary>
    public static class LineEndings
    {
        public const string CrLf = "\r\n";
        public const string Lf = "\n";

        /// <summary>
        /// Returns the dominant line ending of <paramref name="text"/>: CRLF when it contains
        /// at least as many CRLF as bare LF line breaks, otherwise LF. Empty text falls back to
        /// <see cref="Environment.NewLine"/>.
        /// </summary>
        public static string Detect(string text)
        {
            if (string.IsNullOrEmpty(text))
                return Environment.NewLine;

            int crlf = 0;
            int lf = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != '\n')
                    continue;
                if (i > 0 && text[i - 1] == '\r')
                    crlf++;
                else
                    lf++;
            }

            return crlf >= lf ? CrLf : Lf;
        }

        /// <summary>
        /// Rewrites <paramref name="text"/> so every line break uses <paramref name="newline"/>.
        /// Handles CRLF, bare LF and bare CR input uniformly.
        /// </summary>
        public static string Normalize(string text, string newline)
        {
            if (text == null)
                return string.Empty;
            if (string.IsNullOrEmpty(newline))
                newline = Environment.NewLine;

            var single = text.Replace("\r\n", "\n").Replace('\r', '\n');
            if (string.Equals(newline, "\n", StringComparison.Ordinal))
                return single;
            return single.Replace("\n", newline);
        }

        /// <summary>
        /// Writes <paramref name="content"/> to <paramref name="path"/> without a BOM, keeping the
        /// file's existing line ending when the file already exists and using
        /// <see cref="Environment.NewLine"/> for new files.
        /// </summary>
        public static void WriteAllText(string path, string content)
        {
            WriteAllText(path, content, null);
        }

        /// <summary>
        /// Writes <paramref name="content"/> to <paramref name="path"/>, normalizing line endings and
        /// using <paramref name="encoding"/> (null means the default UTF-8 without BOM).
        /// </summary>
        public static void WriteAllText(string path, string content, Encoding encoding)
        {
            var newline = Environment.NewLine;
            if (File.Exists(path))
            {
                try
                {
                    newline = Detect(File.ReadAllText(path));
                }
                catch
                {
                    // Fall back to Environment.NewLine when the existing file cannot be read.
                }
            }

            var text = Normalize(content ?? string.Empty, newline);
            if (encoding != null)
                File.WriteAllText(path, text, encoding);
            else
                File.WriteAllText(path, text);
        }
    }
}
