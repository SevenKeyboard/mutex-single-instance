# mutex-single-instance
Named mutex-based single-instance helper for AutoHotkey.

This repo provides the same library for **AutoHotkey v2.0** and **v1.1** (each on its own branch: `main-ahkv2.0`, `main-ahkv1.1`). The file name is identical on both branches.

## Why this exists (vs [#SingleInstance](https://www.autohotkey.com/docs/v2/lib/_SingleInstance.htm))
AutoHotkey already has `#SingleInstance Force`, but it can fail to behave as expected when **one instance is elevated (admin) and the other is not**. In that scenario, cross-instance messaging can be blocked by Windows’ message filtering (UIPI), so the “force/close other instance” mechanism becomes unreliable.

This library avoids that by:
- using a **named mutex** for instance detection, and
- using a **registered window message** for termination requests.
  When running as **admin**, it also calls `ChangeWindowMessageFilterEx` to allow the terminate message through Windows message filtering (UIPI).

## How it works (overview)
1. Create/open a named mutex (`CreateMutex`).
2. If it already exists, locate other instances and send them a terminate message.
3. Each instance listens for that message and exits.

## Default mutex name
If `name` is omitted, a default name is derived from the script’s **[Main Window Title](https://www.autohotkey.com/docs/v2/Program.htm#title)** (fallback: `A_ScriptFullPath`) and sanitized:

- Prefix: `Local\`
- Escapes: `%` → `%25`, `\` → `%5C`
- Truncates to 259 characters

You can pass your own `name` to explicitly control instance identity.

## API (common)
- `register(name?, message?)`
- `unregister(name?, message?)`
- `isAlreadyExisting(name?) -> bool`
- `getExistingHwnd(name?) -> hWnd|0`
- `closeInstances(name?, message?, includeSelf := false) -> count`
- `terminateProcesses(name?, winCloseTimeout := 4000, processCloseTimeout := 4000)`

## Example (AutoHotkey v2)

```ahk
#Requires AutoHotkey v2.0
#SingleInstance Off
#Include <MutexSingleInstance>

MutexSingleInstance.register() ;  For single-instance use, no explicit unregister() is needed: the mutex is released automatically when the process exits.
persistent
```