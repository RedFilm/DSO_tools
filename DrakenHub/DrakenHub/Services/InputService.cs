using DrakenHub.Core;

namespace DrakenHub.Services;

internal sealed class InputService
{
    public Point GetCursorPosition()
    {
        WinApi.GetCursorPos(out var p);
        return new Point(p.X, p.Y);
    }

    public void MoveTo(int x, int y) => WinApi.SetCursorPos(x, y);

    public void MoveTo(Point p) => MoveTo(p.X, p.Y);

    public void LeftClick(int x, int y, int settleMs = 5)
    {
        MoveTo(x, y);
        Thread.Sleep(settleMs);
        SendMouse(WinApi.MouseeventfLeftDown);
        Thread.Sleep(5);
        SendMouse(WinApi.MouseeventfLeftUp);
    }

    public void RightClick(int x, int y, int settleMs = 5)
    {
        MoveTo(x, y);
        Thread.Sleep(settleMs);
        SendMouse(WinApi.MouseeventfRightDown);
        Thread.Sleep(5);
        SendMouse(WinApi.MouseeventfRightUp);
    }

    public void DoubleClick(int x, int y, int settleMs = 5, int gapMs = 45)
    {
        MoveTo(x, y);
        Thread.Sleep(settleMs);
        SendMouse(WinApi.MouseeventfLeftDown);
        Thread.Sleep(5);
        SendMouse(WinApi.MouseeventfLeftUp);
        Thread.Sleep(Math.Max(20, gapMs));
        SendMouse(WinApi.MouseeventfLeftDown);
        Thread.Sleep(5);
        SendMouse(WinApi.MouseeventfLeftUp);
    }

    public void KeyPress(Keys key)
    {
        if (key == Keys.None) return;
        var vk = (ushort)key;
        var scan = (ushort)WinApi.MapVirtualKey(vk, WinApi.MapvkVkToVsc);
        SendKey(vk, scan, 0);
        Thread.Sleep(8);
        SendKey(vk, scan, WinApi.KeyeventfKeyup);
    }

    public bool IsKeyDown(Keys key) => (WinApi.GetAsyncKeyState((int)key) & 0x8000) != 0;

    public Color GetPixel(int x, int y)
    {
        var hdc = WinApi.GetDC(IntPtr.Zero);
        try
        {
            var pixel = WinApi.GetPixel(hdc, x, y);
            var r = (int)(pixel & 0x000000FF);
            var g = (int)((pixel & 0x0000FF00) >> 8);
            var b = (int)((pixel & 0x00FF0000) >> 16);
            return Color.FromArgb(r, g, b);
        }
        finally
        {
            WinApi.ReleaseDC(IntPtr.Zero, hdc);
        }
    }

    private static void SendMouse(uint flags)
    {
        var input = new WinApi.Input
        {
            Type = WinApi.InputMouse,
            U = new WinApi.InputUnion
            {
                Mi = new WinApi.MouseInput
                {
                    DwFlags = flags,
                    DwExtraInfo = IntPtr.Zero
                }
            }
        };
        WinApi.SendInput(1, [input], System.Runtime.InteropServices.Marshal.SizeOf<WinApi.Input>());
    }

    private static void SendKey(ushort vk, ushort scan, uint flags)
    {
        var input = new WinApi.Input
        {
            Type = WinApi.InputKeyboard,
            U = new WinApi.InputUnion
            {
                Ki = new WinApi.KeyboardInput
                {
                    WVk = vk,
                    WScan = scan,
                    DwFlags = flags,
                    DwExtraInfo = IntPtr.Zero
                }
            }
        };
        WinApi.SendInput(1, [input], System.Runtime.InteropServices.Marshal.SizeOf<WinApi.Input>());
    }
}
