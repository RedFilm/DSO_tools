namespace WalkerEngine;

/// <summary>
/// Кадр карты в памяти. Движок читает положение персонажа прямо с этих пикселей.
/// </summary>
public sealed class ColorImage
{
    private readonly byte[] _rgba;

    public ColorImage(int width, int height)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));

        Width = width;
        Height = height;
        _rgba = new byte[width * height * 4];
    }

    public ColorImage(int width, int height, byte[] rgba)
        : this(width, height, rgba, owned: false)
    {
    }

    private ColorImage(int width, int height, byte[] rgba, bool owned)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));
        if (rgba.Length != width * height * 4)
            throw new ArgumentException("Буфер должен содержать width * height * 4 байт в порядке RGBA.", nameof(rgba));

        Width = width;
        Height = height;
        if (owned)
        {
            _rgba = rgba;
        }
        else
        {
            _rgba = new byte[rgba.Length];
            rgba.CopyTo(_rgba.AsSpan());
        }
    }

    public int Width { get; }

    public int Height { get; }

    public Rgba32 this[int x, int y]
    {
        get
        {
            int i = Index(x, y);
            return new Rgba32(_rgba[i], _rgba[i + 1], _rgba[i + 2], _rgba[i + 3]);
        }
        set
        {
            int i = Index(x, y);
            _rgba[i] = value.R;
            _rgba[i + 1] = value.G;
            _rgba[i + 2] = value.B;
            _rgba[i + 3] = value.A;
        }
    }

    public static ColorImage Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return PngCodec.Read(File.ReadAllBytes(path));
    }

    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        File.WriteAllBytes(path, PngCodec.Write(this));
    }

    public ColorImage Clone()
    {
        var copy = new byte[_rgba.Length];
        _rgba.CopyTo(copy.AsSpan());
        return new ColorImage(Width, Height, copy, owned: true);
    }

    internal byte[] Pixels => _rgba;

    internal static ColorImage FromOwnedBuffer(int width, int height, byte[] rgba)
        => new(width, height, rgba, owned: true);

    private int Index(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
            throw new ArgumentOutOfRangeException(nameof(x), "Пиксель за пределами кадра.");
        return (y * Width + x) * 4;
    }
}
