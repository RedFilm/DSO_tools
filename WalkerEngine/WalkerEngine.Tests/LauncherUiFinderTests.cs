using WalkerEngine;

namespace WalkerEngine.Tests;

public class LauncherUiFinderTests
{
    [Fact]
    public void LoginScreenshot_FindsLoginGreenButton()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "launcher-login.png");
        Assert.True(File.Exists(path), path);
        ColorImage image = ColorImage.Load(path);

        Assert.True(LauncherUiFinder.TryFindGreenButton(image, out var kind, out int x, out int y));
        Assert.Equal(LauncherUiFinder.GreenButtonKind.Login, kind);
        Assert.InRange(x, image.Width * 60 / 100, image.Width - 1);
        Assert.InRange(y, image.Height * 30 / 100, image.Height * 60 / 100);
    }

    [Fact]
    public void PlayScreenshot_FindsPlayGreenButton()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "launcher-play.png");
        Assert.True(File.Exists(path), path);
        ColorImage image = ColorImage.Load(path);

        Assert.True(LauncherUiFinder.TryFindGreenButton(image, out var kind, out int x, out int y));
        Assert.Equal(LauncherUiFinder.GreenButtonKind.Play, kind);
        Assert.InRange(x, image.Width * 60 / 100, image.Width - 1);
        Assert.True(y < image.Height * 30 / 100);
    }

    [Fact]
    public void CharacterSelect_HasShopIcon()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "character-select.png");
        Assert.True(File.Exists(path), path);
        ColorImage image = ColorImage.Load(path);

        Assert.True(LauncherUiFinder.HasShopIcon(image));
        Assert.False(LauncherUiFinder.TryFindGreenButton(image, out _, out _, out _));
    }
}
