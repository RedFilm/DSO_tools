namespace WalkerEngine;

/// <summary>Цвет пикселя: красный, зелёный, синий и непрозрачность, каждый канал 0..255.</summary>
public readonly record struct Rgba32(byte R, byte G, byte B, byte A)
{
    public static Rgba32 Opaque(byte r, byte g, byte b) => new(r, g, b, 255);
}
