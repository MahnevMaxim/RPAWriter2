using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Markdig;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using W = DocumentFormat.OpenXml.Wordprocessing;
using D = DocumentFormat.OpenXml.Drawing;
using DWP = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using MTable = Markdig.Extensions.Tables.Table;
using MTableRow = Markdig.Extensions.Tables.TableRow;
using MTableCell = Markdig.Extensions.Tables.TableCell;

namespace RPAWriter.Core.Services
{
    /// <summary>Summary of a Markdown-to-Word conversion.</summary>
    public class MarkdownToDocxResult
    {
        public int Paragraphs { get; set; }
        public int Images { get; set; }
        public int Tables { get; set; }
    }

    /// <summary>
    /// Converts a Markdown document (text and images) into a Word .docx file.
    /// Uses Markdig for parsing and the Open XML SDK for writing.
    /// </summary>
    public class MarkdownToDocxConverter
    {
        private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
            .UsePipeTables()
            .UseGenericAttributes()
            .Build();

        private W.Body _body;
        private MainDocumentPart _mainPart;
        private MarkdownToDocxResult _result;

        /// <summary>
        /// Converts the markdown file at <paramref name="markdownPath"/> and writes the result
        /// to <paramref name="outputDocxPath"/>. Relative image paths are resolved against the
        /// markdown file's directory.
        /// </summary>
        public MarkdownToDocxResult Convert(string markdownPath, string outputDocxPath)
        {
            if (string.IsNullOrWhiteSpace(markdownPath)) throw new ArgumentNullException(nameof(markdownPath));
            if (!File.Exists(markdownPath)) throw new FileNotFoundException("Markdown file not found", markdownPath);

            var markdown = File.ReadAllText(markdownPath, Encoding.UTF8);
            return Convert(markdown, Path.GetDirectoryName(markdownPath), outputDocxPath);
        }

        /// <summary>
        /// Converts markdown text to a .docx file. Images are resolved relative to
        /// <paramref name="baseDirectory"/>.
        /// </summary>
        public MarkdownToDocxResult Convert(string markdown, string baseDirectory, string outputDocxPath)
        {
            if (string.IsNullOrWhiteSpace(outputDocxPath)) throw new ArgumentNullException(nameof(outputDocxPath));

            _result = new MarkdownToDocxResult();
            var document = Markdig.Markdown.Parse(markdown ?? string.Empty, Pipeline);

            using (var word = WordprocessingDocument.Create(outputDocxPath, WordprocessingDocumentType.Document))
            {
                _mainPart = word.AddMainDocumentPart();
                _mainPart.Document = new W.Document();
                _body = _mainPart.Document.AppendChild(new W.Body());

                foreach (var block in document)
                    AppendBlock(block, baseDirectory, 0);

                _mainPart.Document.Save();
            }

            return _result;
        }

        private void AppendBlock(Block block, string baseDirectory, int depth)
        {
            switch (block)
            {
                case HeadingBlock heading:
                    AppendHeading(heading, baseDirectory);
                    break;

                case ParagraphBlock paragraph:
                    AppendParagraph(paragraph, baseDirectory, depth);
                    break;

                case FencedCodeBlock fenced:
                    AppendCodeBlock(fenced);
                    break;

                case CodeBlock code:
                    AppendCodeBlock(code);
                    break;

                case QuoteBlock quote:
                    foreach (var child in quote)
                        AppendBlock(child, baseDirectory, depth + 1);
                    break;

                case ListBlock list:
                    AppendList(list, baseDirectory, depth);
                    break;

                case MTable table:
                    AppendTable(table, baseDirectory);
                    break;

                case ThematicBreakBlock _:
                    _body.AppendChild(new W.Paragraph());
                    _result.Paragraphs++;
                    break;

                case HtmlBlock _:
                case LinkReferenceDefinitionGroup _:
                    break;

                case LeafBlock leaf when leaf.Inline != null:
                    var p = NewParagraph(depth);
                    AppendInline(leaf.Inline, p, baseDirectory);
                    _body.AppendChild(p);
                    _result.Paragraphs++;
                    break;
            }
        }

