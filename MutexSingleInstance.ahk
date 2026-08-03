#Requires AutoHotkey v2.0.0+
;==============================================================
; MutexSingleInstance — Named mutex-based single-instance helper
;
; GitHub: https://github.com/SevenKeyboard/mutex-single-instance
; Author: SevenKeyboard Ltd. (2025)
; License: MIT License
;==============================================================

/*
Example Usage:
    #SingleInstance Off
    MutexSingleInstance.register() ;  "Local\%PROGRAM_NAME%:{00000000-0000-0000-0000-000000000000}"
*/

class VersionManager_MutexSingleInstance
{
    static _ := this._init()
    static _init()    {
        global
        MUTEXSINGLEINSTANCE_VERSION := "2.1.0"
    }
}
class MutexSingleInstance
{
    static _hMutex := 0
        ,_gui := ""
        ,_objbmOnMutexSingleInstanceTerminate := objBindMethod(this, "_onMutexSingleInstanceTerminate")
        ,_objbmExitApp := objBindMethod(this, "_exitApp")

    static register(name?, message?, previousExitTimeout := 0)    {
        static ERROR_ALREADY_EXISTS := 183
            ,MSGFLT_ALLOW           := 1
            ,MSGFLT_DISALLOW        := 2
            ,MSGFLT_RESET           := 0
            ,SYNCHRONIZE            := 0x00100000
        prevIC  := critical("On")
        name    := isSet(name)? subStr(name, 1, 259) : this._DefaultMutexName
        message := message??this.WM_MUTEXSINGLEINSTANCETERMINATE
        title   := "\MutexSingleInstance\Force\" this._NS "\" name
        previousProcessHandles := []
        previousProcessIds := map()
        previousProcessesWaitable := true
        if (this._hMutex)
            dllCall("Kernel32.dll\CloseHandle", "Ptr",this._hMutex), this._hMutex := 0
        this._hMutex := dllCall("Kernel32.dll\CreateMutex", "Ptr",0, "Int",false, "Str",name, "Ptr")
        if (A_LastError == ERROR_ALREADY_EXISTS)    {
            prevDHH := detectHiddenWindows(true)
            prevTMM := setTitleMatchMode(3)
            prevTMMS := setTitleMatchMode("Fast")
            ids := winGetList(title)
            for id in ids    {
                try  {
                    mainHwnd := integer(controlGetText("Edit1", "ahk_id " id))
                }  catch  {
                }  else  {
                    if (!mainHwnd)
                        continue
                    if (A_ScriptHwnd&0xffffffff !== mainHwnd&0xffffffff)    {
                        if (previousExitTimeout)    {
                            pid := 0
                            dllCall("User32.dll\GetWindowThreadProcessId", "Ptr",mainHwnd, "UInt*",&pid)
                            if (pid && !previousProcessIds.has(pid))    {
                                previousProcessIds[pid] := true
                                hProcess := dllCall("Kernel32.dll\OpenProcess", "UInt",SYNCHRONIZE, "Int",false, "UInt",pid, "Ptr")
                                if (hProcess)
                                    previousProcessHandles.push(hProcess)
                                else
                                    previousProcessesWaitable := false
                            }
                        }
                        dllCall("User32.dll\PostMessage", "Ptr",mainHwnd, "UInt",message, "UPtr",0, "Ptr",0)
                    }
                }
            }
            detectHiddenWindows(prevDHH)
            setTitleMatchMode(prevTMM)
            setTitleMatchMode(prevTMMS)
        }
        if (this._gui)
            this._gui.destroy()
        this._gui := gui(, title)
        this._gui.addEdit("ReadOnly", A_ScriptHwnd)
        if (A_IsAdmin)
            dllCall("User32.dll\ChangeWindowMessageFilterEx", "Ptr",A_ScriptHwnd, "UInt",message, "UInt",MSGFLT_ALLOW, "Ptr",0)
        onMessage(message, this._objbmOnMutexSingleInstanceTerminate, -1)
        previousInstancesExited := true
        if (previousExitTimeout)    {
            processesExited := this._waitForProcessesToExit(previousProcessHandles, previousExitTimeout)
            previousInstancesExited := previousProcessesWaitable && processesExited
        }
        critical(prevIC)
        return previousInstancesExited
    }
    static unregister(name?, message?)    {
        static MSGFLT_ALLOW         := 1
            ,MSGFLT_DISALLOW        := 2
            ,MSGFLT_RESET           := 0
        prevIC  := critical("On")
        name    := isSet(name)? subStr(name, 1, 259) : this._DefaultMutexName
        message := message??this.WM_MUTEXSINGLEINSTANCETERMINATE
        title   := "\MutexSingleInstance\Force\" this._NS "\" name
        if (this._hMutex)
            dllCall("Kernel32.dll\CloseHandle", "Ptr",this._hMutex), this._hMutex := 0
        if (this._gui)
            this._gui.destroy(), this._gui := ""
        if (A_IsAdmin)
            dllCall("User32.dll\ChangeWindowMessageFilterEx", "Ptr",A_ScriptHwnd, "UInt",message, "UInt",MSGFLT_RESET, "Ptr",0)
        onMessage(message, this._objbmOnMutexSingleInstanceTerminate, 0)
        critical(prevIC)
    }
    static isAlreadyExisting(name?)    {
        static ERROR_ALREADY_EXISTS := 183
        prevIC  := critical("On")
        hMutex  := dllCall("Kernel32.dll\CreateMutex", "Ptr",0, "Int",false, "Str",isSet(name)? subStr(name, 1, 259) : this._DefaultMutexName, "Ptr")
        bRet    := (A_LastError == ERROR_ALREADY_EXISTS)
        if (hMutex)
            dllCall("Kernel32.dll\CloseHandle", "Ptr",hMutex)
        critical(prevIC)
        return bRet
    }
    static getExistingHwnd(name?)    {
        static ERROR_ALREADY_EXISTS := 183
        hWnd    := 0
        name    := isSet(name)? subStr(name, 1, 259) : this._DefaultMutexName
        title   := "\MutexSingleInstance\Force\" this._NS "\" name
        hMutex  := dllCall("Kernel32.dll\CreateMutex", "Ptr",0, "Int",false, "Str",name, "Ptr")
        if (A_LastError == ERROR_ALREADY_EXISTS)    {
            prevDHH := detectHiddenWindows(true)
            prevTMM := setTitleMatchMode(3)
            prevTMMS := setTitleMatchMode("Fast")
            ids := winGetList(title)
            for id in ids    {
                try  {
                    mainHwnd := integer(controlGetText("Edit1", "ahk_id " id))
                }  catch  {
                }  else  {
                    if (!mainHwnd)
                        continue
                    if (A_ScriptHwnd&0xffffffff !== mainHwnd&0xffffffff)    {
                        hWnd := mainHwnd
                        break
                    }                        
                }
            }
            detectHiddenWindows(prevDHH)
            setTitleMatchMode(prevTMM)
            setTitleMatchMode(prevTMMS)
        }
        if (hMutex)
            dllCall("Kernel32.dll\CloseHandle", "Ptr",hMutex)
        return hWnd
    }
    ;--------------------------------------------------------------------------
    static closeInstances(name?, message?, includeSelf := false)    {
        static ERROR_ALREADY_EXISTS := 183
        prevIC  := critical("On")
        name    := isSet(name)? subStr(name, 1, 259) : this._DefaultMutexName
        message := message??this.WM_MUTEXSINGLEINSTANCETERMINATE
        title   := "\MutexSingleInstance\Force\" this._NS "\" name
        hMutex := dllCall("Kernel32.dll\CreateMutex", "Ptr",0, "Int",false, "Str",name, "Ptr")
        ret := 0
        if (A_LastError == ERROR_ALREADY_EXISTS)    {
            prevDHH := detectHiddenWindows(true)
            prevTMM := setTitleMatchMode(3)
            prevTMMS := setTitleMatchMode("Fast")
            ids := winGetList(title)
            for id in ids    {
                try  {
                    mainHwnd := integer(controlGetText("Edit1", "ahk_id " id))
                }  catch  {
                }  else  {
                    if (!mainHwnd)
                        continue
                    if (!includeSelf && A_ScriptHwnd&0xffffffff == mainHwnd&0xffffffff)
                        continue
                    dllCall("User32.dll\PostMessage", "Ptr",mainHwnd, "UInt",message, "UPtr",0, "Ptr",0)
                    ++ret
                }
            }
            detectHiddenWindows(prevDHH)
            setTitleMatchMode(prevTMM)
            setTitleMatchMode(prevTMMS)
        }        
        if (hMutex)
            dllCall("Kernel32.dll\CloseHandle", "Ptr",hMutex)
        critical(prevIC)
        return ret
    }
    ;--------------------------------------------------------------------------
    static terminateProcesses(name?, winCloseTimeout := 4, processCloseTimeout := 4)    {
        static ERROR_ALREADY_EXISTS := 183
        name    := isSet(name)? subStr(name, 1, 259) : this._DefaultMutexName
        title   := "\MutexSingleInstance\Force\" this._NS "\" name
        hMutex  := dllCall("Kernel32.dll\CreateMutex", "Ptr",0, "Int",false, "Str",name, "Ptr")
        if (A_LastError == ERROR_ALREADY_EXISTS)    {
            prevDHH := detectHiddenWindows(true)
            prevTMM := setTitleMatchMode(3)
            prevTMMS := setTitleMatchMode("Fast")
            ids := winGetList(title)
            for id in ids    {
                pid := 0
                try  {
                    mainHwnd := integer(controlGetText("Edit1", "ahk_id " id))
                }  catch  {
                    try pid := winGetPID("ahk_id " id)
                }  else  {
                    if (!mainHwnd)
                        continue
                    winClose("ahk_id " mainHwnd)
                    if (winWaitClose("ahk_id " mainHwnd,, winCloseTimeout))
                        continue
                    try  {
                        pid := winGetPID("ahk_id " mainHwnd)
                    }  catch  {
                        try pid := winGetPID("ahk_id " id)
                    }
                }
                if (pid)    {
                    processClose(pid)
                    processWaitClose(pid, processCloseTimeout)
                }
            }
            detectHiddenWindows(prevDHH)
            setTitleMatchMode(prevTMM)
            setTitleMatchMode(prevTMMS)
        }
        if (hMutex)
            dllCall("Kernel32.dll\CloseHandle", "Ptr",hMutex)
    }
    ;--------------------------------------------------------------------------
    static _onMutexSingleInstanceTerminate(wParam, lParam, msg, hWnd)    {
        setTimer(this._objbmExitApp, -1)
        return 0
    }
    static _exitApp() => exitApp()
    static _waitForProcessesToExit(processHandles, timeout)    {
        static WAIT_OBJECT_0 := 0x00000000
            ,INFINITE        := 0xFFFFFFFF
        result    := true
        startTick := this._TickCount
        try  {
            for hProcess in processHandles    {
                if (timeout < 0)    {
                    timeoutMs := INFINITE
                }  else  {
                    elapsedMs := this._TickCount - startTick
                    timeoutMs := max(0, floor(timeout * 1000 - elapsedMs))
                }
                waitResult := dllCall("Kernel32.dll\WaitForSingleObject", "Ptr",hProcess, "UInt",timeoutMs, "UInt")&0xFFFFFFFF
                if (waitResult !== WAIT_OBJECT_0)    {
                    result := false
                    break
                }
            }
        }  finally  {
            for hProcess in processHandles
                dllCall("Kernel32.dll\CloseHandle", "Ptr",hProcess)
        }
        return result
    }
    static _TickCount    {
        get  {
            return (A_Is64BitOS
                ? dllCall("Kernel32.dll\GetTickCount64", "Int64")&0x7FFFFFFFFFFFFFFF
                : dllCall("Kernel32.dll\GetTickCount", "UInt"))
        }
    }
    ;--------------------------------------------------------------------------
    static _DefaultMutexName    {
        get  {
            static name
            if (!isSet(name))    {
                try  {
                    title := winGetTitle(A_ScriptHwnd)
                }  catch  {
                    title := ""
                }
                if (title == "")
                    title := A_ScriptFullPath
                title := strReplace(title, "%", "%25")
                title := strReplace(title, "\", "%5C")
                name := "Local\" title
                if (259 < strLen(name)) ;  MAX_PATH
                    name := subStr(name, 1, 259)
            }
            return name
        }
    }
    static _NS => "{2A89458C-A9A1-402F-A3E6-BCB6848608EA}"
    static WM_MUTEXSINGLEINSTANCETERMINATE => dllCall("User32.dll\RegisterWindowMessage", "Str",this._NS ":MutexSingleInstance:Terminate", "UInt")
}
