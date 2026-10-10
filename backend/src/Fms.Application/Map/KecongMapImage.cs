using System.IO.Compression;

namespace Fms.Application.Map;

public sealed class MapRaster
{
    public byte[] Bytes { get; init; } = [];
    public string ContentType { get; init; } = "image/png";
    public int Width { get; init; }
    public int Height { get; init; }
    public double OriginX { get; init; }
    public double TopY { get; init; }
    public double Resolution { get; init; }
    public string SourceName { get; init; } = "";
}

public static class KecongMapImage
{
    public static MapRaster? TryLoadSibling(string xmapPath, double minX, double minY, double maxX, double maxY, double headerResolution)
    {
        var directory = Path.GetDirectoryName(xmapPath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return null;

        var stem = Path.GetFileNameWithoutExtension(xmapPath);
        var names = new[]
        {
            stem + ".png", stem + ".jpg", stem + ".jpeg", stem + ".pgm",
            "map.png", "loc.png", "intensity.png", stem + "_map.png"
        };

        foreach (var name in names)
        {
            var path = Path.Combine(directory, name);
            if (!File.Exists(path)) continue;
            var raster = FromBytes(File.ReadAllBytes(path), Path.GetFileName(path), minX, minY, maxX, maxY, headerResolution);
            if (raster != null) return raster;
        }

        return null;
    }

    public static MapRaster? FromBytes(byte[] bytes, string sourceName, double minX, double minY, double maxX, double maxY, double headerResolution)
    {
        if (!TryDescribe(bytes, out var width, out var height, out var contentType, out var pngBytes))
            return null;

        var (originX, topY, resolution) = Place(width, height, minX, minY, maxX, maxY, headerResolution);
        return new MapRaster
        {
            Bytes = pngBytes ?? bytes,
            ContentType = pngBytes == null ? contentType : "image/png",
            Width = width,
            Height = height,
            OriginX = originX,
            TopY = topY,
            Resolution = resolution,
            SourceName = sourceName
        };
    }

    public static (double OriginX, double TopY, double Resolution) Place(int width, int height, double minX, double minY, double maxX, double maxY, double headerResolution)
    {
        var resolution = headerResolution > 0 ? headerResolution : 0.05;
        if (headerResolution <= 0 && width > 0 && maxX > minX)
            resolution = (maxX - minX) / width;

        var topY = maxY > minY ? maxY : minY + height * resolution;
        return (minX, topY, resolution);
    }

    private static bool TryDescribe(byte[] bytes, out int width, out int height, out string contentType, out byte[]? pngBytes)
    {
        width = 0;
        height = 0;
        contentType = "application/octet-stream";
        pngBytes = null;
        if (bytes.Length < 8) return false;

        if (bytes[0] == 0x89 && bytes[1] == 0x50)
        {
            if (bytes.Length < 24) return false;
            width = ReadInt32(bytes, 16);
            height = ReadInt32(bytes, 20);
            contentType = "image/png";
            return width > 0 && height > 0;
        }

        if (bytes[0] == 0xFF && bytes[1] == 0xD8)
        {
            if (!TryJpegSize(bytes, out width, out height)) return false;
            contentType = "image/jpeg";
            return true;
        }

        if (bytes[0] == (byte)'P' && (bytes[1] == (byte)'5' || bytes[1] == (byte)'2'))
        {
            if (!TryPgm(bytes, out width, out height, out var gray)) return false;
            pngBytes = EncodeGrayPng(width, height, gray);
            contentType = "image/png";
            return pngBytes.Length > 0;
        }

        return false;
    }

    private static bool TryJpegSize(byte[] bytes, out int width, out int height)
    {
        width = 0;
        height = 0;
        var i = 2;
        while (i + 8 < bytes.Length)
        {
            if (bytes[i] != 0xFF) { i++; continue; }
            var marker = bytes[i + 1];
            if (marker == 0xC0 || marker == 0xC1 || marker == 0xC2)
            {
                height = (bytes[i + 5] << 8) + bytes[i + 6];
                width = (bytes[i + 7] << 8) + bytes[i + 8];
                return width > 0 && height > 0;
            }

            if (marker == 0xD8 || marker == 0xD9) { i += 2; continue; }
            if (i + 3 >= bytes.Length) return false;
            var length = (bytes[i + 2] << 8) + bytes[i + 3];
            if (length < 2) return false;
            i += 2 + length;
        }

        return false;
    }

    private static bool TryPgm(byte[] bytes, out int width, out int height, out byte[] gray)
    {
        width = 0;
        height = 0;
        gray = [];
        var text = System.Text.Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 256));
        var tokens = new List<string>();
        var index = 0;
        while (index < text.Length && tokens.Count < 4)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index])) index++;
            if (index >= text.Length) break;
            if (text[index] == '#')
            {
                while (index < text.Length && text[index] != '\n') index++;
                continue;
            }

            var start = index;
            while (index < text.Length && !char.IsWhiteSpace(text[index])) index++;
            tokens.Add(text[start..index]);
        }

        if (tokens.Count < 4 || (tokens[0] != "P5" && tokens[0] != "P2")) return false;
        if (!int.TryParse(tokens[1], out width) || !int.TryParse(tokens[2], out height) || width <= 0 || height <= 0) return false;
        var offset = 0;
        var seen = 0;
        while (offset < bytes.Length && seen < 4)
        {
            while (offset < bytes.Length && IsWs(bytes[offset])) offset++;
            if (offset < bytes.Length && bytes[offset] == (byte)'#')
            {
                while (offset < bytes.Length && bytes[offset] != (byte)'\n') offset++;
                continue;
            }

            while (offset < bytes.Length && !IsWs(bytes[offset])) offset++;
            seen++;
        }

        while (offset < bytes.Length && IsWs(bytes[offset])) offset++;
        var count = width * height;
        if (offset + count > bytes.Length) return false;
        gray = new byte[count];
        Buffer.BlockCopy(bytes, offset, gray, 0, count);
        return true;
    }

    private static bool IsWs(byte value) => value is (byte)' ' or (byte)'\n' or (byte)'\r' or (byte)'\t';

    private static byte[] EncodeGrayPng(int width, int height, byte[] gray)
    {
        var raw = new byte[(width + 1) * height];
        for (var y = 0; y < height; y++)
        {
            var row = y * (width + 1);
            raw[row] = 0;
            Buffer.BlockCopy(gray, y * width, raw, row + 1, width);
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            zlib.Write(raw);

        var idat = compressed.ToArray();
        using var png = new MemoryStream();
        png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        WriteChunk(png, "IHDR", BuildIhdr(width, height));
        WriteChunk(png, "IDAT", idat);
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static byte[] BuildIhdr(int width, int height)
    {
        var data = new byte[13];
        WriteInt32(data, 0, width);
        WriteInt32(data, 4, height);
        data[8] = 8;
        data[9] = 0;
        return data;
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        WriteInt32(length, 0, data.Length);
        stream.Write(length);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);
        var crc = Crc32(typeBytes, data);
        var crcBytes = new byte[4];
        WriteInt32(crcBytes, 0, crc);
        stream.Write(crcBytes);
    }

    private static int Crc32(byte[] type, byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        void Feed(byte value)
        {
            crc ^= value;
            for (var i = 0; i < 8; i++)
                crc = (crc & 1) == 1 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
        }

        foreach (var value in type) Feed(value);
        foreach (var value in data) Feed(value);
        return (int)~crc;
    }

    private static int ReadInt32(byte[] bytes, int offset) =>
        (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];

    private static void WriteInt32(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)(value >> 24);
        bytes[offset + 1] = (byte)(value >> 16);
        bytes[offset + 2] = (byte)(value >> 8);
        bytes[offset + 3] = (byte)value;
    }
}
