using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace SevenKeyboard;

public static partial class MutexSingleInstance
{
    private static class NativeMethods
    {
        internal const uint SYNCHRONIZE = 0x00100000;
        internal const uint MUTEX_MODIFY_STATE = 0x0001;
        internal const uint PROCESS_TERMINATE = 0x0001;
        internal const uint WAIT_OBJECT_0 = 0;
        internal const uint WAIT_ABANDONED = 0x00000080;
        internal const uint WAIT_TIMEOUT = 0x00000102;
        internal const uint WM_CLOSE = 0x0010;
        internal const uint WM_QUIT = 0x0012;
        internal const uint MSGFLT_ALLOW = 1;
        internal const int ERROR_ACCESS_DENIED = 5;
        internal const int ERROR_INVALID_PARAMETER = 87;
        internal const int ERROR_ALREADY_EXISTS = 183;

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate nint WindowProcedure(nint hwnd, uint message, nuint wParam, nint lParam);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal delegate bool EnumWindowsProcedure(nint hwnd, nint lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct WNDCLASSW
        {
            internal uint style;
            internal nint lpfnWndProc;
            internal int cbClsExtra;
            internal int cbWndExtra;
            internal nint hInstance;
            internal nint hIcon;
            internal nint hCursor;
            internal nint hbrBackground;
            internal string? lpszMenuName;
            internal string lpszClassName;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MSG
        {
            internal nint hwnd;
            internal uint message;
            internal nuint wParam;
            internal nint lParam;
            internal uint time;
            internal int ptX;
            internal int ptY;
            internal uint lPrivate;
        }

        [DllImport("kernel32.dll", ExactSpelling = true)]
        internal static extern uint GetCurrentThreadId();

        //--------------------------------------------------

        [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeWaitHandle CreateMutexExW(nint lpMutexAttributes, string lpName, uint dwFlags, uint dwDesiredAccess);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        internal static extern SafeProcessHandle OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ReleaseMutex(SafeWaitHandle hMutex);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        internal static extern uint WaitForSingleObject(SafeWaitHandle hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        internal static extern uint WaitForSingleObject(SafeProcessHandle hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool TerminateProcess(SafeProcessHandle hProcess, uint uExitCode);

        [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern nint GetModuleHandleW(string? lpModuleName);

        //--------------------------------------------------

        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumWindows(EnumWindowsProcedure lpEnumFunc, nint lParam);

        [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        internal static extern int GetWindowTextLengthW(nint hWnd);

        [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        internal static extern int GetWindowTextW(nint hWnd, [Out] char[] lpString, int nMaxCount);

        [DllImport("user32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindow(nint hWnd);

        [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern uint RegisterWindowMessageW(string lpString);

        [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern ushort RegisterClassW(ref WNDCLASSW lpWndClass);

        [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern nint CreateWindowExW(uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
            int x, int y, int nWidth, int nHeight, nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

        [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowTextW(nint hWnd, string lpString);

        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ChangeWindowMessageFilterEx(nint hWnd, uint message, uint action, nint pChangeFilterStruct);

        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        internal static extern int GetMessageW(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll", ExactSpelling = true)]
        internal static extern nint DispatchMessageW(ref MSG lpMsg);

        [DllImport("user32.dll", ExactSpelling = true)]
        internal static extern nint DefWindowProcW(nint hWnd, uint msg, nuint wParam, nint lParam);

        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PostMessageW(nint hWnd, uint msg, nuint wParam, nint lParam);

        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PostThreadMessageW(uint idThread, uint msg, nuint wParam, nint lParam);

        [DllImport("user32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DestroyWindow(nint hWnd);

        [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnregisterClassW(string lpClassName, nint hInstance);

        [DllImport("user32.dll", ExactSpelling = true)]
        internal static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);
    }
}
