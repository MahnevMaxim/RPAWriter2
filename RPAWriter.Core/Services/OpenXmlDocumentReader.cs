using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Blip = DocumentFormat.OpenXml.Drawing.Blip;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Reads specs from Word (.docx) and Excel (.xlsx) via the Open XML SDK,
    /// and legacy formats (.doc/.xls) via NPOI.
    /// </summary>
    public class OpenXmlDocumentReader : IDocumentReader
    {
        /// <summary>
        /// Reads a spec document. When <paramref name="mediaOutputDir"/> is provided, embedded
        /// images (.docx/.xlsx) are extracted there and referenced from the Markdown by the
        /// directory's own name (e.g. <c>media/image1.png</c>); EMF/WMF metafiles are skipped.
        /// </summary>
        public SpecModel Read(string path, string mediaOutputDir = null)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
            if (!File.Exists(path)) throw new FileNotFoundException("File not found", path);

            var ext = Path.GetExtension(path).ToLowerInvariant();
            var extractor = string.IsNullOrWhiteSpace(mediaOutputDir) ? null : new MediaExtractor(mediaOutputDir);
            string text;
            string markdown;
            switch (ext)
            {
                case ".docx":
                    text = ReadWordText(path);
                    markdown = ReadWordMarkdown(path, extractor);
                    break;
                case ".xlsx":
                    text = ReadExcelText(path);
                    markdown = ReadExcelMarkdown(path, extractor);
                    break;
                case ".doc":
                case ".xls":
                    return new NpoiDocumentReader().Read(path);
                case ".txt":
                case ".md":
                case ".markdown":
                    text = File.ReadAllText(path, Encoding.UTF8);
                    markdown = text;
                    break;
                default:
                    throw new NotSupportedException("Unsupported document format: " + ext);
            }

            return new SpecModel
            {
                Name = Path.GetFileNameWithoutExtension(path),
                SourcePath = path,
                RawText = text,
                MarkdownText = markdown
            };
        }

        public string ReadWordText(string path)
        {
            var sb = new StringBuilder();
            using (var doc = WordprocessingDocument.Open(path, false))
            {
                var body = doc.MainDocumentPart?.Document?.Body;
                if (body == null) return "";
                var tracker = new ListMarkerTracker(doc.MainDocumentPart?.NumberingDefinitionsPart?.Numbering);
                foreach (var element in body.ChildElements)
                {
                    if (element is Paragraph p)
                    {
                        var text = ReadParagraph(p);
                        var marker = tracker.GetMarker(p);
                        sb.AppendLine(marker != null ? marker + " " + text.Trim() : text);
                    }
                    else if (element is DocumentFormat.OpenXml.Wordprocessing.Table table)
                    {
                        ReadTable(table, tracker, sb);
                    }
                }
            }
            return sb.ToString();
        }

        public string ReadExcelText(string path)
        {
            var sb = new StringBuilder();
            using (var doc = SpreadsheetDocument.Open(path, false))
            {
                var workbookPart = doc.WorkbookPart;
                if (workbookPart?.Workbook?.Sheets == null) return "";

                var sharedStrings = LoadSharedStrings(workbookPart);

                foreach (var sheet in workbookPart.Workbook.Sheets.ChildElements.OfType<Sheet>())
                {
                    var id = sheet.Id?.Value;
                    if (id == null) continue;
                    var worksheetPart = (WorksheetPart)workbookPart.GetPartById(id);
                    if (worksheetPart?.Worksheet == null) continue;

                    sb.AppendLine("== " + sheet.Name + " ==");
                    foreach (var row in worksheetPart.Worksheet.Descendants<Row>())
                    {
                        var cells = row.Descendants<Cell>().Select(c => ReadCell(c, sharedStrings)).ToList();
                        sb.AppendLine(string.Join("\t", cells));
                    }
                }
            }
            return sb.ToString();
        }

        /// <summary>Generates structured Markdown for Word (.docx): headings, lists, and tables.</summary>
        public string ReadWordMarkdown(string path)
        {
            return ReadWordMarkdown(path, null);
        }

        private string ReadWordMarkdown(string path, MediaExtractor extractor)
        {
            var sb = new StringBuilder();
            using (var doc = WordprocessingDocument.Open(path, false))
            {
                var mainPart = doc.MainDocumentPart;
                var body = mainPart?.Document?.Body;
                if (body == null) return "";
                var tracker = new ListMarkerTracker(mainPart?.NumberingDefinitionsPart?.Numbering);
                foreach (var element in body.ChildElements)
                {
                    if (element is Paragraph p)
                    {
                        ReadParagraphImages(p, mainPart, extractor, sb);
                        ReadParagraphMarkdown(p, tracker, sb);
                    }
                    else if (element is DocumentFormat.OpenXml.Wordprocessing.Table table)
                    {
                        ReadTableImages(table, mainPart, extractor, sb);
                        ReadTableMarkdown(table, tracker, sb);
                        sb.AppendLine();
                    }
                }
            }
            return sb.ToString();
        }

        /// <summary>Generates structured Markdown for Excel (.xlsx): sheet → heading + table.</summary>
        public string ReadExcelMarkdown(string path)
        {
            return ReadExcelMarkdown(path, null);
        }

        private string ReadExcelMarkdown(string path, MediaExtractor extractor)
        {
            var sb = new StringBuilder();
            using (var doc = SpreadsheetDocument.Open(path, false))
            {
                var workbookPart = doc.WorkbookPart;
                if (workbookPart?.Workbook?.Sheets == null) return "";
                var sharedStrings = LoadSharedStrings(workbookPart);

                foreach (var sheet in workbookPart.Workbook.Sheets.ChildElements.OfType<Sheet>())
                {
                    var id = sheet.Id?.Value;
                    if (id == null) continue;
                    var worksheetPart = (WorksheetPart)workbookPart.GetPartById(id);
                    if (worksheetPart?.Worksheet == null) continue;

                    sb.AppendLine("## " + sheet.Name);
                    sb.AppendLine();
                    var rows = new List<IReadOnlyList<string>>();
                    foreach (var row in worksheetPart.Worksheet.Descendants<Row>())
                    {
                        var cells = row.Descendants<Cell>().Select(c => ReadCell(c, sharedStrings)).ToList();
                        rows.Add(cells);
                    }
                    sb.Append(MarkdownContent.Table(rows));
                    sb.AppendLine();

                    ReadSheetImages(worksheetPart, extractor, sb);
                }
            }
            return sb.ToString();
        }

        private void ReadParagraphMarkdown(Paragraph p, ListMarkerTracker tracker, StringBuilder sb)
        {
            var text = ReadParagraph(p);
            int level = HeadingLevel(p.ParagraphProperties?.ParagraphStyleId?.Val?.Value);
            if (level > 0)
            {
                sb.AppendLine(new string('#', level) + " " + text.Trim());
                return;
            }
            var marker = tracker.GetMarker(p);
            if (marker != null)
            {
                sb.AppendLine(marker + " " + text.Trim());
                return;
            }
            if (string.IsNullOrWhiteSpace(text))
            {
                sb.AppendLine();
                return;
            }
            sb.AppendLine(text.Trim());
        }

        private void ReadTableMarkdown(DocumentFormat.OpenXml.Wordprocessing.Table table, ListMarkerTracker tracker, StringBuilder sb)
        {
            var rows = new List<IReadOnlyList<string>>();
            foreach (var row in table.Descendants<TableRow>())
            {
                var cells = row.Descendants<TableCell>()
                    .Select(c => ReadCellLines(c, tracker, "<br>"))
                    .ToList();
                rows.Add(cells);
            }
            sb.Append(MarkdownContent.Table(rows));
        }

        private void ReadParagraphImages(Paragraph p, OpenXmlPart part, MediaExtractor extractor, StringBuilder sb)
        {
            if (extractor == null) return;
            foreach (var blip in p.Descendants<Blip>())
                EmitBlip(blip, part, extractor, sb);
        }

        private void ReadTableImages(DocumentFormat.OpenXml.Wordprocessing.Table table, OpenXmlPart part, MediaExtractor extractor, StringBuilder sb)
        {
            if (extractor == null) return;
            foreach (var blip in table.Descendants<Blip>())
                EmitBlip(blip, part, extractor, sb);
        }

        private void ReadSheetImages(WorksheetPart worksheetPart, MediaExtractor extractor, StringBuilder sb)
        {
            if (extractor == null) return;
            var drawingsPart = worksheetPart.DrawingsPart;
            if (drawingsPart?.WorksheetDrawing == null) return;
            foreach (var blip in drawingsPart.WorksheetDrawing.Descendants<Blip>())
                EmitBlip(blip, drawingsPart, extractor, sb);
        }

        private void EmitBlip(Blip blip, OpenXmlPart part, MediaExtractor extractor, StringBuilder sb)
        {
            var embed = blip.Embed?.Value;
            if (string.IsNullOrEmpty(embed)) return; // linked (external) image, not embedded

            OpenXmlPart target;
            try { target = part.GetPartById(embed); }
            catch { return; }

            if (!(target is ImagePart imagePart)) return;

            var relative = extractor.Extract(imagePart);
            if (relative == null) return; // unsupported content type (EMF/WMF, etc.)

            sb.AppendLine("![" + ImageAltText + "](" + relative + ")");
        }

        private const string ImageAltText = "image";

        /// <summary>Writes embedded images to disk and produces Markdown-relative references.</summary>
        private sealed class MediaExtractor
        {
            private readonly string _outputDir;
            private readonly string _relativePrefix;
            private int _counter;

            public MediaExtractor(string outputDir)
            {
                if (string.IsNullOrWhiteSpace(outputDir))
                    throw new ArgumentNullException(nameof(outputDir));
                _outputDir = outputDir;
                var folder = Path.GetFileName(Path.GetFullPath(outputDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                _relativePrefix = string.IsNullOrEmpty(folder) ? "media" : folder;
            }

            /// <summary>Extracts one image part; returns the Markdown-relative path, or null for unsupported types.</summary>
            public string Extract(OpenXmlPart part)
            {
                var ext = ExtensionFromContentType(part.ContentType);
                if (ext == null) return null;

                _counter++;
                var fileName = "image" + _counter + ext;
                Directory.CreateDirectory(_outputDir);
                using (var src = part.GetStream(FileMode.Open, FileAccess.Read))
                using (var dst = File.Create(Path.Combine(_outputDir, fileName)))
                    src.CopyTo(dst);
                return _relativePrefix + "/" + fileName;
            }
        }

        private static string ExtensionFromContentType(string contentType)
        {
            switch ((contentType ?? "").ToLowerInvariant())
            {
                case "image/png": return ".png";
                case "image/jpeg":
                case "image/jpg": return ".jpg";
                case "image/gif": return ".gif";
                case "image/bmp": return ".bmp";
                case "image/tiff":
                case "image/tif": return ".tiff";
                case "image/svg+xml": return ".svg";
                // EMF/WMF vector metafiles are intentionally not extracted.
                default: return null;
            }
        }

        private static int HeadingLevel(string styleId)
        {
            if (string.IsNullOrWhiteSpace(styleId)) return 0;
            var s = styleId.Trim().ToLowerInvariant();
            if (s == "title" || s.Contains("heading1") || s == "1") return 1;
            if (s.Contains("heading2") || s == "2") return 2;
            if (s.Contains("heading3") || s == "3") return 3;
            if (s.Contains("heading4") || s == "4") return 4;
            if (s.Contains("heading5") || s == "5") return 5;
            if (s.Contains("heading6") || s == "6") return 6;
            return 0;
        }

        private static bool IsList(Paragraph p)
        {
            var ppr = p?.ParagraphProperties;
            if (ppr == null) return false;
            if (ppr.NumberingProperties != null) return true;
            var styleId = ppr.ParagraphStyleId?.Val?.Value;
            return styleId != null && styleId.IndexOf("list", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Reconstructs Word list markers (numbers/bullets) from the document numbering definitions.
        /// Word does not store the rendered "1." text in the runs — it is computed from numbering.xml.
        /// </summary>
        private sealed class ListMarkerTracker
        {
            private sealed class LevelInfo
            {
                public NumberFormatValues Format = NumberFormatValues.Decimal;
                public string Text = "";
                public int Start = 1;
                public bool IsBullet { get { return Format == NumberFormatValues.Bullet; } }
            }

            private readonly Dictionary<int, LevelInfo[]> _levels = new Dictionary<int, LevelInfo[]>();
            private readonly Dictionary<int, int[]> _counters = new Dictionary<int, int[]>();
            private readonly Dictionary<int, int> _lastLevel = new Dictionary<int, int>();

            public ListMarkerTracker(Numbering numbering)
            {
                if (numbering == null) return;

                var abstractById = new Dictionary<int, List<LevelInfo>>();
                foreach (var abs in numbering.Elements<AbstractNum>())
                {
                    var abstractId = abs.AbstractNumberId?.Value ?? -1;
                    if (abstractId < 0) continue;

                    var list = new List<LevelInfo>();
                    foreach (var lvl in abs.Elements<Level>())
                    {
                        var ilvl = lvl.LevelIndex?.Value ?? list.Count;
                        var info = new LevelInfo
                        {
                            Format = lvl.NumberingFormat?.Val?.Value ?? NumberFormatValues.Decimal,
                            Text = lvl.LevelText?.Val?.Value ?? "",
                            Start = lvl.StartNumberingValue?.Val?.Value ?? 1
                        };
                        while (list.Count <= ilvl) list.Add(null);
                        list[ilvl] = info;
                    }
                    abstractById[abstractId] = list;
                }

                foreach (var num in numbering.Elements<NumberingInstance>())
                {
                    var numId = num.NumberID?.Value ?? -1;
                    var abstractId = num.AbstractNumId?.Val?.Value ?? -1;
                    if (numId < 0 || abstractId < 0 || !abstractById.TryGetValue(abstractId, out var list)) continue;
                    _levels[numId] = list.ToArray();
                }
            }

            /// <summary>Returns the list marker for a paragraph (e.g. "1." or "-"), or null when it is not a list item.</summary>
            public string GetMarker(Paragraph p)
            {
                var numPr = p?.ParagraphProperties?.NumberingProperties;
                var numId = numPr?.NumberingId?.Val?.Value ?? -1;
                var ilvl = numPr?.NumberingLevelReference?.Val?.Value ?? 0;

                if (numId >= 0 && _levels.TryGetValue(numId, out var levels) && ilvl >= 0 && ilvl < levels.Length && levels[ilvl] != null)
                    return NextMarker(numId, ilvl, levels);

                // A list paragraph that cannot be resolved (style-based or missing numbering part) degrades to a bullet.
                return IsList(p) ? "-" : null;
            }

            private string NextMarker(int numId, int ilvl, LevelInfo[] levels)
            {
                if (!_counters.TryGetValue(numId, out var counters))
                    _counters[numId] = counters = new int[9];

                var last = _lastLevel.TryGetValue(numId, out var lastValue) ? lastValue : -1;
                var level = levels[ilvl];

                if (counters[ilvl] == 0 || ilvl > last)
                {
                    counters[ilvl] = level.Start;
                }
                else if (ilvl == last)
                {
                    counters[ilvl]++;
                }
                else
                {
                    counters[ilvl]++;
                    for (int i = ilvl + 1; i < counters.Length; i++) counters[i] = 0;
                }
                _lastLevel[numId] = ilvl;

                if (level.IsBullet) return "-";

                var template = string.IsNullOrEmpty(level.Text) ? "%1." : level.Text;
                var result = template;
                for (int i = 0; i < 9; i++)
                {
                    var token = "%" + (i + 1);
                    if (result.IndexOf(token, StringComparison.Ordinal) < 0) continue;

                    var info = i < levels.Length ? levels[i] : null;
                    var value = i < counters.Length ? counters[i] : 0;
                    if (value == 0 && info != null) value = info.Start;
                    result = result.Replace(token, FormatValue(value, info != null ? info.Format : NumberFormatValues.Decimal));
                }
                return result;
            }

            private static string FormatValue(int value, NumberFormatValues format)
            {
                if (format == NumberFormatValues.LowerLetter) return ToLetters(value, false);
                if (format == NumberFormatValues.UpperLetter) return ToLetters(value, true);
                if (format == NumberFormatValues.LowerRoman) return ToRoman(value).ToLowerInvariant();
                if (format == NumberFormatValues.UpperRoman) return ToRoman(value);
                return value.ToString();
            }

            private static string ToLetters(int value, bool upper)
            {
                var sb = new StringBuilder();
                int n = value;
                while (n > 0)
                {
                    n--;
                    sb.Insert(0, (char)('a' + (n % 26)));
                    n /= 26;
                }
                var text = sb.Length == 0 ? "a" : sb.ToString();
                return upper ? text.ToUpperInvariant() : text;
            }

            private static string ToRoman(int value)
            {
                if (value <= 0) return value.ToString();
                var numerals = new[] { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
                var symbols = new[] { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };
                var sb = new StringBuilder();
                int n = value;
                for (int i = 0; i < numerals.Length; i++)
                {
                    while (n >= numerals[i])
                    {
                        sb.Append(symbols[i]);
                        n -= numerals[i];
                    }
                }
                return sb.ToString();
            }
        }

        private static List<string> LoadSharedStrings(WorkbookPart workbookPart)
        {
            return workbookPart.SharedStringTablePart?.SharedStringTable?
                .Descendants<SharedStringItem>().Select(s => s.InnerText).ToList()
                ?? new List<string>();
        }

        private static string ReadParagraph(Paragraph p)
        {
            return string.Join("", p.Descendants<DocumentFormat.OpenXml.Wordprocessing.Text>().Select(t => t.Text ?? ""));
        }

        private static void ReadTable(DocumentFormat.OpenXml.Wordprocessing.Table table, ListMarkerTracker tracker, StringBuilder sb)
        {
            foreach (var row in table.Descendants<TableRow>())
            {
                var cells = row.Descendants<TableCell>()
                    .Select(c => ReadCellLines(c, tracker, "\n"))
                    .ToList();
                sb.AppendLine(string.Join("\t", cells));
            }
        }

        private static string ReadCellLines(TableCell cell, ListMarkerTracker tracker, string separator)
        {
            var paragraphs = cell.ChildElements.OfType<Paragraph>().ToList();
            if (paragraphs.Count == 0)
                return string.Join("", cell.Descendants<DocumentFormat.OpenXml.Wordprocessing.Text>().Select(t => t.Text ?? ""));

            var lines = new List<string>();
            foreach (var p in paragraphs)
            {
                var text = ReadParagraph(p).Trim();
                if (text.Length == 0) continue;
                var marker = tracker.GetMarker(p);
                lines.Add(marker != null ? marker + " " + text : text);
            }
            return string.Join(separator, lines);
        }

        private static string ReadCell(Cell cell, List<string> sharedStrings)
        {
            var value = cell.InnerText ?? "";
            if (cell.DataType != null && cell.DataType.Value == CellValues.SharedString)
            {
                int index;
                if (int.TryParse(value, out index) && index >= 0 && index < sharedStrings.Count)
                    return sharedStrings[index];
            }
            return value;
        }
    }
}
