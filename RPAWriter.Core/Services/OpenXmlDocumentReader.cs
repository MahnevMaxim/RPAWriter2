using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
        public SpecModel Read(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
            if (!File.Exists(path)) throw new FileNotFoundException("File not found", path);

            var ext = Path.GetExtension(path).ToLowerInvariant();
            string text;
            string markdown;
            switch (ext)
            {
                case ".docx":
                    text = ReadWordText(path);
                    markdown = ReadWordMarkdown(path);
                    break;
                case ".xlsx":
                    text = ReadExcelText(path);
                    markdown = ReadExcelMarkdown(path);
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
                foreach (var element in body.ChildElements)
                {
                    if (element is Paragraph p)
                        sb.AppendLine(ReadParagraph(p));
                    else if (element is DocumentFormat.OpenXml.Wordprocessing.Table table)
                        ReadTable(table, sb);
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
            var sb = new StringBuilder();
            using (var doc = WordprocessingDocument.Open(path, false))
            {
                var body = doc.MainDocumentPart?.Document?.Body;
                if (body == null) return "";
                foreach (var element in body.ChildElements)
                {
                    if (element is Paragraph p)
                        ReadParagraphMarkdown(p, sb);
                    else if (element is DocumentFormat.OpenXml.Wordprocessing.Table table)
                    {
                        ReadTableMarkdown(table, sb);
                        sb.AppendLine();
                    }
                }
            }
            return sb.ToString();
        }

        /// <summary>Generates structured Markdown for Excel (.xlsx): sheet → heading + table.</summary>
        public string ReadExcelMarkdown(string path)
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
                }
            }
            return sb.ToString();
        }

        private void ReadParagraphMarkdown(Paragraph p, StringBuilder sb)
        {
            var text = ReadParagraph(p);
            int level = HeadingLevel(p.ParagraphProperties?.ParagraphStyleId?.Val?.Value);
            if (level > 0)
            {
                sb.AppendLine(new string('#', level) + " " + text.Trim());
                return;
            }
            if (IsList(p))
            {
                sb.AppendLine("- " + text.Trim());
                return;
            }
            if (string.IsNullOrWhiteSpace(text))
            {
                sb.AppendLine();
                return;
            }
            sb.AppendLine(text.Trim());
        }

        private void ReadTableMarkdown(DocumentFormat.OpenXml.Wordprocessing.Table table, StringBuilder sb)
        {
            var rows = new List<IReadOnlyList<string>>();
            foreach (var row in table.Descendants<TableRow>())
            {
                var cells = row.Descendants<TableCell>()
                    .Select(c => string.Join("", c.Descendants<DocumentFormat.OpenXml.Wordprocessing.Text>().Select(t => t.Text ?? "")))
                    .ToList();
                rows.Add(cells);
            }
            sb.Append(MarkdownContent.Table(rows));
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

        private static void ReadTable(DocumentFormat.OpenXml.Wordprocessing.Table table, StringBuilder sb)
        {
            foreach (var row in table.Descendants<TableRow>())
            {
                var cells = row.Descendants<TableCell>()
                    .Select(c => string.Join("", c.Descendants<DocumentFormat.OpenXml.Wordprocessing.Text>().Select(t => t.Text ?? "")))
                    .ToList();
                sb.AppendLine(string.Join("\t", cells));
            }
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
