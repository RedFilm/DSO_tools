using WalkerEngine;

namespace WalkerEngine.Tests;

public class QuestMarkFinderTests
{
    [Fact]
    public void QuestionMark_IsFound_ChestBlueAndDoorAreIgnored()
    {
        var image = new ColorImage(120, 120);
        Fill(image, 20, 20, 10, 12, new Rgba32(40, 200, 120, 255));
        Fill(image, 60, 20, 16, 16, new Rgba32(40, 80, 200, 255));
        Fill(image, 90, 20, 6, 14, new Rgba32(40, 90, 170, 255));

        List<QuestMark> marks = QuestMarkFinder.FindAll(image, new MapBounds(0, 0, 119, 119), QuestMarkKind.Question);

        Assert.Single(marks);
        Assert.Equal(QuestMarkKind.Question, marks[0].Kind);
        Assert.InRange(marks[0].Point.X, 0.15, 0.35);
        Assert.InRange(marks[0].Point.Y, 0.15, 0.35);
    }

    [Fact]
    public void ExclaimMark_IsFound_WideYellowDiskIsIgnored()
    {
        var image = new ColorImage(100, 100);
        Fill(image, 15, 15, 6, 14, new Rgba32(220, 180, 40, 255));
        Fill(image, 50, 20, 18, 12, new Rgba32(220, 180, 40, 255));

        List<QuestMark> marks = QuestMarkFinder.FindAll(image, new MapBounds(0, 0, 99, 99), QuestMarkKind.Exclaim);

        Assert.Single(marks);
        Assert.Equal(QuestMarkKind.Exclaim, marks[0].Kind);
        Assert.True(marks[0].Point.X < 0.4);
    }

    private static void Fill(ColorImage image, int x, int y, int width, int height, Rgba32 color)
    {
        for (int py = y; py < y + height; py++)
        {
            for (int px = x; px < x + width; px++)
                image[px, py] = color;
        }
    }
}
