using System;
using System.IO;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Reads intrinsic image dimensions (width/height in pixels) from the headers of common
    /// raster formats (PNG, JPEG, GIF, BMP) without external dependencies. TIFF is not decoded.
    /// </summary>
    public static class ImageDimensions
    {
        public static (int Width, int Height) Read(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return (0, 0);

            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var buffer = new byte[64 * 1024];
                    int length = 0;
                    while (length < buffer.Length)
                    {
                        int read = stream.Read(buffer, length, buffer.Length - length);
                        if (read == 0) break;
                        length += read;
                    }
                    return Read(buffer, length);
                }
            }
            catch
            {
                return (0, 0);
            }
        }

        public static (int Width, int Height) Read(byte[] data, int length)
        {
            if (data == null || length < 2)
                return (0, 0);

            if (length >= 24 && IsPng(data))
                return ReadPng(data);
            if (length >= 2 && IsJpeg(data))
                return ReadJpeg(data, length);
            if (length >= 10 && IsGif(data))
                return ReadGif(data);
            if (length >= 26 && IsBmp(data))
                return ReadBmp(data);

            return (0, 0);
        }

        private static bool IsPng(byte[] d) =>
            d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47 &&
            d[4] == 0x0D && d[5] == 0x0A && d[6] == 0x1A && d[7] == 0x0A;

        private static (int, int) ReadPng(byte[] d)
        {
            // IHDR: width at offset 16, height at offset 20 (big-endian).
            return (ReadInt32BigEndian(d, 16), ReadInt32BigEndian(d, 20));
        }

        private static bool IsJpeg(byte[] d) => d[0] == 0xFF && d[1] == 0xD8;

        private static (int, int) ReadJpeg(byte[] data, int length)
        {
            int offset = 2;
            while (offset + 8 < length)
            {
                if (data[offset] != 0xFF)
                {
                    offset++;
                    continue;
                }

                int marker = data[offset + 1];

                // Fill bytes (0xFF) and standalone markers (0x01, 0xD0..0xD9) have no length field.
                if (marker == 0xFF || marker == 0x00 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD9))
                {
                    offset++;
                    continue;
                }

                // Start Of Frame markers (SOF0..SOF15) carry dimensions.
                if (marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
                {
                    int height = (data[offset + 5] << 8) | data[offset + 6];
                    int width = (data[offset + 7] << 8) | data[offset + 8];
                    return (width, height);
                }

                int segmentLength = (data[offset + 2] << 8) | data[offset + 3];
                if (segmentLength < 2)
                    break;
                offset += 2 + segmentLength;
            }

            return (0, 0);
        }

        private static bool IsGif(byte[] d) =>
            d[0] == (byte)'G' && d[1] == (byte)'I' && d[2] == (byte)'F';

        private static (int, int) ReadGif(byte[] d)
        {
            // GIF logical screen descriptor: width at offset 6, height at offset 8 (little-endian).
            return (d[6] | (d[7] << 8), d[8] | (d[9] << 8));
        }

        private static bool IsBmp(byte[] d) =>
            d[0] == (byte)'B' && d[1] == (byte)'M';

        private static (int, int) ReadBmp(byte[] d)
        {
            // BITMAPINFOHEADER: width at offset 18, height at offset 22 (little-endian, signed).
            int width = ReadInt32LittleEndian(d, 18);
            int height = ReadInt32LittleEndian(d, 22);
            return (width, Math.Abs(height));
        }

        private static int ReadInt32BigEndian(byte[] d, int offset) =>
            (d[offset] << 24) | (d[offset + 1] << 16) | (d[offset + 2] << 8) | d[offset + 3];

        private static int ReadInt32LittleEndian(byte[] d, int offset) =>
            d[offset] | (d[offset + 1] << 8) | (d[offset + 2] << 16) | (d[offset + 3] << 24);
    }
}
