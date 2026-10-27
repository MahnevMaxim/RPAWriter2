using System;
using System.IO;
using NUnit.Framework;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class ImageDimensionsTests
    {
        // ---------- PNG ----------

        [Test]
        public void Read_Png_ReturnsDimensions()
        {
            var data = Png(800, 600);

            var (width, height) = ImageDimensions.Read(data, data.Length);

            Assert.That(width, Is.EqualTo(800));
            Assert.That(height, Is.EqualTo(600));
        }

        [Test]
        public void Read_PngFromFile_ReturnsDimensions()
        {
            var path = WriteTempFile(Png(320, 240));

            try
            {
                var (width, height) = ImageDimensions.Read(path);

                Assert.That(width, Is.EqualTo(320));
                Assert.That(height, Is.EqualTo(240));
            }
            finally
            {
                File.Delete(path);
            }
        }

        // ---------- JPEG ----------

        [Test]
        public void Read_Jpeg_ReturnsDimensions()
        {
            var data = Jpeg(320, 240);

            var (width, height) = ImageDimensions.Read(data, data.Length);

            Assert.That(width, Is.EqualTo(320));
            Assert.That(height, Is.EqualTo(240));
        }

        // ---------- GIF ----------

        [Test]
        public void Read_Gif_ReturnsDimensions()
        {
            var data = Gif(100, 50);

            var (width, height) = ImageDimensions.Read(data, data.Length);

            Assert.That(width, Is.EqualTo(100));
            Assert.That(height, Is.EqualTo(50));
        }

        // ---------- BMP ----------

        [Test]
        public void Read_Bmp_ReturnsDimensions()
        {
            var data = Bmp(64, 48);

            var (width, height) = ImageDimensions.Read(data, data.Length);

            Assert.That(width, Is.EqualTo(64));
            Assert.That(height, Is.EqualTo(48));
        }

        [Test]
        public void Read_Bmp_NegativeHeight_ReturnsAbsoluteHeight()
        {
            var data = Bmp(64, -48);

            var (width, height) = ImageDimensions.Read(data, data.Length);

            Assert.That(width, Is.EqualTo(64));
            Assert.That(height, Is.EqualTo(48));
        }

        // ---------- invalid / missing input ----------

        [Test]
        public void Read_NullData_ReturnsZero()
        {
            Assert.That(ImageDimensions.Read(null, 0), Is.EqualTo((0, 0)));
        }

        [Test]
        public void Read_TooShortData_ReturnsZero()
        {
            Assert.That(ImageDimensions.Read(new byte[] { 0xFF }, 1), Is.EqualTo((0, 0)));
        }

        [Test]
        public void Read_NonImageData_ReturnsZero()
        {
            var data = System.Text.Encoding.ASCII.GetBytes("hello, this is not an image");

            Assert.That(ImageDimensions.Read(data, data.Length), Is.EqualTo((0, 0)));
        }

        [Test]
        public void Read_MissingFile_ReturnsZero()
        {
            Assert.That(
                ImageDimensions.Read(Path.Combine(Path.GetTempPath(), "rpawriter_missing_" + Guid.NewGuid().ToString("N") + ".png")),
                Is.EqualTo((0, 0)));
        }

        [Test]
        public void Read_NullOrEmptyPath_ReturnsZero()
        {
            Assert.That(ImageDimensions.Read(null), Is.EqualTo((0, 0)));
            Assert.That(ImageDimensions.Read(""), Is.EqualTo((0, 0)));
            Assert.That(ImageDimensions.Read("   "), Is.EqualTo((0, 0)));
        }

        // ---------- helpers ----------

        private static byte[] Png(int width, int height)
        {
            var data = new byte[24];
            var signature = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            Array.Copy(signature, data, signature.Length);
            data[12] = 0x49; data[13] = 0x48; data[14] = 0x44; data[15] = 0x52; // "IHDR"
            WriteBigEndian32(data, 16, width);
            WriteBigEndian32(data, 20, height);
            return data;
        }

        private static byte[] Jpeg(int width, int height)
        {
            var data = new byte[11];
            data[0] = 0xFF; data[1] = 0xD8; // SOI
            data[2] = 0xFF; data[3] = 0xC0; // SOF0
            data[4] = 0x00; data[5] = 0x08; // segment length
            data[6] = 0x08;                 // precision
            data[7] = (byte)(height >> 8); data[8] = (byte)height;
            data[9] = (byte)(width >> 8); data[10] = (byte)width;
            return data;
        }

        private static byte[] Gif(int width, int height)
        {
            var data = new byte[10];
            data[0] = (byte)'G'; data[1] = (byte)'I'; data[2] = (byte)'F';
            data[3] = (byte)'8'; data[4] = (byte)'9'; data[5] = (byte)'a';
            WriteLittleEndian16(data, 6, width);
            WriteLittleEndian16(data, 8, height);
            return data;
        }

        private static byte[] Bmp(int width, int height)
        {
            var data = new byte[26];
            data[0] = (byte)'B'; data[1] = (byte)'M';
            WriteLittleEndian32(data, 18, width);
            WriteLittleEndian32(data, 22, height);
            return data;
        }

        private static void WriteBigEndian32(byte[] data, int offset, int value)
        {
            data[offset] = (byte)((value >> 24) & 0xFF);
            data[offset + 1] = (byte)((value >> 16) & 0xFF);
            data[offset + 2] = (byte)((value >> 8) & 0xFF);
            data[offset + 3] = (byte)(value & 0xFF);
        }

        private static void WriteLittleEndian16(byte[] data, int offset, int value)
        {
            data[offset] = (byte)(value & 0xFF);
            data[offset + 1] = (byte)((value >> 8) & 0xFF);
        }

        private static void WriteLittleEndian32(byte[] data, int offset, int value)
        {
            var v = unchecked((uint)value);
            data[offset] = (byte)(v & 0xFF);
            data[offset + 1] = (byte)((v >> 8) & 0xFF);
            data[offset + 2] = (byte)((v >> 16) & 0xFF);
            data[offset + 3] = (byte)((v >> 24) & 0xFF);
        }

        private static string WriteTempFile(byte[] data)
        {
            var path = Path.Combine(Path.GetTempPath(), "rpawriter_img_" + Guid.NewGuid().ToString("N") + ".bin");
            File.WriteAllBytes(path, data);
            return path;
        }
    }
}
