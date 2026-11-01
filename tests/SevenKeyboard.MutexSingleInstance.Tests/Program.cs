using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using SevenKeyboard;

internal static class Program
{
    private const int Timeout = 15000;

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 3 && args[0] == "--child")
                return RunChild(args[1], args[2]);

            (string Name, Action<TestScope> Run)[] tests =
            [
                ("Missing instance and self exclusion", MissingAndSelf),
                ("Inaccessible mutex counts as existing", InaccessibleMutex),
                ("Unregister and repeated registration", Reregister),
                ("Default and truncated names", Names),
                ("Repeated close requests and callback unregister", Callbacks),
                ("No-wait registration does not take over", NoWaitTakeover),
                ("Registration waits for the owner to exit", WaitForOwner),
                ("Timeout returns false without registering", WaitBudget),
                ("Cooperative and indefinite waits", CooperativeWait),
                ("Custom close message", CustomMessage),
                ("Waiting registration stays unpublished", WaitingUnpublished),
                ("Hidden receiver and cooperative WM_CLOSE", HiddenReceiver),
                ("Queued registrations leave one owner", QueuedRegistrations),
                ("Simultaneous registrations leave one owner", Simultaneous),
                ("Forced termination of the owner", ForceOwner),
                ("Abandoned ownership is taken over", AbandonedOwnership),
                ("Process death and background-thread lifetime", ProcessLifetime)
            ];
            int failures = 0;
            foreach (var test in tests)
            {
                try
                {
                    using TestScope scope = new();
                    test.Run(scope);
                    Console.WriteLine("PASS " + test.Name);
                }
                catch (Exception error)
                {
                    failures++;
                    Console.Error.WriteLine("FAIL " + test.Name + ": " + error);
                }
                finally
                {
                    MutexSingleInstance.Unregister();
                }
            }
            Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed.");
            return failures == 0 ? 0 : 1;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void MissingAndSelf(TestScope scope)
    {
        Require(!MutexSingleInstance.IsAlreadyExisting(scope.Name), "Unused name exists.");
        Require(MutexSingleInstance.GetExistingHwnd(scope.Name) == 0, "Unused name has a receiver.");
        Require(MutexSingleInstance.CloseInstances(scope.Name) == 0, "Unused name has targets.");
        Require(MutexSingleInstance.Register(() => { }, scope.Name), "Initial registration failed.");
        Require(MutexSingleInstance.IsAlreadyExisting(scope.Name), "Registration is missing.");
        Require(MutexSingleInstance.GetExistingHwnd(scope.Name) == 0, "Own receiver was included.");
        Require(MutexSingleInstance.CloseInstances(scope.Name) == 0, "Own receiver was closed.");
    }

    // Simulates a mutex created by an elevated instance with a DACL that denies this process.
    private static void InaccessibleMutex(TestScope scope)
    {
        Require(Native.ConvertStringSecurityDescriptorToSecurityDescriptorW("D:(D;;GA;;;WD)", 1, out nint descriptor, 0),
            "Security descriptor conversion failed.");
        try
        {
            Native.SECURITY_ATTRIBUTES attributes = new() { Length = Marshal.SizeOf<Native.SECURITY_ATTRIBUTES>(), SecurityDescriptor = descriptor };
            using SafeWaitHandle denied = Native.CreateMutexExW(ref attributes, scope.Name, 0, 0x00100000);
            Require(!denied.IsInvalid, "Inaccessible mutex creation failed.");

            Require(MutexSingleInstance.IsAlreadyExisting(scope.Name), "Inaccessible mutex was not reported as existing.");
            Require(MutexSingleInstance.GetExistingHwnd(scope.Name) == 0, "Inaccessible mutex has an unexpected receiver.");
            Require(MutexSingleInstance.CloseInstances(scope.Name) == 0, "Inaccessible mutex has unexpected targets.");
            Require(!MutexSingleInstance.Register(() => { }, scope.Name), "Registration succeeded without opening the mutex.");
            Require(CountWindows(scope.Name) == 0, "Failed registration published a receiving window.");
        }
        finally
        {
            Native.LocalFree(descriptor);
        }
    }

    private static void Reregister(TestScope scope)
    {
        MutexSingleInstance.Register(() => { }, scope.Name);
        MutexSingleInstance.Register(() => { }, scope.Name);
        Require(CountWindows(scope.Name) == 1, "Re-registering duplicated the receiver.");
        MutexSingleInstance.Unregister();
        MutexSingleInstance.Unregister();
        Require(!MutexSingleInstance.IsAlreadyExisting(scope.Name), "Unregister kept a handle.");
        MutexSingleInstance.Register(() => { }, scope.Name);
        Require(CountWindows(scope.Name) == 1, "Registration did not recover.");
    }

    private static void Names(TestScope scope)
    {
        MutexSingleInstance.Register(() => { });
        Require(MutexSingleInstance.IsAlreadyExisting(), "Default identity is missing.");
        MutexSingleInstance.Unregister();
        string name = scope.Name + new string('x', 300);
        MutexSingleInstance.Register(() => { }, name);
        Require(MutexSingleInstance.IsAlreadyExisting(name[..259]), "Name was not truncated.");
        Require(CountWindows(name[..259]) == 1, "Truncated title is missing.");
    }

    private static void Callbacks(TestScope scope)
    {
        using SemaphoreSlim calls = new(0);
        MutexSingleInstance.Register(() => calls.Release(), scope.Name);
        for (int i = 0; i < 2; i++)
        {
            Require(MutexSingleInstance.CloseInstances(scope.Name, includeSelf: true) == 1, "Self-inclusive close missed receiver.");
            Require(calls.Wait(Timeout), "Repeated callback was suppressed.");
            Thread.Sleep(50); // Let the callback return before the next independent request.
        }
        MutexSingleInstance.Unregister();
        using ManualResetEventSlim disposed = new();
        MutexSingleInstance.Register(() =>
        {
            MutexSingleInstance.Unregister();
            disposed.Set();
        }, scope.Name);
        MutexSingleInstance.CloseInstances(scope.Name, includeSelf: true);
        Require(disposed.Wait(Timeout), "Callback Unregister deadlocked.");
        Require(!MutexSingleInstance.IsAlreadyExisting(scope.Name), "Callback did not release registration.");
    }

    private static void NoWaitTakeover(TestScope scope)
    {
        Child owner = scope.Start("hold");
        owner.Ready();
        Stopwatch timer = Stopwatch.StartNew();
        Require(!MutexSingleInstance.Register(() => { }, scope.Name), "No-wait registration took over a running owner.");
        Require(timer.ElapsedMilliseconds < 1500, "No-wait registration waited.");
        Require(CountWindows(scope.Name) == 1, "Failed registration published a receiving window.");
        Require(!owner.Process.HasExited, "Register forcibly killed the owner.");
        Require(MutexSingleInstance.CloseInstances(scope.Name) == 1, "Close did not find the owner.");
    }

    private static void WaitForOwner(TestScope scope)
    {
        Child owner = scope.Start("hold");
        owner.Ready();
        owner.Send("EXIT 500");
        Stopwatch timer = Stopwatch.StartNew();
        Require(MutexSingleInstance.Register(() => { }, scope.Name, previousExitTimeout: 4), "Waiting registration failed.");
        Require(timer.ElapsedMilliseconds >= 400, "Register returned before the owner released ownership.");
        owner.Exit();
        Require(CountWindows(scope.Name) == 1, "Old receiving window remains.");
    }

    private static void WaitBudget(TestScope scope)
    {
        Child owner = scope.Start("hold");
        owner.Ready();
        Stopwatch timer = Stopwatch.StartNew();
        Require(!MutexSingleInstance.Register(() => { }, scope.Name, previousExitTimeout: 0.7), "Ignored close did not return false.");
        Require(timer.ElapsedMilliseconds >= 550 && timer.ElapsedMilliseconds < 1050,
            $"Wait did not follow the timeout: {timer.ElapsedMilliseconds} ms.");
        Require(CountWindows(scope.Name) == 1, "Timeout published a second receiving window.");
        Require(!owner.Process.HasExited, "Register forcibly killed the owner.");
    }

    private static void CooperativeWait(TestScope scope)
    {
        Child first = scope.Start("cooperative");
        first.Ready();
        Require(MutexSingleInstance.Register(() => { }, scope.Name, previousExitTimeout: -1), "Indefinite wait failed.");
        first.Exit();
    }

    private static void CustomMessage(TestScope scope)
    {
        uint message = Native.RegisterWindowMessageW("SevenKeyboard.MutexSingleInstance.Tests.Custom");
        using ManualResetEventSlim requested = new();
        MutexSingleInstance.Register(() => requested.Set(), scope.Name, message);
        Require(MutexSingleInstance.CloseInstances(scope.Name, message, includeSelf: true) == 1, "Custom receiver missing.");
        Require(requested.Wait(Timeout), "Custom close message was ignored.");
    }

    private static void WaitingUnpublished(TestScope scope)
    {
        Child a = scope.Start("hold");
        a.Ready();
        nint ownerWindow = MutexSingleInstance.GetExistingHwnd(scope.Name);
        Child b = scope.Start("guard");
        Thread.Sleep(300);
        Require(CountWindows(scope.Name) == 1, "Waiting registration published its receiving window.");
        Require(MutexSingleInstance.GetExistingHwnd(scope.Name) == ownerWindow, "The owner changed while it was still running.");
        a.Send("STOP");
        a.Exit();
        b.Ready();
        Require(CountWindows(scope.Name) == 1, "The new owner did not replace the old receiving window.");
        b.Send("STOP");
        b.Exit();
    }

    private static void HiddenReceiver(TestScope scope)
    {
        Child child = scope.Start("cooperative");
        child.Ready();
        nint window = MutexSingleInstance.GetExistingHwnd(scope.Name);
        Require(window != 0, "The receiving window was not found.");
        Native.GetWindowThreadProcessId(window, out uint processId);
        Require(processId == child.Process.Id, "The receiver belongs to a different process.");
        Require(!Native.IsWindowVisible(window), "The receiving window is visible.");
        Require(MutexSingleInstance.TerminateProcesses(scope.Name, 4, 2), "Cooperative termination was not confirmed.");
        child.Exit();
        Require(!MutexSingleInstance.IsAlreadyExisting(scope.Name), "Cooperative WM_CLOSE kept the registration.");
    }

    private static void QueuedRegistrations(TestScope scope)
    {
        Child a = scope.Start("slow");
        a.Ready();
        Child[] waiting = Enumerable.Range(0, 3).Select(_ => scope.Start("chain")).ToArray();
        Thread.Sleep(300);
        Require(CountWindows(scope.Name) == 1, "Waiting registrations published receiving windows.");
        a.Exit();
        RequireSingleOwner(scope.Name, waiting);
    }

    private static void Simultaneous(TestScope scope)
    {
        Child[] children = Enumerable.Range(0, 6).Select(_ => scope.Start("contender")).ToArray();
        foreach (Child child in children)
            child.Expect("BARRIER");
        foreach (Child child in children)
            child.Send("GO");
        RequireSingleOwner(scope.Name, children);
    }

    // Every contender must eventually own the mutex once; all but the last are then asked to close.
    private static void RequireSingleOwner(string name, Child[] children)
    {
        foreach (Child child in children)
            child.Ready();
        WaitUntil(() => children.Count(child => !child.Process.HasExited) == 1, "More than one owner remained.");
        Require(CountWindows(name) == 1, "The remaining owner has no single receiving window.");
        Child survivor = children.Single(child => !child.Process.HasExited);
        survivor.Send("STOP");
        foreach (Child child in children)
            child.Exit();
    }

    private static void ForceOwner(TestScope scope)
    {
        Child owner = scope.Start("hold");
        owner.Ready();
        Require(MutexSingleInstance.TerminateProcesses(scope.Name, 0.05, 2), "Forced termination was not confirmed.");
        owner.Exit(expectedCode: 1);
        Require(!MutexSingleInstance.IsAlreadyExisting(scope.Name), "Killed process left mutex handles.");
    }

    private static void AbandonedOwnership(TestScope scope)
    {
        // Keep the mutex alive after the owner dies, so the next wait reports abandonment.
        using SafeWaitHandle observer = Native.CreateMutexExW(0, scope.Name, 0, 0x00100000);
        Require(!observer.IsInvalid, "Observer handle creation failed.");
        Child owner = scope.Start("hold");
        owner.Ready();
        owner.Process.Kill();
        owner.Exit(expectedCode: null);
        Require(MutexSingleInstance.Register(() => { }, scope.Name), "Abandoned ownership was not taken over.");
        Require(CountWindows(scope.Name) == 1, "Takeover did not publish a receiving window.");
    }

    private static void ProcessLifetime(TestScope scope)
    {
        Child child = scope.Start("hold");
        child.Ready();
        child.Process.Kill();
        child.Exit(expectedCode: null);
        Require(!MutexSingleInstance.IsAlreadyExisting(scope.Name), "Process death kept registration.");
        Child background = scope.Start("background");
        background.Ready();
        background.Exit();
        Require(!MutexSingleInstance.IsAlreadyExisting(scope.Name), "Background receiver kept app alive.");
    }

    private static int RunChild(string mode, string name)
    {
        if (mode == "contender")
        {
            Console.WriteLine("BARRIER");
            Require(Console.ReadLine() == "GO", "Expected GO.");
        }
        using ManualResetEventSlim shutdown = new();
        bool registered = MutexSingleInstance.Register(() =>
        {
            if (mode == "guard")
            {
                Console.WriteLine("CALLBACK");
                shutdown.Set();
            }
            if (mode is "cooperative" or "slow" or "chain" or "contender")
                shutdown.Set();
        }, name, previousExitTimeout: mode is "chain" or "contender" or "guard" ? 10 : 0);
        Console.WriteLine(registered ? "READY" : "FAILED");
        if (!registered || mode == "background")
            return 0;

        _ = Task.Run(() =>
        {
            while (Console.ReadLine() is string command)
            {
                if (command == "STOP")
                {
                    shutdown.Set();
                    return;
                }
                if (command.StartsWith("EXIT ", StringComparison.Ordinal))
                {
                    Thread.Sleep(int.Parse(command[5..]));
                    shutdown.Set();
                    return;
                }
            }
            shutdown.Set();
        });
        Require(shutdown.Wait(TimeSpan.FromSeconds(45)), "Child shutdown timed out.");
        if (mode == "slow")
            Thread.Sleep(2500);
        MutexSingleInstance.Unregister();
        return 0;
    }

    private static int CountWindows(string name)
    {
        string title = @"\MutexSingleInstance\CSharp\{2A89458C-A9A1-402F-A3E6-BCB6848608EA}\" + name;
        int count = 0;
        Native.EnumCallback callback = (window, _) =>
        {
            StringBuilder text = new(title.Length + 1);
            Native.GetWindowTextW(window, text, text.Capacity);
            if (text.ToString() == title)
                count++;
            return true;
        };
        Require(Native.EnumWindows(callback, 0), "Window enumeration failed.");
        GC.KeepAlive(callback);
        return count;
    }

    private static void WaitUntil(Func<bool> predicate, string error)
    {
        Stopwatch timer = Stopwatch.StartNew();
        while (!predicate())
        {
            Require(timer.ElapsedMilliseconds < Timeout, error);
            Thread.Sleep(10);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class TestScope : IDisposable
    {
        private readonly List<Child> _children = new();
        public string Name { get; } = @"Local\SevenKeyboard.MutexSingleInstance.Tests." + Guid.NewGuid().ToString("N");
        public Child Start(string mode)
        {
            Child child = new(mode, Name);
            _children.Add(child);
            return child;
        }
        public void Dispose()
        {
            MutexSingleInstance.Unregister();
            foreach (Child child in _children)
                child.Dispose();
        }
    }

    private sealed class Child : IDisposable
    {
        public Process Process { get; }
        private readonly Task<string> _stderr;
        public Child(string mode, string name)
        {
            string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Missing process path.");
            Require(Path.GetFileNameWithoutExtension(executable) != "dotnet", "Use the generated apphost or dotnet run.");
            ProcessStartInfo start = new(executable)
            {
                UseShellExecute = false, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
            };
            foreach (string argument in new[] { "--child", mode, name })
                start.ArgumentList.Add(argument);
            Process = Process.Start(start)!;
            _stderr = Process.StandardError.ReadToEndAsync();
        }
        public void Ready() => Expect("READY");
        public void Expect(string expected)
        {
            string? line = Process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromMilliseconds(Timeout)).GetAwaiter().GetResult();
            Require(line == expected, $"Expected {expected}, received {line}; {(_stderr.IsCompletedSuccessfully ? _stderr.Result : "")}");
        }
        public void Send(string command)
        {
            Process.StandardInput.WriteLine(command);
            Process.StandardInput.Flush();
        }
        public void Exit(int? expectedCode = 0)
        {
            Require(Process.WaitForExit(Timeout), "Child did not exit.");
            if (expectedCode.HasValue)
                Require(Process.ExitCode == expectedCode.Value, $"Child exit {Process.ExitCode}; {_stderr.GetAwaiter().GetResult()}");
        }
        public void Dispose()
        {
            if (!Process.HasExited)
                Process.Kill(); // Only this exact harness-owned child is terminated.
            Require(Process.WaitForExit(Timeout), "Child cleanup failed.");
            Process.Dispose();
        }
    }

    private static class Native
    {
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal delegate bool EnumCallback(nint window, nint lParam);
        [DllImport("user32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumWindows(EnumCallback callback, nint lParam);
        [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        internal static extern int GetWindowTextW(nint window, StringBuilder text, int capacity);
        [DllImport("user32.dll", ExactSpelling = true)]
        internal static extern uint GetWindowThreadProcessId(nint window, out uint processId);
        [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        internal static extern uint RegisterWindowMessageW(string value);
        [DllImport("user32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowVisible(nint window);

        [StructLayout(LayoutKind.Sequential)]
        internal struct SECURITY_ATTRIBUTES
        {
            internal int Length;
            internal nint SecurityDescriptor;
            internal int InheritHandle;
        }
        [DllImport("advapi32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string sddl, uint revision, out nint descriptor, nint size);
        [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeWaitHandle CreateMutexExW(ref SECURITY_ATTRIBUTES attributes, string name, uint flags, uint access);
        [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeWaitHandle CreateMutexExW(nint attributes, string name, uint flags, uint access);
        [DllImport("kernel32.dll", ExactSpelling = true)]
        internal static extern nint LocalFree(nint memory);
    }
}
