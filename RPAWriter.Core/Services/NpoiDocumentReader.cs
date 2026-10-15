using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NPOI.HSSF.UserModel;
using NPOI.POIFS.FileSystem;
using NPOI.SS.UserModel;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Reads legacy documents (<c>.doc</c> / <c>.xls</c>) via NPOI.
    /// <c>.xls</c> is read in full (HSSF). <c>.doc</c> is best-effort:
    /// NPOI does not ship an HWPF parser (Word 97-2003), so text is extracted
    /// from the OLE2 container ("WordDocument" stream) using the Unicode flag in the FIB.
    /// </summary>
    public class NpoiDocumentReader : IDocumentReader
    {
        private const string WordDocumentStream = "WordDocument";

        public SpecModel Read(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
            if (!File.Exists(path)) throw new FileNotFoundException("File not found", path);

            var ext = Path.GetExtension(path).ToLowerInvariant();
            string text;
            string markdown;
            switch (ext)
            {
                case ".doc":
                    text = ReadDocText(path);
                    markdown = text;
                    break;
                case ".xls":
                    text = ReadXlsText(path);
                    markdown = ReadXlsMarkdown(path);
                    break;
                default:
                    throw new NotSupportedException("Unsupported document format for NPOI: " + ext);
            }

            return new SpecModel
            {
                Name = Path.GetFileNameWithoutExtension(path),
                SourcePath = path,
                RawText = text,
                MarkdownText = markdown
            };
        }

        /// <summary>Extracts text from a legacy Word document (.doc) through the NPOI OLE2 container.</summary>
        public string ReadDocText(string path)
        {
            using (var fs = File.OpenRead(path))
            {
                var poifs = new NPOIFSFileSystem(fs);
                try
                {
                    var entry = poifs.Root.GetEntry(WordDocumentStream);
                    if (entry == null || !entry.IsDocumentEntry) return string.Empty;
                    using (var din = new DocumentInputStream((DocumentEntry)entry))
                    {
                        return ExtractDocText(ReadFully(din));
                    }
                }
                finally
                {
                    poifs.Close();
                }
            }
        }

        /// <summary>Extracts text from a legacy Excel workbook (.xls) in the same shape as .xlsx.</summary>
        public string ReadXlsText(string path)
        {
            var sb = new StringBuilder();
            using (var fs = File.OpenRead(path))
            {
                var workbook = new HSSFWorkbook(fs);
                try
                {
                    var formatter = new DataFormatter();
                    for (int i = 0; i < workbook.NumberOfSheets; i++)
                    {
                        var sheet = workbook.GetSheetAt(i);
                        sb.AppendLine("== " + workbook.GetSheetName(i) + " ==");
                        foreach (IRow row in sheet)
                        {
                            var cells = new List<string>();
                            for (int c = 0; c < row.LastCellNum; c++)
                            {
                                var cell = row.GetCell(c);
                                cells.Add(cell == null ? "" : formatter.FormatCellValue(cell));
                            }
                            sb.AppendLine(string.Join("\t", cells));
                        }
                    }
                }
                finally
                {
                    workbook.Close();
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Best-effort extraction of text from the "WordDocument" stream of a .doc file.
        /// Determines the encoding from the fExtChar (Unicode) flag in the FIB and assembles
        /// contiguous runs of printable characters.
        /// </summary>
        /// <summary>Extracts structured Markdown from a legacy Excel workbook (.xls): sheet → heading + table.</summary>
        public string ReadXlsMarkdown(string path)
        {
            var sb = new StringBuilder();
            using (var fs = File.OpenRead(path))
            {
                var workbook = new HSSFWorkbook(fs);
                try
                {
                    var formatter = new DataFormatter();
                    for (int i = 0; i < workbook.NumberOfSheets; i++)
                    {
                        var sheet = workbook.GetSheetAt(i);
                        sb.AppendLine("## " + workbook.GetSheetName(i));
                        sb.AppendLine();
                        var rows = new List<IReadOnlyList<string>>();
                        foreach (IRow row in sheet)
                        {
                            var cells = new List<string>();
                            for (int c = 0; c < row.LastCellNum; c++)
                            {
                                var cell = row.GetCell(c);
                                cells.Add(cell == null ? "" : formatter.FormatCellValue(cell));
                            }
                            rows.Add(cells);
                        }
                        sb.Append(MarkdownContent.Table(rows));
                        sb.AppendLine();
                    }
                }
                finally
                {
                    workbook.Close();
                }
            }
            return sb.ToString();
        }

        public static string ExtractDocText(byte[] wordDocumentStream)
        {
            if (wordDocumentStream == null || wordDocumentStream.Length < 0x0C)
                return string.Empty;

            var sb = new StringBuilder();
            var run = new StringBuilder();

            if (IsUnicode(wordDocumentStream))
            {
                for (int i = 0; i + 1 < wordDocumentStream.Length; i += 2)
                {
                    char c = (char)(wordDocumentStream[i] | (wordDocumentStream[i + 1] << 8));
                    if (IsTextChar(c)) run.Append(c); else FlushRun(sb, run);
                }
            }
            else
            {
                // ANSI (.doc): byte → Latin-1 character (best-effort).
                for (int i = 0; i < wordDocumentStream.Length; i++)
                {
                    char c = (char)wordDocumentStream[i];
                    if (IsTextChar(c)) run.Append(c); else FlushRun(sb, run);
                }
            }

            FlushRun(sb, run);
            return sb.ToString();
        }

        private static bool IsUnicode(byte[] data)
        {
            // FIB: flags word at offset 0x0A; bit 12 (0x1000) — fExtChar (Unicode).
            int flags = data[0x0A] | (data[0x0B] << 8);
            return (flags & 0x1000) != 0;
        }

        private static void FlushRun(StringBuilder result, StringBuilder run)
        {
            if (run.Length >= 4)
            {
                if (result.Length > 0) result.Append('\n');
                result.Append(run.ToString());
            }
            run.Clear();
        }

        private static bool IsTextChar(char c)
        {
            if (char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)) return true;
            switch (c)
            {
                case '.': case ',': case ';': case ':': case '!': case '?':
                case '-': case '_': case '(': case ')': case '[': case ']':
                case '{': case '}': case '/': case '\\': case '"': case '\'':
                case '+': case '=': case '<': case '>': case '@': case '#':
                case '$': case '%': case '&': case '*': case '~': case '^':
                case '|': case '`':
                    return true;
                default:
                    return false;
            }
        }

        private static byte[] ReadFully(Stream stream)
        {
            using (var ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                return ms.ToArray();
            }
        }

    }
}
