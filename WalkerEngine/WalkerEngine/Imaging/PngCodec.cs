using System.Buffers.Binary;
using System.IO.Compression;

namespace WalkerEngine;

/// <summary>Чтение и запись PNG без внешних библиотек. Нужен, чтобы движок работал прямо с картинкой карты.</summary>
internal static class PngCodec
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly uint[] CrcTable = CreateCrcTable();

    public static ColorImage Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8 || !data.StartsWith(Signature))
            throw new InvalidDataException("Файл не является PNG.");

        int width = 0;
        int height = 0;
        int bitDepth = 0;
        int colorType = -1;
        bool seenHeader = false;
        using var idat = new MemoryStream();

        int offset = 8;
        while (offset + 12 <= data.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(data.Slice(offset, 4));
            if (length < 0 || offset + 12L + length > data.Length)
                throw new InvalidDataException("Повреждённый блок PNG.");

            var type = data.Slice(offset + 4, 4);
            var chunk = data.Slice(offset + 8, length);
            offset += 12 + length;

            if (type.SequenceEqual("IHDR"u8))
            {
                if (length < 13)
                    throw new InvalidDataException("Заголовок PNG повреждён.");

                width = BinaryPrimitives.ReadInt32BigEndian(chunk);
                height = BinaryPrimitives.ReadInt32BigEndian(chunk[4..]);
                bitDepth = chunk[8];
                colorType = chunk[9];
                if (chunk[10] != 0 || chunk[11] != 0)
                    throw new InvalidDataException("Неподдерживаемое сжатие PNG.");
                if (chunk[12] != 0)
                    throw new InvalidDataException("Черезстрочный PNG не поддерживается.");
                seenHeader = true;
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                idat.Write(chunk);
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                break;
            }
        }

        if (!seenHeader || width <= 0 || height <= 0)
            throw new InvalidDataException("В PNG нет корректного заголовка.");
        if (bitDepth != 8)
            throw new InvalidDataException($"Поддерживается только 8 бит на канал, в файле {bitDepth}.");

        int bytesPerPixel = colorType switch
        {
            0 => 1,
            2 => 3,
            4 => 2,
            6 => 4,
            3 => throw new InvalidDataException("PNG с палитрой не поддерживается."),
            _ => throw new InvalidDataException($"Цветовой тип PNG {colorType} не поддерживается."),
        };

        byte[] compressed = idat.ToArray();
        if (compressed.Length == 0)
            throw new InvalidDataException("В PNG нет данных изображения.");

        byte[] raw;
        using (var input = new MemoryStream(compressed, writable: false))
        using (var zlib = new ZLibStream(input, CompressionMode.Decompress))
        using (var output = new MemoryStream())
        {
            zlib.CopyTo(output);
            raw = output.ToArray();
        }

        byte[] scan = Unfilter(raw, width, height, bytesPerPixel);
        byte[] rgba = ToRgba(scan, width, height, colorType);
        return ColorImage.FromOwnedBuffer(width, height, rgba);
    }

    public static byte[] Write(ColorImage image)
    {
        int stride = image.Width * 4;
        var raw = new byte[image.Height * (stride + 1)];
        var pixels = image.Pixels;
        for (int y = 0; y < image.Height; y++)
        {
            int rawRow = y * (stride + 1);
            raw[rawRow] = 0;
            pixels.AsSpan(y * stride, stride).CopyTo(raw.AsSpan(rawRow + 1, stride));
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(raw);
        byte[] idat = compressed.ToArray();

        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, image.Width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), image.Height);
        ihdr[8] = 8;
        ihdr[9] = 6;

        using var output = new MemoryStream();
        output.Write(Signature);
        WriteChunk(output, "IHDR"u8, ihdr);
        WriteChunk(output, "IDAT"u8, idat);
        WriteChunk(output, "IEND"u8, ReadOnlySpan<byte>.Empty);
        return output.ToArray();
    }

    private static byte[] Unfilter(byte[] raw, int width, int height, int bytesPerPixel)
    {
        int stride = width * bytesPerPixel;
        if (raw.Length < height * (stride + 1))
            throw new InvalidDataException("В PNG не хватает строк изображения.");

        var pixels = new byte[height * stride];
        for (int y = 0; y < height; y++)
        {
            int filterIndex = y * (stride + 1);
            byte filter = raw[filterIndex];
            int source = filterIndex + 1;
            int destination = y * stride;
            for (int i = 0; i < stride; i++)
            {
                byte left = i >= bytesPerPixel ? pixels[destination + i - bytesPerPixel] : (byte)0;
                byte up = y > 0 ? pixels[destination - stride + i] : (byte)0;
                byte upLeft = y > 0 && i >= bytesPerPixel ? pixels[destination - stride + i - bytesPerPixel] : (byte)0;
                byte predicted = filter switch
                {
                    0 => 0,
                    1 => left,
                    2 => up,
                    3 => (byte)((left + up) / 2),
                    4 => Paeth(left, up, upLeft),
                    _ => throw new InvalidDataException($"Неизвестный фильтр PNG: {filter}."),
                };
                pixels[destination + i] = (byte)(raw[source + i] + predicted);
            }
        }

        return pixels;
    }

    private static byte[] ToRgba(byte[] pixels, int width, int height, int colorType)
    {
        int count = width * height;
        var rgba = new byte[count * 4];
        switch (colorType)
        {
            case 6:
                pixels.CopyTo(rgba.AsSpan());
                break;
            case 2:
                for (int i = 0, source = 0, destination = 0; i < count; i++, source += 3, destination += 4)
                {
                    rgba[destination] = pixels[source];
                    rgba[destination + 1] = pixels[source + 1];
                    rgba[destination + 2] = pixels[source + 2];
                    rgba[destination + 3] = 255;
                }
                break;
            case 0:
                for (int i = 0, destination = 0; i < count; i++, destination += 4)
                {
                    rgba[destination] = rgba[destination + 1] = rgba[destination + 2] = pixels[i];
                    rgba[destination + 3] = 255;
                }
                break;
            case 4:
                for (int i = 0, source = 0, destination = 0; i < count; i++, source += 2, destination += 4)
                {
                    rgba[destination] = rgba[destination + 1] = rgba[destination + 2] = pixels[source];
                    rgba[destination + 3] = pixels[source + 1];
                }
                break;
        }

        return rgba;
    }

    private static byte Paeth(byte left, byte up, byte upLeft)
    {
        int estimate = left + up - upLeft;
        int distanceLeft = Math.Abs(estimate - left);
        int distanceUp = Math.Abs(estimate - up);
        int distanceUpLeft = Math.Abs(estimate - upLeft);
        if (distanceLeft <= distanceUp && distanceLeft <= distanceUpLeft)
            return left;
        if (distanceUp <= distanceUpLeft)
            return up;
        return upLeft;
    }

    private static void WriteChunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteInt32BigEndian(header, data.Length);
        type.CopyTo(header[4..]);
        stream.Write(header);
        stream.Write(data);

        uint crc = 0xFFFF_FFFF;
        crc = UpdateCrc(crc, type);
        crc = UpdateCrc(crc, data);
        Span<byte> trailer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(trailer, crc ^ 0xFFFF_FFFF);
        stream.Write(trailer);
    }

    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (byte value in data)
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < table.Length; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB8_8320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }

        return table;
    }
}
