//==============================================================
// MutexSingleInstance — Named mutex-based single-instance helper
//
// GitHub: https://github.com/SevenKeyboard/mutex-single-instance
// Author: SevenKeyboard Ltd. (2026)
// License: MIT License
//==============================================================

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace SevenKeyboard;

public static partial class MutexSingleInstance
{
    private const string NamespaceId = "{2A89458C-A9A1-402F-A3E6-BCB6848608EA}";
    private static readonly object Sync = new();
    private static readonly string DefaultMutexName = CreateDefaultMutexName();
    private static SafeWaitHandle? _mutex;
    private static InstanceWindow? _window;

    public static uint TerminateMessage { get; } = RegisterTerminateMessage();

    // Timeouts are seconds: zero tries once without waiting; a negative value waits indefinitely.
    // Returns true when this process owns the mutex, so at most one registration is active per name.
    public static bool Register(Action closeRequested, string? name = null, uint? message = null, double previousExitTimeout = 0)
    {
        ArgumentNullException.ThrowIfNull(closeRequested);
        ValidateTimeout(previousExitTimeout);
        name = NormalizeMutexName(name ?? DefaultMutexName);
        uint closeMessage = message ?? TerminateMessage;

        lock (Sync)
        {
            // An earlier registration's window thread may own the mutex; release it before waiting.
            ReleaseRegistration();
            try
            {
                _mutex = CreateOrOpenMutex(name, out _, NativeMethods.SYNCHRONIZE | NativeMethods.MUTEX_MODIFY_STATE);
                if (_mutex == null)
                    return false;

                _window = new InstanceWindow(name, closeMessage, closeRequested, _mutex, previousExitTimeout);
                if (_window.Handle != 0)
                    return true;

                ReleaseRegistration();
                return false;
            }
            catch
            {
                ReleaseRegistration();
                throw;
            }
        }
    }

    public static void Unregister()
    {
        lock (Sync)
            ReleaseRegistration();
    }

    public static bool IsAlreadyExisting(string? name = null)
    {
        using SafeWaitHandle? mutex = CreateOrOpenMutex(NormalizeMutexName(name ?? DefaultMutexName), out bool alreadyExists);
        return alreadyExists;
    }

    public static nint GetExistingHwnd(string? name = null)
    {
        name = NormalizeMutexName(name ?? DefaultMutexName);
        using SafeWaitHandle? mutex = CreateOrOpenMutex(name, out bool alreadyExists);
        if (!alreadyExists)
            return 0;

        List<nint> windows = GetInstanceWindows(name, includeSelf: false);
        return windows.Count == 0 ? 0 : windows[0];
    }

    //--------------------------------------------------

    public static int CloseInstances(string? name = null, uint? message = null, bool includeSelf = false)
    {
        name = NormalizeMutexName(name ?? DefaultMutexName);
        using SafeWaitHandle? mutex = CreateOrOpenMutex(name, out bool alreadyExists);
        if (!alreadyExists)
            return 0;

        List<nint> windows = GetInstanceWindows(name, includeSelf);
        foreach (nint window in windows)
            NativeMethods.PostMessageW(window, message ?? TerminateMessage, 0, 0);
        return windows.Count;
    }

    // Like the original, this explicitly targets every instance, including the caller.
    // Returns false when any instance could not be confirmed closed; the rest are still processed.
    public static bool TerminateProcesses(string? name = null, double winCloseTimeout = 4, double processCloseTimeout = 4)
    {
        ValidateTimeout(winCloseTimeout);
        ValidateTimeout(processCloseTimeout);
        name = NormalizeMutexName(name ?? DefaultMutexName);
        using SafeWaitHandle? mutex = CreateOrOpenMutex(name, out bool alreadyExists);
        if (!alreadyExists)
            return true;

        bool allClosed = true;
        foreach (nint window in GetInstanceWindows(name, includeSelf: true))
        {
            NativeMethods.GetWindowThreadProcessId(window, out uint processId);
            NativeMethods.PostMessageW(window, NativeMethods.WM_CLOSE, 0, 0);
            if (WaitForWindowToClose(window, winCloseTimeout))
                continue;

            using SafeProcessHandle process = NativeMethods.OpenProcess(NativeMethods.SYNCHRONIZE | NativeMethods.PROCESS_TERMINATE, false, processId);
            if (process.IsInvalid)
            {
                // ERROR_INVALID_PARAMETER means the process has already exited.
                if (Marshal.GetLastWin32Error() != NativeMethods.ERROR_INVALID_PARAMETER)
                    allClosed = false;
                continue;
            }

            // A failed termination is reflected by the wait below, such as access denied while still running.
            NativeMethods.TerminateProcess(process, 1);
            if (NativeMethods.WaitForSingleObject(process, GetRemainingWaitMilliseconds(processCloseTimeout, 0)) != NativeMethods.WAIT_OBJECT_0)
                allClosed = false;
        }
        return allClosed;
    }