        private void AppendHeading(HeadingBlock heading, string baseDirectory)
        {
            var p = new W.Paragraph();
            var level = Math.Max(1, Math.Min(6, heading.Level));
            p.AppendChild(new W.ParagraphProperties(new W.ParagraphStyleId { Val = "Heading" + level }));
            AppendInline(heading.Inline, p, baseDirectory);
            _body.AppendChild(p);
            _result.Paragraphs++;
        }

        private void AppendParagraph(ParagraphBlock paragraph, string baseDirectory, int depth)
        {
            var p = NewParagraph(depth);
            AppendInline(paragraph.Inline, p, baseDirectory);
            _body.AppendChild(p);
            _result.Paragraphs++;
        }

        private void AppendCodeBlock(CodeBlock code)
        {
            var text = MarkdownCodeBlockText.GetText(code).TrimEnd('\r', '\n');
            var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            foreach (var line in normalized.Split('\n'))
            {
                var p = new W.Paragraph();
                p.AppendChild(new W.ParagraphProperties(new W.Shading { Fill = "F2F2F2" }));
                p.AppendChild(Run(line, bold: false, monospace: true));
                _body.AppendChild(p);
                _result.Paragraphs++;
            }
        }

        private void AppendList(ListBlock list, string baseDirectory, int depth)
        {
            var number = 1;
            foreach (var child in list)
            {
                if (!(child is ListItemBlock item)) continue;

                var p = NewParagraph(depth);
                if (list.IsOrdered)
                    p.AppendChild(Run(number + (list.OrderedDelimiter == ')' ? ") " : ". ")));
                else
                    p.AppendChild(Run("\u2022 "));

                var first = true;
                foreach (var sub in item)
                {
                    if (first && sub is ParagraphBlock pb)
                    {
                        AppendInline(pb.Inline, p, baseDirectory);
                        first = false;
                    }
                    else
                    {
                        AppendBlock(sub, baseDirectory, depth + 1);
                    }
                }

                _body.AppendChild(p);
                _result.Paragraphs++;
                number++;
            }
        }

        private void AppendTable(MTable table, string baseDirectory)
        {
            var rows = new List<MTableRow>();
            foreach (var child in table)
                if (child is MTableRow row)
                    rows.Add(row);

            if (rows.Count == 0) return;

            var columnCount = 0;
            foreach (var row in rows)
                columnCount = Math.Max(columnCount, CountCells(row));
            if (columnCount == 0) return;

            var t = new W.Table();
            t.AppendChild(new W.TableProperties(
                new W.TableBorders(
                    new W.TopBorder { Val = W.BorderValues.Single, Size = 4 },
                    new W.BottomBorder { Val = W.BorderValues.Single, Size = 4 },
                    new W.LeftBorder { Val = W.BorderValues.Single, Size = 4 },
                    new W.RightBorder { Val = W.BorderValues.Single, Size = 4 },
                    new W.InsideHorizontalBorder { Val = W.BorderValues.Single, Size = 4 },
                    new W.InsideVerticalBorder { Val = W.BorderValues.Single, Size = 4 })));

            foreach (var row in rows)
            {
                var cells = new List<MTableCell>();
                foreach (var child in row)
                    if (child is MTableCell cell)
                        cells.Add(cell);

                var tr = new W.TableRow();
                for (var c = 0; c < columnCount; c++)
                {
                    var tc = new W.TableCell();
                    var p = new W.Paragraph();
                    if (c < cells.Count)
                        AppendCell(cells[c], p, baseDirectory, bold: row.IsHeader);
                    tc.AppendChild(p);
                    tr.AppendChild(tc);
                }
                t.AppendChild(tr);
            }

            _body.AppendChild(t);
            _result.Tables++;
        }

