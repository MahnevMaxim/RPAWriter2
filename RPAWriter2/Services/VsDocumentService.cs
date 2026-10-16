using System;
using System.IO;
using System.Text;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;

namespace RPAWriter2.Services
{
    /// <summary>
    /// Helpers for working with Visual Studio document tabs (not the on-disk files).
    /// Used by the agent tools and the compiler-error workflow to read what the user
    /// actually sees in the editor, including unsaved changes and crash-recovery buffers.
    /// Must be called on the UI thread.
    /// </summary>
    public static class VsDocumentService
    {
        /// <summary>Returns the open document whose path matches <paramref name="path"/>, or null.</summary>
        public static Document? FindOpenDocument(DTE2 dte, string path)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (dte == null || string.IsNullOrWhiteSpace(path)) return null;

            var full = Path.GetFullPath(path);
            foreach (Document doc in dte.Documents)
            {
                try
                {
                    var docPath = doc.FullName;
                    if (!string.IsNullOrEmpty(docPath) &&
                        string.Equals(Path.GetFullPath(docPath), full, StringComparison.OrdinalIgnoreCase))
                        return doc;
                }
                catch
                {
                    // Skip documents whose path cannot be resolved.
                }
            }
            return null;
        }

        /// <summary>
        /// Finds the open document for <paramref name="path"/> or opens it in Visual Studio,
        /// then activates its tab. Returns the document, or null when it cannot be opened.
        /// </summary>
        public static Document? OpenOrActivate(DTE2 dte, string path)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (dte == null || string.IsNullOrWhiteSpace(path)) return null;

            var doc = FindOpenDocument(dte, path);
            if (doc == null)
            {
                var window = dte.ItemOperations.OpenFile(path, Constants.vsViewKindTextView);
                doc = window?.Document;
            }
            if (doc != null)
            {
                doc.Activate();
            }
            return doc;
        }

        /// <summary>Reads the full text of an open text document (the editor tab buffer). Returns null for non-text documents.</summary>
        public static string? ReadDocumentText(Document doc)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (doc == null) return null;
            try
            {
                var textDoc = doc.Object("TextDocument") as TextDocument;
                if (textDoc == null) return null;
                var editPoint = textDoc.StartPoint.CreateEditPoint();
                return editPoint.GetText(textDoc.EndPoint);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Reads a limited window of the open document around <paramref name="errorLine"/>.
        /// Lines are numbered and the error line is marked with "&gt;&gt;&gt;".
        /// Returns null for non-text documents or when no lines can be read.
        /// </summary>
        public static string? ReadDocumentWindow(Document doc, int errorLine, int contextLines)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (doc == null) return null;
            try
            {
                var textDoc = doc.Object("TextDocument") as TextDocument;
                if (textDoc == null) return null;

                var lastLine = textDoc.EndPoint.Line;
                var startLine = Math.Max(1, errorLine - contextLines);
                var endLine = Math.Min(lastLine, errorLine + contextLines);
                if (startLine > endLine) return null;

                var editPoint = textDoc.StartPoint.CreateEditPoint();
                var sb = new StringBuilder();
                for (var l = startLine; l <= endLine; l++)
                {
                    var lineText = editPoint.GetLines(l, l);
                    if (lineText == null) continue;
                    lineText = lineText.TrimEnd('\r', '\n');
                    var marker = l == errorLine ? ">>> " : "    ";
                    sb.Append(marker).Append(l).Append(": ").AppendLine(lineText);
                }
                return sb.ToString();
            }
            catch
            {
                return null;
            }
        }
    }
}