    //--------------------------------------------------

    // Returns null when the mutex exists but this process may not open it.
    private static SafeWaitHandle? CreateOrOpenMutex(string name, out bool alreadyExists, uint access = NativeMethods.SYNCHRONIZE)
    {
        SafeWaitHandle mutex = NativeMethods.CreateMutexExW(0, name, 0, access);
        int error = Marshal.GetLastWin32Error();
        if (!mutex.IsInvalid)
        {
            alreadyExists = error == NativeMethods.ERROR_ALREADY_EXISTS;
            return mutex;
        }

        mutex.Dispose();
        // For example, a non-elevated process cannot open a mutex created by an elevated instance.
        if (error == NativeMethods.ERROR_ACCESS_DENIED)
        {
            alreadyExists = true;
            return null;
        }
        throw new Win32Exception(error);
    }

    private static List<nint> GetInstanceWindows(string name, bool includeSelf)
    {
        string title = BuildWindowTitle(name);
        nint ownWindow = _window?.Handle ?? 0;
        List<nint> windows = new();
        char[] titleBuffer = new char[title.Length + 1];

        NativeMethods.EnumWindowsProcedure callback = (window, _) =>
        {
            if (window == ownWindow && !includeSelf)
                return true;
            if (NativeMethods.GetWindowTextLengthW(window) != title.Length)
                return true;
            int titleLength = NativeMethods.GetWindowTextW(window, titleBuffer, titleBuffer.Length);
            if (new string(titleBuffer, 0, titleLength) != title)
                return true;

            windows.Add(window);
            return true;
        };

        if (!NativeMethods.EnumWindows(callback, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        GC.KeepAlive(callback);
        return windows;
    }

    private static bool WaitForWindowToClose(nint window, double timeout)
    {
        Stopwatch timer = Stopwatch.StartNew();
        while (NativeMethods.IsWindow(window))
        {
            uint remaining = GetRemainingWaitMilliseconds(timeout, timer.Elapsed.TotalMilliseconds);
            if (remaining == 0)
                return false;
            Thread.Sleep((int)Math.Min(10u, remaining));
        }
        return true;
    }

    private static uint GetRemainingWaitMilliseconds(double timeout, double elapsedMilliseconds)
        => timeout < 0 ? uint.MaxValue : (uint)Math.Clamp(Math.Floor(timeout * 1000 - elapsedMilliseconds), 0, uint.MaxValue - 1);

    private static void ReleaseRegistration()
    {
        _window?.Dispose();
        _window = null;
        _mutex?.Dispose();
        _mutex = null;
    }

    //--------------------------------------------------

    private static string NormalizeMutexName(string name)
    {
        if (name.Contains('\0'))
            throw new ArgumentException("Mutex names must not contain null characters.", nameof(name));
        return name.Length > 259 ? name[..259] : name;
    }

    private static string CreateDefaultMutexName()
    {
        string path = Environment.ProcessPath
            ?? throw new InvalidOperationException("The current executable path is unavailable.");
        if (string.Equals(Path.GetFileNameWithoutExtension(path), "dotnet", StringComparison.OrdinalIgnoreCase))
            path = Path.GetFullPath(Environment.GetCommandLineArgs()[0]);
        string name = @"Local\" + path.Replace("%", "%25").Replace(@"\", "%5C");
        return name.Length > 259 ? name[..259] : name;
    }

    private static string BuildWindowTitle(string name)
        => @"\MutexSingleInstance\CSharp\" + NamespaceId + @"\" + name;

    private static void ValidateTimeout(double timeout)
    {
        if (!double.IsFinite(timeout))
            throw new ArgumentOutOfRangeException(nameof(timeout), "Use a finite number of seconds; a negative value waits indefinitely.");
    }

    private static uint RegisterTerminateMessage()
    {
        uint message = NativeMethods.RegisterWindowMessageW(NamespaceId + ":MutexSingleInstance:Terminate");
        if (message == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return message;
    }

    //--------------------------------------------------

    private sealed class InstanceWindow : IDisposable
    {
        private const uint RescanMilliseconds = 100;

        private uint _threadId;
        private readonly string _name;
        private readonly uint _message;
        private readonly Action _closeRequested;
        private readonly SafeWaitHandle _mutex;
        private readonly double _timeout;
        private readonly NativeMethods.WindowProcedure _windowProcedure;
        private readonly Thread _thread;
        private readonly TaskCompletionSource<nint> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _closeQueued;

        // Zero when ownership was not acquired within the timeout; the window thread has then ended.
        internal nint Handle { get; }

        internal InstanceWindow(string name, uint message, Action closeRequested, SafeWaitHandle mutex, double timeout)
        {
            _name = name;
            _message = message;
            _closeRequested = closeRequested;
            _mutex = mutex;
            _timeout = timeout;
            _windowProcedure = WindowProc;
            _thread = new Thread(Run) { IsBackground = true, Name = "MutexSingleInstance" };
            _thread.Start();
            try
            {
                Handle = _ready.Task.GetAwaiter().GetResult();
            }
            catch
            {
                _thread.Join();
                throw;
            }
        }

        // Cleanup must not throw. A failed post means the message loop has already ended.
        public void Dispose()
        {
            if (NativeMethods.PostThreadMessageW(_threadId, NativeMethods.WM_QUIT, 0, 0))
                _thread.Join();
        }

        // Mutex ownership belongs to the thread that acquired it, so this thread acquires and releases it.
        private void Run()
        {
            _threadId = NativeMethods.GetCurrentThreadId();
            string className = "SevenKeyboard.MutexSingleInstance." + Guid.NewGuid().ToString("N");
            nint module = NativeMethods.GetModuleHandleW(null);
            nint window = 0;
            ushort windowClass = 0;
            bool owned = false;
            try
            {
                try
                {
                    NativeMethods.WNDCLASSW definition = new()
                    {
                        lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_windowProcedure),
                        hInstance = module,
                        lpszClassName = className
                    };
                    windowClass = NativeMethods.RegisterClassW(ref definition);
                    if (windowClass == 0)
                        throw new Win32Exception(Marshal.GetLastWin32Error());

                    window = NativeMethods.CreateWindowExW(0, className, "", 0, 0, 0, 0, 0, 0, 0, module, 0);
                    if (window == 0)
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    // Lets lower-integrity instances reach an elevated receiver; failure only limits that case.
                    NativeMethods.ChangeWindowMessageFilterEx(window, _message, NativeMethods.MSGFLT_ALLOW, 0);

                    owned = AcquireOwnership();
                    // Publish the title only as the owner, so waiting registrations never close each other.
                    if (owned && !NativeMethods.SetWindowTextW(window, BuildWindowTitle(_name)))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                catch (Exception error)
                {
                    _ready.SetException(error);
                    return;
                }

                _ready.SetResult(owned ? window : 0);
                if (!owned)
                    return;

                // GetMessageW returns 0 for WM_QUIT and -1 on failure; both end the loop and release the window.
                while (NativeMethods.GetMessageW(out NativeMethods.MSG message, 0, 0, 0) > 0)
                    NativeMethods.DispatchMessageW(ref message);
            }
            finally
            {
                if (window != 0)
                    NativeMethods.DestroyWindow(window);
                if (owned)
                    NativeMethods.ReleaseMutex(_mutex);
                if (windowClass != 0)
                    NativeMethods.UnregisterClassW(className, module);
                GC.KeepAlive(_windowProcedure);
            }
        }

        // Waits for the mutex, asking each owner found meanwhile to close once.
        // Registrations still waiting also ask each new owner to close, so a single owner remains.
        private bool AcquireOwnership()
        {
            HashSet<nint> requested = new();
            Stopwatch timer = Stopwatch.StartNew();
            while (true)
            {
                foreach (nint owner in GetInstanceWindows(_name, includeSelf: false))
                {
                    if (requested.Add(owner))
                        NativeMethods.PostMessageW(owner, _message, 0, 0);
                }

                uint remaining = GetRemainingWaitMilliseconds(_timeout, timer.Elapsed.TotalMilliseconds);
                uint result = NativeMethods.WaitForSingleObject(_mutex, Math.Min(remaining, RescanMilliseconds));
                switch (result)
                {
                    case NativeMethods.WAIT_OBJECT_0:
                    case NativeMethods.WAIT_ABANDONED: // The previous owner exited without releasing it.
                        return true;
                    case NativeMethods.WAIT_TIMEOUT:
                        if (remaining <= RescanMilliseconds)
                            return false;
                        continue;
                    default:
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
        }

        private nint WindowProc(nint window, uint message, nuint wParam, nint lParam)
        {
            if (message == _message || message == NativeMethods.WM_CLOSE)
            {
                if (Interlocked.Exchange(ref _closeQueued, 1) == 0)
                    ThreadPool.QueueUserWorkItem(_ => InvokeCloseRequested());
                return 0;
            }

            return NativeMethods.DefWindowProcW(window, message, wParam, lParam);
        }

        private void InvokeCloseRequested()
        {
            // Like Critical, registration defers handling a posted close request.
            lock (Sync)
            {
                if (!ReferenceEquals(_window, this))
                    return;
            }

            // App code may call Unregister; never invoke it while holding Sync.
            try
            {
                _closeRequested();
            }
            finally
            {
                Volatile.Write(ref _closeQueued, 0);
            }
        }
    }
}