        private void AppendCell(MTableCell cell, W.Paragraph paragraph, string baseDirectory, bool bold)
        {
            foreach (var child in cell)
            {
                if (child is ParagraphBlock pb)
                    AppendInline(pb.Inline, paragraph, baseDirectory, bold);
                else
                    AppendBlock(child, baseDirectory, 1);
            }
        }

        private void AppendInline(ContainerInline inline, W.Paragraph paragraph, string baseDirectory, bool bold = false)
        {
            if (inline == null) return;
            foreach (var child in inline)
                AppendInlineNode(child, paragraph, baseDirectory, bold);
        }

        private void AppendInlineNode(Inline inline, W.Paragraph paragraph, string baseDirectory, bool bold)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    paragraph.AppendChild(Run(literal.Content.ToString(), bold));
                    break;

                case CodeInline code:
                    paragraph.AppendChild(Run(code.Content, bold, monospace: true));
                    break;

                case LineBreakInline breakInline:
                    paragraph.AppendChild(new W.Run(new W.Break()));
                    break;

                case EmphasisInline emphasis:
                    AppendInline(emphasis, paragraph, baseDirectory, bold);
                    break;

                case LinkInline link when link.IsImage:
                    AppendImage(paragraph, link, baseDirectory);
                    break;

                case LinkInline link:
                    AppendInline(link, paragraph, baseDirectory, bold);
                    break;

                case HtmlEntityInline entity:
                    paragraph.AppendChild(Run(entity.Transcoded.ToString(), bold));
                    break;

                case ContainerInline container:
                    AppendInline(container, paragraph, baseDirectory, bold);
                    break;

