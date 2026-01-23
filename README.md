# MutexSingleInstance
Named mutex-based single-instance helper for AutoHotkey.

This repository provides the same library for **AutoHotkey v2.0** and **v1.1**, maintained on separate branches:
- `main-ahkv2.0`
- `main-ahkv1.1`

The file name and public API are identical on both branches.

## Rationale
AutoHotkey already provides `#SingleInstance Force`, but it can fail when **one instance is elevated (admin) and another is not**.  
In that situation, Windows message filtering (UIPI) may block cross-instance messages, making the built-in “force/close other instance” mechanism unreliable.

This library addresses that problem by:
- using a **named mutex** as the authoritative instance detector, and
- using a **registered window message** to request termination.

When running with elevated privileges, it additionally calls `ChangeWindowMessageFilterEx` so the terminate message is allowed through UIPI.

Because the **mutex name defines the instance identity**, this approach also allows you to explicitly control what “the same instance” means.

## Design overview
1. Create or open a named mutex via `CreateMutex`.
2. If the mutex already exists, locate other instances and send them a terminate message.
3. Each instance listens for that message and exits cleanly.

## Instance identity and mutex names

### Default behavior
If no name is provided, a default mutex name is derived from the script's  
**[Main Window Title](https://www.autohotkey.com/docs/v2/Program.htm#title)**  
(fallback: `A_ScriptFullPath`) and sanitized as follows:

- Prefix: `Local\`
- Escaping: `%` → `%25`, `\` → `%5C`
- Truncation: limited to 259 characters

This default matches the usual `#SingleInstance Force` behavior: different entry points (such as different paths) are treated as different instance identities unless you provide an explicit name.

### Custom mutex names
By supplying an explicit name, you define *what constitutes the same instance group*.

This is useful when you want multiple launch forms to be treated as one app, for example:
- the same code run as both `.ahk` and a compiled `.exe`,
- the same program launched from different paths,
- wrapper or launcher scripts that should still enforce a shared single instance.

If two processes use the same mutex name, this library will treat them as belonging to the same instance group, regardless of how they were launched.

## Public API
- `register(name?, message?)`
- `unregister(name?, message?)`
- `isAlreadyExisting(name?) -> bool`
- `getExistingHwnd(name?) -> hWnd | 0`
- `closeInstances(name?, message?, includeSelf := false) -> count`
- `terminateProcesses(name?, winCloseTimeout := 4000, processCloseTimeout := 4000)`

## Examples

### Basic single-instance usage
```ahk
#Requires AutoHotkey v2.0
#SingleInstance Off
#Include <MutexSingleInstance>

MutexSingleInstance.register()
Persistent
````

### Unified instance identity across builds and paths

Use a stable, app-level mutex name to unify `.ahk` and `.exe` builds (or multiple install locations) under one single-instance identity:

```ahk
#Requires AutoHotkey v2.0
#SingleInstance Off
#Include <MutexSingleInstance>

PROGRAM_NAME := "MyApp"
APP_GUID := "{00000000-0000-0000-0000-000000000000}" ; replace with a real GUID

MutexSingleInstance.register("Local\" PROGRAM_NAME ":" APP_GUID)
Persistent
```

> Tip: Assign one GUID per product and keep it stable across releases so all variants share the same instance group.

## Forum Thread
AutoHotkey forum discussion and support:

https://www.autohotkey.com/boards/viewtopic.php?t=XXXXXX