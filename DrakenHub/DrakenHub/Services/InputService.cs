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

    public void LeftClick(int x, int y, int settleMs = 5, int holdMs = 25)
    {
        MoveTo(x, y);
        Thread.Sleep(Math.Max(1, settleMs));
        SendMouse(WinApi.MouseeventfLeftDown);
        Thread.Sleep(Math.Max(5, holdMs));
        SendMouse(WinApi.MouseeventfLeftUp);
    }

    public void RightClick(int x, int y, int settleMs = 5, int holdMs = 25)
    {
        MoveTo(x, y);
        Thread.Sleep(Math.Max(1, settleMs));
        SendMouse(WinApi.MouseeventfRightDown);
        Thread.Sleep(Math.Max(5, holdMs));
        SendMouse(WinApi.MouseeventfRightUp);
    }

    public void DoubleClick(int x, int y, int settleMs = 5, int gapMs = 45)
    {
        MoveTo(x, y);
        Thread.Sleep(Math.Max(1, settleMs));
        SendMouse(WinApi.MouseeventfLeftDown);
        Thread.Sleep(25);
        SendMouse(WinApi.MouseeventfLeftUp);
        Thread.Sleep(Math.Max(20, gapMs));
        SendMouse(WinApi.MouseeventfLeftDown);
        Thread.Sleep(25);
        SendMouse(WinApi.MouseeventfLeftUp);
    }

    /// <summary>
    /// Stronger LMB for flaky Flash/AIR UI buttons (map Enter etc.):
    /// absolute move to point, hover, down/up with position flags, stay briefly.
    /// </summary>
    public void LeftClickReliable(int x, int y)
    {
        // 1) Desktop cursor
        WinApi.SetCursorPos(x, y);
        Thread.Sleep(30);

        // 2) Absolute SendInput move (games that ignore SetCursorPos alone)
        SendMouseAbsolute(x, y, WinApi.MouseeventfMove | WinApi.MouseeventfAbsolute | WinApi.MouseeventfVirtualDesk);
        Thread.Sleep(80); // hover — Flash buttons often need mouseover before click

        // 3) LMB down at that absolute position
        SendMouseAbsolute(x, y,
            WinApi.MouseeventfMove | WinApi.MouseeventfAbsolute | WinApi.MouseeventfVirtualDesk | WinApi.MouseeventfLeftDown);
        Thread.Sleep(50);

        // 4) LMB up at same position
        SendMouseAbsolute(x, y,
            WinApi.MouseeventfMove | WinApi.MouseeventfAbsolute | WinApi.MouseeventfVirtualDesk | WinApi.MouseeventfLeftUp);
        Thread.Sleep(80); // keep cursor on button so the UI finishes handling the click
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

    private static void SendMouseAbsolute(int screenX, int screenY, uint flags)
    {
        var vs = SystemInformation.VirtualScreen;
        var nx = (int)Math.Round((screenX - vs.Left) * 65535.0 / Math.Max(1, vs.Width - 1));
        var ny = (int)Math.Round((screenY - vs.Top) * 65535.0 / Math.Max(1, vs.Height - 1));
        var input = new WinApi.Input
        {
            Type = WinApi.InputMouse,
            U = new WinApi.InputUnion
            {
                Mi = new WinApi.MouseInput
                {
                    Dx = nx,
                    Dy = ny,
                    DwFlags = flags,
                    DwExtraInfo = IntPtr.Zero
                }
            }
        };
        WinApi.SendInput(1, [input], System.Runtime.InteropServices.Marshal.SizeOf<WinApi.Input>());
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
