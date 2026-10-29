using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Signatures;

// Reads back the PNGs the package writes: true colour with alpha at 8 bits, the rows unfiltered in the IDAT chunks.
internal static class PngPixels
{
    public static (int Width, int Height, byte[] Rgba) Decode(byte[] png)
    {
        var at = 8;
        var (width, height) = (0, 0);
        using var data = new MemoryStream();
        while (at < png.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            var type = Encoding.ASCII.GetString(png, at + 4, 4);
            if (type == "IHDR")
            {
                width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at + 8));
                height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at + 12));
                Assert.That((png[at + 16], png[at + 17]), Is.EqualTo(((byte)8, (byte)6)), "8 bits of true colour with alpha");
            }
            else if (type == "IDAT")
            {
                data.Write(png, at + 8, length);
            }

            at += 12 + length;
        }

        data.Position = 0;
        using var zlib = new ZLibStream(data, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        zlib.CopyTo(raw);
        var bytes = raw.ToArray();
        Assert.That(bytes, Has.Length.EqualTo(height * ((width * 4) + 1)));
        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            Assert.That(bytes[y * ((width * 4) + 1)], Is.Zero, "each row unfiltered");
            Array.Copy(bytes, (y * ((width * 4) + 1)) + 1, rgba, y * width * 4, width * 4);
        }

        return (width, height, rgba);
    }

    // The alpha of each pixel, row by row.
    public static byte[] Alpha(byte[] png) => [.. Decode(png).Rgba.Where((_, at) => at % 4 == 3)];

    // The smallest box around every pixel with any ink: left, top, right and bottom; null for an image without ink.
    public static (int Left, int Top, int Right, int Bottom)? Ink(byte[] png)
    {
        var (width, height, rgba) = Decode(png);
        (int Left, int Top, int Right, int Bottom)? box = null;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (rgba[(((y * width) + x) * 4) + 3] > 0)
                {
                    box = box is { } seen ? (Math.Min(seen.Left, x), Math.Min(seen.Top, y), Math.Max(seen.Right, x), Math.Max(seen.Bottom, y)) : (x, y, x, y);
                }
            }
        }

        return box;
    }
}
