using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using DocumentFormat.OpenXml.Wordprocessing;
using Pic = DocumentFormat.OpenXml.Drawing.Pictures;
using Wp = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NUnit.Framework;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class ReaderAndPipelineTests
    {
        private string _dir;

        [SetUp]
        public void SetUp() => _dir = Path.Combine(Path.GetTempPath(), "RPAWriterTests", Path.GetRandomFileName());

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        [Test]
        public void OpenXmlDocumentReader_ReadsWordText()
        {
            var path = Path.Combine(_dir, "spec.docx");
            Directory.CreateDirectory(_dir);
            CreateDocx(path, "Open browser", "Read table");

            var spec = new OpenXmlDocumentReader().Read(path);

            Assert.That(spec.RawText, Does.Contain("Open browser"));
            Assert.That(spec.RawText, Does.Contain("Read table"));
            Assert.That(spec.Name, Is.EqualTo("spec"));
        }

        [Test]
        public void NpoiDocumentReader_ReadsXlsText()
        {
            var path = Path.Combine(_dir, "spec.xls");
            Directory.CreateDirectory(_dir);
            CreateXls(path);

            var spec = new NpoiDocumentReader().Read(path);

            Assert.That(spec.RawText, Does.Contain("Sheet1"));
            Assert.That(spec.RawText, Does.Contain("Open browser"));
            Assert.That(spec.RawText, Does.Contain("Read table"));
            Assert.That(spec.Name, Is.EqualTo("spec"));
        }

        [Test]
        public void OpenXmlDocumentReader_ReadsXls()
        {
            var path = Path.Combine(_dir, "spec.xls");
            Directory.CreateDirectory(_dir);
            CreateXls(path);

            var spec = new OpenXmlDocumentReader().Read(path);

            Assert.That(spec.RawText, Does.Contain("Open browser"));
            Assert.That(spec.RawText, Does.Contain("Sheet1"));
        }

        [Test]
        public void NpoiDocumentReader_RejectsNonLegacyExtension()
        {
            var path = Path.Combine(_dir, "spec.docx");
            Directory.CreateDirectory(_dir);
            File.WriteAllText(path, "not a legacy document");

            Assert.Throws<NotSupportedException>(() => new NpoiDocumentReader().Read(path));
        }

        [Test]
        public void NpoiDocumentReader_ExtractsDocText()
        {
            // Minimal "WordDocument" stream: FIB header (fExtChar/Unicode) + UTF-16LE text.
            var text = "Open browser and read table";
            var textBytes = Encoding.Unicode.GetBytes(text);
            var data = new byte[0x0C + textBytes.Length];
            data[0x0B] = 0x10; // bit 12 of the flags word (0x1000) — fExtChar
            Array.Copy(textBytes, 0, data, 0x0C, textBytes.Length);

            var extracted = NpoiDocumentReader.ExtractDocText(data);

            Assert.That(extracted, Does.Contain("Open browser"));
            Assert.That(extracted, Does.Contain("read table"));
        }

        [Test]
        public void OpenXmlDocumentReader_RoutesDocToNpoi()
        {
            // .doc is not OLE2 — NPOI (POIFS) will throw its own exception, not "unsupported format".
            var path = Path.Combine(_dir, "spec.doc");
            Directory.CreateDirectory(_dir);
            File.WriteAllText(path, "not a valid .doc");

            Exception caught = null;
            try { new OpenXmlDocumentReader().Read(path); }
            catch (Exception ex) { caught = ex; }

            Assert.That(caught, Is.Not.Null);
            Assert.That(caught, Is.Not.TypeOf<NotSupportedException>(), "expected NPOI error when reading an invalid .doc");
        }

        [Test]
        public async Task Pipeline_GeneratesXamlAndCode()
        {
            var path = Path.Combine(_dir, "spec.txt");
            Directory.CreateDirectory(_dir);
            File.WriteAllText(path, "Open browser and read table.");

            var kb = new FileKnowledgeBase(_dir);
            var pipeline = new RpaPipelineService(
                new OpenXmlDocumentReader(),
                new StubAiProvider(),
                kb,
                new RobotGenerator());

            var result = await pipeline.GenerateRobotFromSpecAsync(path);

            Assert.That(result.Xaml, Does.Contain("InvokeMethod"));
            Assert.That(result.CSharpCode, Does.Not.Empty);
        }

        [Test]
        public async Task KnowledgeBaseFormation_BuildsRecords()
        {
            var docPath = Path.Combine(_dir, "robot.md");
            Directory.CreateDirectory(_dir);
            File.WriteAllText(docPath, "Open browser. Fill in the form.");

            var kb = new FileKnowledgeBase(_dir);
            var service = new KnowledgeBaseFormationService(new OpenXmlDocumentReader(), new StubAiProvider(), kb);

            var added = await service.BuildFromRobotDocsAsync(new[] { docPath }, "// robot code");

            Assert.That(added, Is.GreaterThan(0));
            var all = await kb.GetAllAsync();
            Assert.That(all.Count, Is.EqualTo(added));
        }

        [Test]
        public void OpenXmlDocumentReader_GeneratesMarkdownWithHeadingAndTable()
        {
            var path = Path.Combine(_dir, "spec.docx");
            Directory.CreateDirectory(_dir);
            CreateDocxStructured(path);

            var spec = new OpenXmlDocumentReader().Read(path);

            Assert.That(spec.MarkdownText, Does.Contain("# Heading"));
            Assert.That(spec.MarkdownText, Does.Contain("| Column A | Column B |"));
            Assert.That(spec.MarkdownText, Does.Contain("| --- | --- |"));
            Assert.That(spec.MarkdownText, Does.Contain("| value 1 | value 2 |"));
        }

        [Test]
        public void OpenXmlDocumentReader_GeneratesExcelMarkdownTable()
        {
            var path = Path.Combine(_dir, "spec.xlsx");
            Directory.CreateDirectory(_dir);
            CreateXlsx(path);

            var spec = new OpenXmlDocumentReader().Read(path);

            Assert.That(spec.MarkdownText, Does.Contain("## Sheet1"));
            Assert.That(spec.MarkdownText, Does.Contain("| Open browser | Read table |"));
        }

        [Test]
        public void OpenXmlDocumentReader_PreservesNumberedListInTableCell()
        {
            var path = Path.Combine(_dir, "list.docx");
            Directory.CreateDirectory(_dir);
            CreateDocxWithNumberedListInCell(path);

            var spec = new OpenXmlDocumentReader().Read(path);

            Assert.That(spec.MarkdownText, Does.Contain("1. item 1<br>2. item 2<br>3. item 3"));
            Assert.That(spec.RawText, Does.Contain("1. item 1\n2. item 2\n3. item 3"));
        }

        private static void CreateDocxStructured(string path)
        {
            using (var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document))
            {
                var main = doc.AddMainDocumentPart();
                main.Document = new Document(new Body());
                main.Document.Body.AppendChild(new Paragraph(
                    new ParagraphProperties(new ParagraphStyleId { Val = "Heading1" }),
                    new Run(new Text("Heading"))));
                var table = new DocumentFormat.OpenXml.Wordprocessing.Table(
                    new TableRow(
                        new TableCell(new Paragraph(new Run(new Text("Column A")))),
                        new TableCell(new Paragraph(new Run(new Text("Column B"))))),
                    new TableRow(
                        new TableCell(new Paragraph(new Run(new Text("value 1")))),
                        new TableCell(new Paragraph(new Run(new Text("value 2"))))));
                main.Document.Body.AppendChild(table);
                main.Document.Save();
            }
        }

        private static void CreateDocxWithNumberedListInCell(string path)
        {
            using (var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document))
            {
                var main = doc.AddMainDocumentPart();
                main.Document = new Document(new Body());

                var numberingPart = main.AddNewPart<NumberingDefinitionsPart>();
                numberingPart.Numbering = new Numbering(
                    new AbstractNum(
                        new Level(
                            new StartNumberingValue { Val = 1 },
                            new NumberingFormat { Val = NumberFormatValues.Decimal },
                            new LevelText { Val = "%1." })
                        { LevelIndex = 0 })
                    { AbstractNumberId = 0 },
                    new NumberingInstance(
                        new AbstractNumId { Val = 0 })
                    { NumberID = 1 });

                var listCell = new TableCell();
                for (int i = 1; i <= 3; i++)
                {
                    listCell.Append(new Paragraph(
                        new ParagraphProperties(
                            new NumberingProperties(
                                new NumberingLevelReference { Val = 0 },
                                new NumberingId { Val = 1 })),
                        new Run(new Text("item " + i))));
                }

                var table = new DocumentFormat.OpenXml.Wordprocessing.Table(
                    new TableRow(
                        new TableCell(new Paragraph(new Run(new Text("Header")))),
                        listCell));
                main.Document.Body.AppendChild(table);
                main.Document.Save();
            }
        }

        private static void CreateXlsx(string path)
        {
            using (var doc = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook))
            {
                var workbookPart = doc.AddWorkbookPart();
                workbookPart.Workbook = new DocumentFormat.OpenXml.Spreadsheet.Workbook();
                var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
                worksheetPart.Worksheet = new DocumentFormat.OpenXml.Spreadsheet.Worksheet(new DocumentFormat.OpenXml.Spreadsheet.SheetData());
                var sheets = workbookPart.Workbook.AppendChild(new DocumentFormat.OpenXml.Spreadsheet.Sheets());
                var sheet = new DocumentFormat.OpenXml.Spreadsheet.Sheet { Id = workbookPart.GetIdOfPart(worksheetPart), SheetId = 1, Name = "Sheet1" };
                sheets.Append(sheet);

                var sheetData = worksheetPart.Worksheet.GetFirstChild<DocumentFormat.OpenXml.Spreadsheet.SheetData>();
                var row = new DocumentFormat.OpenXml.Spreadsheet.Row();
                row.Append(new DocumentFormat.OpenXml.Spreadsheet.Cell { CellValue = new DocumentFormat.OpenXml.Spreadsheet.CellValue("Open browser"), DataType = DocumentFormat.OpenXml.Spreadsheet.CellValues.String });
                row.Append(new DocumentFormat.OpenXml.Spreadsheet.Cell { CellValue = new DocumentFormat.OpenXml.Spreadsheet.CellValue("Read table"), DataType = DocumentFormat.OpenXml.Spreadsheet.CellValues.String });
                sheetData.Append(row);

                worksheetPart.Worksheet.Save();
                workbookPart.Workbook.Save();
            }
        }

        private static void CreateDocx(string path, params string[] paragraphs)
        {
            using (var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document))
            {
                var main = doc.AddMainDocumentPart();
                main.Document = new Document(new Body());
                foreach (var text in paragraphs)
                    main.Document.Body.AppendChild(new Paragraph(new Run(new Text(text))));
                main.Document.Save();
            }
        }

        private static void CreateXls(string path)
        {
            var workbook = new HSSFWorkbook();
            var sheet = workbook.CreateSheet("Sheet1");
            var row = sheet.CreateRow(0);
            row.CreateCell(0).SetCellValue("Open browser");
            row.CreateCell(1).SetCellValue("Read table");
            using (var fs = File.Create(path))
            {
                workbook.Write(fs);
            }
            workbook.Close();
        }

        [Test]
        public void OpenXmlDocumentReader_ExtractsWordImage()
        {
            var path = Path.Combine(_dir, "spec.docx");
            var mediaDir = Path.Combine(_dir, "media");
            Directory.CreateDirectory(_dir);
            CreateDocxWithImage(path);

            var spec = new OpenXmlDocumentReader().Read(path, mediaDir);

            Assert.That(spec.MarkdownText, Does.Contain("![image](media/image1.png)"));
            Assert.That(File.Exists(Path.Combine(mediaDir, "image1.png")), Is.True);
        }

        [Test]
        public void OpenXmlDocumentReader_ExtractsExcelImage()
        {
            var path = Path.Combine(_dir, "spec.xlsx");
            var mediaDir = Path.Combine(_dir, "media");
            Directory.CreateDirectory(_dir);
            CreateXlsxWithImage(path);

            var spec = new OpenXmlDocumentReader().Read(path, mediaDir);

            Assert.That(spec.MarkdownText, Does.Contain("![image](media/image1.png)"));
            Assert.That(File.Exists(Path.Combine(mediaDir, "image1.png")), Is.True);
        }

        private static readonly byte[] PngBytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");

        private static void CreateDocxWithImage(string path)
        {
            using (var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document))
            {
                var main = doc.AddMainDocumentPart();
                main.Document = new Document(new Body());

                var imagePart = main.AddImagePart(ImagePartType.Png);
                using (var ms = new MemoryStream(PngBytes))
                    imagePart.FeedData(ms);
                var relId = main.GetIdOfPart(imagePart);

                var inline = new Wp.Inline();
                inline.Append(new Wp.Extent { Cx = 990000L, Cy = 990000L });
                inline.Append(new Wp.DocProperties { Id = 1U, Name = "Picture 1" });

                var nvPicPr = new Pic.NonVisualPictureProperties();
                nvPicPr.Append(new Pic.NonVisualDrawingProperties { Id = 0U, Name = "Picture 1" });
                nvPicPr.Append(new Pic.NonVisualPictureDrawingProperties());

                var blipFill = new Pic.BlipFill();
                blipFill.Append(new A.Blip { Embed = relId });
                var stretch = new A.Stretch();
                stretch.Append(new A.FillRectangle());
                blipFill.Append(stretch);

                var picture = new Pic.Picture();
                picture.Append(nvPicPr);
                picture.Append(blipFill);

                var graphicData = new A.GraphicData(picture) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" };
                var graphic = new A.Graphic(graphicData);
                inline.Append(graphic);

                var drawing = new DocumentFormat.OpenXml.Wordprocessing.Drawing(inline);
                main.Document.Body.AppendChild(new Paragraph(new Run(drawing)));
                main.Document.Save();
            }
        }

        private static void CreateXlsxWithImage(string path)
        {
            using (var doc = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook))
            {
                var workbookPart = doc.AddWorkbookPart();
                workbookPart.Workbook = new DocumentFormat.OpenXml.Spreadsheet.Workbook();
                var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
                var worksheet = new DocumentFormat.OpenXml.Spreadsheet.Worksheet();
                var sheetData = new DocumentFormat.OpenXml.Spreadsheet.SheetData();
                worksheet.Append(sheetData);
                worksheetPart.Worksheet = worksheet;

                var drawingsPart = worksheetPart.AddNewPart<DrawingsPart>();
                drawingsPart.WorksheetDrawing = new Xdr.WorksheetDrawing();

                var imagePart = drawingsPart.AddImagePart(ImagePartType.Png);
                using (var ms = new MemoryStream(PngBytes))
                    imagePart.FeedData(ms);
                var relId = drawingsPart.GetIdOfPart(imagePart);

                var fromMarker = new Xdr.FromMarker();
                fromMarker.Append(new Xdr.ColumnId("0"));
                fromMarker.Append(new Xdr.ColumnOffset("0"));
                fromMarker.Append(new Xdr.RowId("0"));
                fromMarker.Append(new Xdr.RowOffset("0"));

                var toMarker = new Xdr.ToMarker();
                toMarker.Append(new Xdr.ColumnId("1"));
                toMarker.Append(new Xdr.ColumnOffset("0"));
                toMarker.Append(new Xdr.RowId("1"));
                toMarker.Append(new Xdr.RowOffset("0"));

                var nvPicPr = new Xdr.NonVisualPictureProperties();
                nvPicPr.Append(new Xdr.NonVisualDrawingProperties { Id = 0U, Name = "Picture 1" });
                nvPicPr.Append(new Xdr.NonVisualPictureDrawingProperties());

                var blipFill = new Xdr.BlipFill();
                blipFill.Append(new A.Blip { Embed = relId });
                var stretch = new A.Stretch();
                stretch.Append(new A.FillRectangle());
                blipFill.Append(stretch);

                var picture = new Xdr.Picture();
                picture.Append(nvPicPr);
                picture.Append(blipFill);

                var anchor = new Xdr.TwoCellAnchor();
                anchor.Append(fromMarker);
                anchor.Append(toMarker);
                anchor.Append(picture);
                anchor.Append(new Xdr.ClientData());

                drawingsPart.WorksheetDrawing.Append(anchor);
                drawingsPart.WorksheetDrawing.Save();

                worksheet.Append(new DocumentFormat.OpenXml.Spreadsheet.Drawing { Id = worksheetPart.GetIdOfPart(drawingsPart) });

                var sheets = workbookPart.Workbook.AppendChild(new DocumentFormat.OpenXml.Spreadsheet.Sheets());
                var sheet = new DocumentFormat.OpenXml.Spreadsheet.Sheet { Id = workbookPart.GetIdOfPart(worksheetPart), SheetId = 1, Name = "Sheet1" };
                sheets.Append(sheet);

                var row = new DocumentFormat.OpenXml.Spreadsheet.Row();
                row.Append(new DocumentFormat.OpenXml.Spreadsheet.Cell { CellValue = new DocumentFormat.OpenXml.Spreadsheet.CellValue("Open browser"), DataType = DocumentFormat.OpenXml.Spreadsheet.CellValues.String });
                sheetData.Append(row);

                worksheet.Save();
                workbookPart.Workbook.Save();
            }
        }
    }
}
