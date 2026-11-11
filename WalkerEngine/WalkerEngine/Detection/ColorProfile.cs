namespace WalkerEngine;

/// <summary>
/// Какие цвета считать пустотой, а какие — маркером персонажа.
/// По умолчанию персонаж — светящаяся зелёная точка: зелёный канал заметно сильнее красного и синего.
/// Так собирается вся точка вместе с полупрозрачным краем, а серая карта и синие/красные значки не мешают.
/// </summary>
public sealed class ColorProfile
{
    /// <summary>Пиксель темнее этого порога по всем каналам — чёрный фон, не карта.</summary>
    public int BackgroundCeiling { get; init; } = 21;

    /// <summary>Пиксель прозрачнее этого порога не принадлежит карте.</summary>
    public int TransparentCeiling { get; init; } = 16;

    /// <summary>Минимальная яркость зелёного канала у маркера персонажа.</summary>
    public int PlayerMinGreen { get; init; } = 90;

    /// <summary>Насколько зелёный канал должен превышать красный и синий.</summary>
    public int PlayerGreenMargin { get; init; } = 28;

    /// <summary>
    /// Если задан, персонаж ищется по близости к этому цвету, а не по правилу зелёного маркера.
    /// Подходит для плоской точки одного цвета. Для светящейся зелёной точки правило по умолчанию точнее.
    /// </summary>
    public Rgba32? MarkerColor { get; init; }

    /// <summary>Допуск по евклидову расстоянию в RGB, когда задан <see cref="MarkerColor"/>.</summary>
    public int MarkerTolerance { get; init; } = 48;

    public static ColorProfile GreenMarker { get; } = new();

    public static ColorProfile ForMarker(Rgba32 color, int tolerance = 48) => new()
    {
        MarkerColor = color,
        MarkerTolerance = tolerance,
    };

    public bool IsBackground(byte r, byte g, byte b, byte a)
    {
        if (a <= TransparentCeiling)
            return true;
        return r <= BackgroundCeiling && g <= BackgroundCeiling && b <= BackgroundCeiling;
    }

    public bool IsPlayer(byte r, byte g, byte b)
    {
        if (MarkerColor is { } target)
        {
            int dr = r - target.R;
            int dg = g - target.G;
            int db = b - target.B;
            int limit = MarkerTolerance;
            return dr * dr + dg * dg + db * db <= limit * limit;
        }

        return g >= PlayerMinGreen
            && g >= r + PlayerGreenMargin
            && g >= b + PlayerGreenMargin;
    }
}
