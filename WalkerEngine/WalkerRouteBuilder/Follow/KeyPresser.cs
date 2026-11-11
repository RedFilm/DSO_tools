using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WalkerRouteBuilder;

internal static class KeyPresser
{
    private const uint KeyUp = 0x0002;
    private const uint ScanCode = 0x0008;
    private const int KeyDownMessage = 0x0100;
    private const int KeyUpMessage = 0x0101;

    private const uint Unicode = 0x0004;

    public static void Tap(Keys key, int holdMilliseconds)
    {
        ushort virtualKey = (ushort)(key & Keys.KeyCode);
        if (virtualKey == 0)
            return;

        ushort scan = (ushort)MapVirtualKey(virtualKey, 0);
        Send(virtualKey, scan, 0);
        if (holdMilliseconds > 0)
            Thread.Sleep(holdMilliseconds);
        Send(virtualKey, scan, KeyUp);
    }

    /// <summary>Ввод текста через Unicode SendInput (логин/пароль лаунчера).</summary>
    public static void TypeText(string text, int pauseMilliseconds = 15)
    {
        if (string.IsNullOrEmpty(text))
            return;

        foreach (char ch in text)
        {
            SendUnicode(ch, up: false);
            SendUnicode(ch, up: true);
            if (pauseMilliseconds > 0)
                Thread.Sleep(pauseMilliseconds);
        }
    }

    public static void ClearField()
    {
        ushort control = (ushort)Keys.LControlKey;
        ushort scan = (ushort)MapVirtualKey(control, 0);
        Send(control, scan, 0);
        Tap(Keys.A, 20);
        Send(control, scan, KeyUp);
        Thread.Sleep(40);
        Tap(Keys.Back, 30);
        Thread.Sleep(40);
    }

    private static void SendUnicode(char ch, bool up)
    {
        var input = new Input
        {
            Type = 1,
            Union = new InputUnion
            {
                Key = new KeyboardInput
                {
                    VirtualKey = 0,
                    Scan = ch,
                    Flags = Unicode | (up ? KeyUp : 0),
                },
            },
        };

        if (SendInput(1, [input], Marshal.SizeOf<Input>()) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось отправить символ.");
    }

    /// <summary>Нажатие по скан-коду — надёжнее для игр, чем только virtual-key.</summary>
    public static void TapHardware(Keys key, int holdMilliseconds)
    {
        ushort virtualKey = (ushort)(key & Keys.KeyCode);
        if (virtualKey == 0)
            return;

        ushort scan = (ushort)MapVirtualKey(virtualKey, 0);
        if (scan == 0)
        {
            Tap(key, holdMilliseconds);
            return;
        }

        Send(0, scan, ScanCode);
        if (holdMilliseconds > 0)
            Thread.Sleep(holdMilliseconds);
        Send(0, scan, ScanCode | KeyUp);
    }

    public static void Post(IntPtr window, Keys key, int holdMilliseconds)
    {
        ushort virtualKey = (ushort)(key & Keys.KeyCode);
        if (window == IntPtr.Zero || virtualKey == 0)
            return;

        uint scan = MapVirtualKey(virtualKey, 0);
        PostMessage(window, KeyDownMessage, virtualKey, KeyParameter(scan, up: false));
        if (holdMilliseconds > 0)
            Thread.Sleep(holdMilliseconds);
        PostMessage(window, KeyUpMessage, virtualKey, KeyParameter(scan, up: true));
    }

    private static nint KeyParameter(uint scan, bool up)
    {
        long value = 1 | ((long)(scan & 0xFF) << 16);
        if (up)
            value |= (1L << 30) | (1L << 31);
        return (nint)value;
    }

    private static void Send(ushort virtualKey, ushort scan, uint flags)
    {
        var input = new Input
        {
            Type = 1,
            Union = new InputUnion
            {
                Key = new KeyboardInput
                {
                    VirtualKey = virtualKey,
                    Scan = scan,
                    Flags = flags,
                },
            },
        };

        if (SendInput(1, [input], Marshal.SizeOf<Input>()) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось отправить нажатие клавиши.");
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr window, int message, nuint key, nint parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInput Mouse;

        [FieldOffset(0)]
        public KeyboardInput Key;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nint Extra;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public nint Extra;
    }
}