                default:
                    var text = inline?.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                        paragraph.AppendChild(Run(text, bold));
                    break;
            }
        }

        private void AppendImage(W.Paragraph paragraph, LinkInline link, string baseDirectory)
        {
            var imagePath = ResolveImagePath(link.Url, baseDirectory);
            if (imagePath == null) return;

            var partType = ImagePartTypeFor(imagePath);
            if (partType == null) return;

            ImagePart part;
            try
            {
                part = _mainPart.AddImagePart(partType.Value);
                using (var stream = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    part.FeedData(stream);
            }
            catch
            {
                return; // skip unreadable/unsupported images
            }

            var relationId = _mainPart.GetIdOfPart(part);
            var index = _result.Images + 1;
            var size = ResolveImageSize(link, imagePath);

            paragraph.AppendChild(new W.Run(BuildInlineDrawing(relationId, index, size.WidthEmu, size.HeightEmu)));
            _result.Images++;
        }

        private const long EmuPerInch = 914400L;
        private const double EmuPerPixel = 9525.0;
        private const long MaxNaturalWidthEmu = 5943600L; // 6.5 inches (fits within page margins)

        private static (long WidthEmu, long HeightEmu) ResolveImageSize(LinkInline link, string imagePath)
        {
            // 1. Explicit size from Markdown attributes, e.g. ![alt](url){width=104 height=104}.
            var attributes = link.GetAttributes();
            double? width = attributes?.Properties == null ? null : TryGetNumber(attributes, "width");
            double? height = attributes?.Properties == null ? null : TryGetNumber(attributes, "height");
            if (width.HasValue && height.HasValue && width.Value > 0 && height.Value > 0)
                return (PixelsToEmu(width.Value), PixelsToEmu(height.Value));

            // 2. Natural size of the image file, capped to the printable width.
            var natural = ImageDimensions.Read(imagePath);
            if (natural.Width > 0 && natural.Height > 0)
            {
                var w = PixelsToEmu(natural.Width);
                var h = PixelsToEmu(natural.Height);
                if (w > MaxNaturalWidthEmu)
                {
                    h = (long)Math.Round(h * (double)MaxNaturalWidthEmu / w);
                    w = MaxNaturalWidthEmu;
                }
                return (w, h);
            }

            // 3. Default 6x4 inches when the size cannot be determined.
            return (6 * EmuPerInch, 4 * EmuPerInch);
        }

        private static double? TryGetNumber(HtmlAttributes attributes, string key)
        {
            foreach (var property in attributes.Properties)
            {
                if (string.Equals(property.Key, key, StringComparison.OrdinalIgnoreCase)
                    && double.TryParse(property.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
                {
                    return value;
                }
            }
            return null;
        }

        private static long PixelsToEmu(double pixels)
        {
            return (long)Math.Round(pixels * EmuPerPixel);
        }

        private static W.Drawing BuildInlineDrawing(string relationId, int index, long widthEmu, long heightEmu)
        {
            var picture = new PIC.Picture(
                new PIC.NonVisualPictureProperties(
                    new PIC.NonVisualDrawingProperties { Id = (UInt32Value)(uint)index, Name = "Image" + index },
                    new PIC.NonVisualPictureDrawingProperties()),
                new PIC.BlipFill(
                    new D.Blip { Embed = relationId },
                    new D.Stretch(new D.FillRectangle())),
                new PIC.ShapeProperties(
                    new D.Transform2D(
                        new D.Offset { X = 0, Y = 0 },
                        new D.Extents { Cx = widthEmu, Cy = heightEmu }),
                    new D.PresetGeometry(new D.AdjustValueList()) { Preset = D.ShapeTypeValues.Rectangle }));

            var graphicData = new D.GraphicData(picture)
            {
                Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture"
            };

            return new W.Drawing(
                new DWP.Inline(
                    new DWP.Extent { Cx = widthEmu, Cy = heightEmu },
                    new DWP.EffectExtent { LeftEdge = 0, TopEdge = 0, RightEdge = 0, BottomEdge = 0 },
                    new DWP.DocProperties { Id = (UInt32Value)(uint)index, Name = "Image" + index },
                    new DWP.NonVisualGraphicFrameDrawingProperties(new D.GraphicFrameLocks { NoChangeAspect = true }),
                    new D.Graphic(graphicData)));
        }

        private static PartTypeInfo? ImagePartTypeFor(string imagePath)
        {
            switch (Path.GetExtension(imagePath).ToLowerInvariant())
            {
                case ".png": return ImagePartType.Png;
                case ".jpg":
                case ".jpeg": return ImagePartType.Jpeg;
                case ".gif": return ImagePartType.Gif;
                case ".bmp": return ImagePartType.Bmp;
                case ".tiff":
                case ".tif": return ImagePartType.Tiff;
                default: return null; // svg/emf/wmf are not inserted inline
            }
        }

        private static string ResolveImagePath(string url, string baseDirectory)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            var path = url.Trim();
            var hash = path.IndexOf('#');
            if (hash >= 0) path = path.Substring(0, hash);
            var query = path.IndexOf('?');
            if (query >= 0) path = path.Substring(0, query);

            if (Path.IsPathRooted(path))
                return File.Exists(path) ? path : null;

            var full = Path.Combine(baseDirectory ?? string.Empty, path);
            return File.Exists(full) ? full : null;
        }

        private static int CountCells(MTableRow row)
        {
            var count = 0;
            foreach (var child in row)
                if (child is MTableCell)
                    count++;
            return count;
        }

        private static W.Paragraph NewParagraph(int depth)
        {
            var p = new W.Paragraph();
            if (depth > 0)
                p.AppendChild(new W.ParagraphProperties(new W.Indentation { Left = (240 * depth).ToString() }));
            return p;
        }

        private static W.Run Run(string text, bool bold = false, bool monospace = false)
        {
            var run = new W.Run();
            var runProperties = new W.RunProperties();
            if (bold) runProperties.AppendChild(new W.Bold());
            if (monospace) runProperties.AppendChild(new W.RunFonts { Ascii = "Consolas", HighAnsi = "Consolas" });
            if (runProperties.HasChildren) run.AppendChild(runProperties);
            run.AppendChild(new W.Text(text ?? string.Empty));
            return run;
        }
    }
}



