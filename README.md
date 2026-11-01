# MutexSingleInstance

Named mutex-based single-instance helper for C#.

Windows / .NET 10 / NativeAOT compatible · 3.0.0 · MIT License

## Build and reference

```powershell
dotnet build src/SevenKeyboard.MutexSingleInstance -c Release
```

Reference `src/SevenKeyboard.MutexSingleInstance/SevenKeyboard.MutexSingleInstance.csproj`
from your application. No additional native DLL or external packages are required.

## Usage

```csharp
using System.Threading;
using SevenKeyboard;

using ManualResetEventSlim shutdown = new();
try
{
    bool registered = MutexSingleInstance.Register(
        () => shutdown.Set(),
        name: @"Local\MyApp:{00000000-0000-0000-0000-000000000001}",
        previousExitTimeout: 4);

    if (registered)
        shutdown.Wait(); // The application owns its run loop and shutdown cleanup.
}
finally
{
    MutexSingleInstance.Unregister();
}
```

`Register` opens or creates the named mutex and waits to own it. While waiting, it
asks each owner it finds to close, once per owner. The registration that owns the
mutex publishes its receiving window; waiting registrations stay hidden, so they
never close each other. Registrations still waiting ask each new owner to close,
so a single owner remains even when several processes start at the same time.
Ownership is released by `Unregister` or when the process exits, including a crash.
One registration is maintained per process; registering again replaces it.

`previousExitTimeout` is in **seconds** and limits the wait for ownership: zero (the
default) tries once without waiting, and a negative value waits indefinitely.
`Register` returns `true` when this process owns the mutex. On timeout it returns
`false` without registering, for example when the owner ignores the close request
or has stopped responding. Among processes waiting at the same time, the order in
which they become the owner is not defined.

If the mutex exists but this process may not open it, as when a non-elevated process
starts while an elevated instance is running, `Register` returns `false` without
registering or requesting closure. Invalid arguments and receiving-window setup
failures throw exceptions.

The close callback runs on the thread pool, with at most one request pending or
running at a time.
A later request can invoke it again if the application remained running. A request received
before `Register` returns waits until it does. The callback
runs outside the internal lock and may call `Unregister`; dispatch UI work to
its UI thread. Unregistering cancels callbacks that have not started; an already
started callback can finish afterward. A callback may start before the caller
executes its next statement after `Register`, so do not depend on that timing.

The hidden receiving window uses a background thread and does not keep the app
alive. The library notifies the application to shut down; the application performs
cleanup and exits. `Register` does not forcibly terminate other processes.

## Instance identity

Pass a stable name to group different executable paths or builds. If omitted, the
name is based on the entry executable/DLL path, prefixed with `Local\`
and escaped (`%` to `%25`, `\` to `%5C`). Names are truncated to 259 characters.
Names are case-sensitive; Windows namespace and access rules apply. An explicitly empty
name is passed through to Windows and does not select the default identity.

Discovery uses windows on the calling thread's desktop. A `Global\` mutex name
does not make cross-session window discovery possible. Each registration has one
hidden receiving window, found by its C#-specific title. Both processes must use
the same name and message to cooperate; this protocol does not coordinate with
the AutoHotkey implementation. This is coordination between cooperating
applications, not authentication.

## Other operations

- `Unregister()` releases this process's registration and receiving window.
- `IsAlreadyExisting(name)` checks whether the named mutex already exists, including
  one this process may not open.
- `GetExistingHwnd(name)` returns the first discovered receiver other than this
  registration, or zero.
- `CloseInstances(name, message, includeSelf: false)` posts a close request to all
  matching receivers and returns their count, without waiting for exit.
- `TerminateProcesses(name, winCloseTimeout: 4, processCloseTimeout: 4)` posts
  `WM_CLOSE`, waits for each window to close, and forcibly terminates a process
  whose window remains. It includes the caller if registered under that name.
  It continues past failures and returns `false` if any instance could not be
  confirmed closed.

All timeout arguments use seconds; negative values mean an indefinite wait.
`message` is optional and defaults to `TerminateMessage`. Use a registered window
message when supplying your own. The receiver tries to allow that message through UIPI;
Windows process access checks still apply to waiting and force termination.

## Verification

```powershell
dotnet run --project tests/SevenKeyboard.MutexSingleInstance.Tests -c Release
dotnet publish tests/SevenKeyboard.MutexSingleInstance.Tests -c Release -r win-x64 -p:PublishAot=true
```

Run the published executable to verify NativeAOT behavior as well.
