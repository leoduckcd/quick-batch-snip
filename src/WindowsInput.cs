using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

internal sealed class ShortcutHook : IDisposable
{
    private const int WinLeft = 0x5B;
    private const int WinRight = 0x5C;
    private const int Ctrl = 0x11;
    private const int Alt = 0x12;
    private const int Shift = 0x10;
    private const int S = 0x53;
    private const int V = 0x56;
    private static readonly UIntPtr OurInput = new UIntPtr(0x69426C91u);
    private readonly IntPtr destination;
    private readonly Thread thread;
    private readonly Native.KeyboardCallback callback;
    private uint threadId;
    private int installError;
    private IntPtr hook;
    private bool suppressS;
    private bool suppressV;
    internal volatile bool PasteEnabled;

    internal ShortcutHook(IntPtr destination)
    {
        this.destination = destination;
        callback = OnKey;
        using (var ready = new ManualResetEvent(false))
        {
            thread = new Thread(() => Run(ready)) { IsBackground = true };
            thread.Start();
            ready.WaitOne();
        }
        if (hook == IntPtr.Zero)
            throw new Win32Exception(installError, "Could not start the screenshot shortcut.");
    }

    private void Run(ManualResetEvent ready)
    {
        threadId = Native.GetCurrentThreadId();
        Native.QueueMessage message;
        Native.PeekMessage(out message, IntPtr.Zero, 0, 0, 0);
        hook = Native.SetWindowsHookEx(13, callback, Native.GetModuleHandle(null), 0); // WH_KEYBOARD_LL
        installError = Marshal.GetLastWin32Error();
        ready.Set();
        if (hook == IntPtr.Zero)
            return;

        try
        {
            while (Native.GetMessage(out message, IntPtr.Zero, 0, 0) > 0)
                Native.DispatchMessage(ref message);
        }
        finally { Native.UnhookWindowsHookEx(hook); }
    }

    private IntPtr OnKey(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            int key = Marshal.ReadInt32(lParam);
            if ((key == S || key == V) && !Marshal.ReadIntPtr(lParam, 16).Equals((IntPtr)(int)OurInput.ToUInt32()))
            {
                bool down = (wParam.ToInt32() & 1) == 0;
                if (key == S)
                {
                    if (suppressS)
                    {
                        suppressS = down;
                        return new IntPtr(1);
                    }
                    if (down && WinDown() && !KeyDown(Ctrl) && !KeyDown(Alt) && !KeyDown(Shift) &&
                        Native.PostMessage(destination, SnipApp.CaptureMessage, IntPtr.Zero, IntPtr.Zero))
                    {
                        suppressS = true;
                        // Give the shell a chord partner so it does not open Start when Win is released.
                        Native.keybd_event(0xE8, 0, 0, OurInput);
                        Native.keybd_event(0xE8, 0, 2, OurInput);
                        return new IntPtr(1);
                    }
                }
                else
                {
                    if (suppressV)
                    {
                        suppressV = down;
                        return new IntPtr(1);
                    }
                    if (down && PasteEnabled && KeyDown(Ctrl) && !KeyDown(Shift) && !KeyDown(Alt) && !WinDown() &&
                        Native.PostMessage(destination, SnipApp.PasteMessage, Native.GetForegroundWindow(), IntPtr.Zero))
                    {
                        suppressV = true;
                        return new IntPtr(1);
                    }
                }
            }
        }
        return Native.CallNextHookEx(hook, code, wParam, lParam);
    }

    internal static bool ReplayPaste()
    {
        if (KeyDown(Shift) || KeyDown(Alt) || WinDown())
            return false;

        var inputs = KeyDown(Ctrl)
            ? new[] { KeyboardInput(V, false), KeyboardInput(V, true) }
            : new[] { KeyboardInput(Ctrl, false), KeyboardInput(V, false), KeyboardInput(V, true), KeyboardInput(Ctrl, true) };
        return Native.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(Native.Input))) == inputs.Length;
    }

    private static Native.Input KeyboardInput(int key, bool released)
    {
        return new Native.Input
        {
            Type = 1,
            Data = new Native.InputData
            {
                Keyboard = new Native.KeyboardInput
                {
                    Key = (ushort)key,
                    Flags = released ? 2u : 0u,
                    ExtraInfo = OurInput
                }
            }
        };
    }

    private static bool WinDown() { return KeyDown(WinLeft) || KeyDown(WinRight); }
    private static bool KeyDown(int key) { return Native.GetAsyncKeyState(key) < 0; }

    public void Dispose()
    {
        Native.PostThreadMessage(threadId, 0x0012, IntPtr.Zero, IntPtr.Zero); // WM_QUIT
        thread.Join();
    }
}

internal static class Native
{
    internal delegate IntPtr KeyboardCallback(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    internal struct QueueMessage
    {
        public IntPtr Window;
        public uint Id;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X, Y;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Input
    {
        public uint Type;
        public InputData Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct InputData
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardInput
    {
        public ushort Key, Scan;
        public uint Flags, Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseInput
    {
        public int X, Y;
        public uint Data, Flags, Time;
        public UIntPtr ExtraInfo;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr SetWindowsHookEx(int type, KeyboardCallback callback, IntPtr module, uint thread);
    [DllImport("user32.dll")]
    internal static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]
    internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool PeekMessage(out QueueMessage message, IntPtr hwnd, uint min, uint max, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetMessage(out QueueMessage message, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr DispatchMessage(ref QueueMessage message);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool PostThreadMessage(uint thread, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string caption);
    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")]
    internal static extern bool AllowSetForegroundWindow(uint processId);
    [DllImport("user32.dll")]
    internal static extern uint SendInput(uint count, [In] Input[] inputs, int size);
    [DllImport("user32.dll")]
    internal static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extraInfo);
    [DllImport("user32.dll")]
    internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")]
    internal static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")]
    internal static extern bool CloseClipboard();
    [DllImport("user32.dll")]
    internal static extern bool EmptyClipboard();
    [DllImport("user32.dll")]
    internal static extern IntPtr GetClipboardOwner();
    [DllImport("user32.dll")]
    internal static extern IntPtr SetClipboardData(uint format, IntPtr handle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern uint RegisterClipboardFormat(string name);
    [DllImport("kernel32.dll")]
    internal static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
    [DllImport("kernel32.dll")]
    internal static extern IntPtr GlobalLock(IntPtr handle);
    [DllImport("kernel32.dll")]
    internal static extern bool GlobalUnlock(IntPtr handle);
    [DllImport("kernel32.dll")]
    internal static extern IntPtr GlobalFree(IntPtr handle);
}
