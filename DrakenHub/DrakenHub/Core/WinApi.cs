using System.Runtime.InteropServices;

namespace DrakenHub.Core;

internal static class WinApi
{
    public const int WhKeyboardLl = 13;
    public const int WhMouseLl = 14;
    public const int WmKeyDown = 0x0100;
    public const int WmSysKeyDown = 0x0104;
    public const int WmKeyUp = 0x0101;
    public const int WmSysKeyUp = 0x0105;
    public const int WmLButtonDown = 0x0201;
    public const int WmRButtonDown = 0x0204;
    public const int WmHotkey = 0x0312;
    public const uint KeyeventfKeyup = 0x0002;
    public const uint MouseeventfLeftDown = 0x0002;
    public const uint MouseeventfLeftUp = 0x0004;
    public const uint MouseeventfRightDown = 0x0008;
    public const uint MouseeventfRightUp = 0x0010;
    public const uint MouseeventfMove = 0x0001;
    public const uint MouseeventfAbsolute = 0x8000;
    public const int InputKeyboard = 1;
    public const int InputMouse = 0;
    public const uint MapvkVkToVsc = 0;

    public delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MsllHookStruct
    {
        public Point Pt;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr DwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KbdLlHookStruct
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr DwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Input
    {
        public int Type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mi;
        [FieldOffset(0)] public KeyboardInput Ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint DwFlags;
        public uint Time;
        public IntPtr DwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KeyboardInput
    {
        public ushort WVk;
        public ushort WScan;
        public uint DwFlags;
        public uint Time;
        public IntPtr DwExtraInfo;
    }

    [DllImport("user32.dll")]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out Point lpPoint);

    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    public static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    public static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("gdi32.dll")]
    public static extern uint GetPixel(IntPtr hdc, int nXPos, int nYPos);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    public const uint ProcessVmRead = 0x0010;
    public const uint ProcessVmWrite = 0x0020;
    public const uint ProcessVmOperation = 0x0008;
    public const uint ProcessQueryInformation = 0x0400;
    public const uint ProcessQueryLimitedInformation = 0x1000;
    public const uint ThreadQueryInformation = 0x0040;
    public const uint ThreadQueryLimitedInformation = 0x0800;
    public const uint Th32csSnapThread = 0x00000004;
    public const int ThreadInfoClassBasic = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct ThreadEntry32
    {
        public uint DwSize;
        public uint CntUsage;
        public uint Th32ThreadId;
        public uint Th32OwnerProcessId;
        public int TpBasePri;
        public int TpDeltaPri;
        public uint DwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ThreadBasicInfo
    {
        public int ExitStatus;
        public IntPtr TebBaseAddress;
        public IntPtr ClientIdUniqueProcess;
        public IntPtr ClientIdUniqueThread;
        public IntPtr AffinityMask;
        public int Priority;
        public int BasePriority;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenThread(uint dwDesiredAccess, bool bInheritHandle, uint dwThreadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int nSize, out IntPtr lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int nSize, out IntPtr lpNumberOfBytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool Thread32First(IntPtr hSnapshot, ref ThreadEntry32 lpte);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool Thread32Next(IntPtr hSnapshot, ref ThreadEntry32 lpte);

    [DllImport("ntdll.dll")]
    public static extern int NtQueryInformationThread(
        IntPtr threadHandle,
        int threadInformationClass,
        ref ThreadBasicInfo threadInformation,
        int threadInformationLength,
        out int returnLength);
}
