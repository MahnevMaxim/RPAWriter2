using System;
using System.IO;
using System.Linq;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DWP = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using NUnit.Framework;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class MarkdownToDocxConverterTests
    {
        private string _dir;

        private static readonly byte[] Png1x1 = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");

        [SetUp]
        public void SetUp() => _dir = Path.Combine(Path.GetTempPath(), "RPAWriterTests", Path.GetRandomFileName());

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        [Test]
        public void Convert_WritesTextHeadingsListAndImage()
        {
            Directory.CreateDirectory(_dir);
            var mediaDir = Path.Combine(_dir, "media");
            Directory.CreateDirectory(mediaDir);
            File.WriteAllBytes(Path.Combine(mediaDir, "image1.png"), new byte[] { 1, 2, 3, 4 });

            var mdPath = Path.Combine(_dir, "spec.md");
            File.WriteAllText(
                mdPath,
                "# Заголовок\n\nАбзац текста.\n\n- пункт один\n- пункт два\n\n![картинка](media/image1.png)\n",
                Encoding.UTF8);

            var docxPath = Path.Combine(_dir, "spec.docx");
            var result = new MarkdownToDocxConverter().Convert(mdPath, docxPath);

            Assert.That(File.Exists(docxPath), Is.True);
            Assert.That(result.Images, Is.EqualTo(1));
            Assert.That(result.Paragraphs, Is.GreaterThan(0));

            using (var doc = WordprocessingDocument.Open(docxPath, false))
            {
                var body = doc.MainDocumentPart.Document.Body;
                Assert.That(body.InnerText, Does.Contain("Заголовок"));
                Assert.That(body.InnerText, Does.Contain("Абзац текста."));
                Assert.That(body.InnerText, Does.Contain("пункт один"));
                Assert.That(doc.MainDocumentPart.ImageParts.Count(), Is.EqualTo(1));
            }
        }

        [Test]
        public void Convert_UsesExplicitImageSizeFromAttributes()
        {
            Directory.CreateDirectory(_dir);
            var mediaDir = Path.Combine(_dir, "media");
            Directory.CreateDirectory(mediaDir);
            File.WriteAllBytes(Path.Combine(mediaDir, "image1.png"), Png1x1);

            var mdPath = Path.Combine(_dir, "spec.md");
            File.WriteAllText(mdPath, "![картинка](media/image1.png){width=104 height=104}\n", Encoding.UTF8);

            var docxPath = Path.Combine(_dir, "spec.docx");
            var result = new MarkdownToDocxConverter().Convert(mdPath, docxPath);

            Assert.That(result.Images, Is.EqualTo(1));

            using (var doc = WordprocessingDocument.Open(docxPath, false))
            {
                var extent = doc.MainDocumentPart.Document.Body
                    .Descendants<DWP.Extent>()
                    .First();
                Assert.That(extent.Cx.Value, Is.EqualTo(990600L));
                Assert.That(extent.Cy.Value, Is.EqualTo(990600L));
            }
        }

        [Test]
        public void Convert_UsesNaturalImageSizeWhenNoSizeIsGiven()
        {
            Directory.CreateDirectory(_dir);
            var mediaDir = Path.Combine(_dir, "media");
            Directory.CreateDirectory(mediaDir);
            File.WriteAllBytes(Path.Combine(mediaDir, "image1.png"), Png1x1);

            var mdPath = Path.Combine(_dir, "spec.md");
            File.WriteAllText(mdPath, "![картинка](media/image1.png)\n", Encoding.UTF8);

            var docxPath = Path.Combine(_dir, "spec.docx");
            var result = new MarkdownToDocxConverter().Convert(mdPath, docxPath);

            Assert.That(result.Images, Is.EqualTo(1));

            using (var doc = WordprocessingDocument.Open(docxPath, false))
            {
                var extent = doc.MainDocumentPart.Document.Body
                    .Descendants<DWP.Extent>()
                    .First();
                Assert.That(extent.Cx.Value, Is.EqualTo(9525L));
                Assert.That(extent.Cy.Value, Is.EqualTo(9525L));
            }
        }
    }
}
