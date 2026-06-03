#Requires AutoHotkey v1.1.35+
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
    static _ := VersionManager_MutexSingleInstance._init()
    _init()    {
        global
        MUTEXSINGLEINSTANCE_VERSION := "2.0.1"
    }
}
class MutexSingleInstance
{
    static _hMutex := 0
        ,_guiName := "MutexSingleInstance_ForceGui_2A89458C"
        ,_objbmOnMutexSingleInstanceTerminate := objBindMethod(MutexSingleInstance, "_onMutexSingleInstanceTerminate")
        ,_objbmExitApp := objBindMethod(MutexSingleInstance, "_exitApp")

    register(name := "", message := -1)    {
        static ERROR_ALREADY_EXISTS := 183
            ,MSGFLT_ALLOW           := 1
            ,MSGFLT_DISALLOW        := 2
            ,MSGFLT_RESET           := 0
        critical % format("{2}", prevIC := A_IsCritical, "On")
        name    := name !== "" ? subStr(name, 1, 259) : this._DefaultMutexName
        message := message !== -1 ? message : this.WM_MUTEXSINGLEINSTANCETERMINATE
        title   := "\MutexSingleInstance\Force\" this._NS "\" name
        if (this._hMutex)
            dllCall("Kernel32.dll\CloseHandle", "Ptr",this._hMutex), this._hMutex := 0
        this._hMutex := dllCall("Kernel32.dll\CreateMutex", "Ptr",0, "Int",false, "Str",name, "Ptr")
        if (A_LastError == ERROR_ALREADY_EXISTS)    {
            detectHiddenWindows % format("{2}", prevDHH := A_DetectHiddenWindows, "On")
            setTitleMatchMode   % format("{2}", prevTMM := A_TitleMatchMode, 3)
            setTitleMatchMode   % format("{2}", prevTMMS := A_TitleMatchModeSpeed, "Fast")
            winGet id, List, % title
            loop % id    {
                controlGetText mainHwnd, % "Edit1", % "ahk_id " (id%A_Index%)
                mainHwnd += 0
                if (!mainHwnd)
                    continue
                if (!errorLevel && A_ScriptHwnd&0xffffffff !== mainHwnd&0xffffffff)
                    dllCall("User32.dll\PostMessage", "Ptr",mainHwnd, "UInt",message, "UPtr",0, "Ptr",0)
            }
            detectHiddenWindows % prevDHH
            setTitleMatchMode   % prevTMM
            setTitleMatchMode   % prevTMMS
        }
        prevDG := A_DefaultGui
        gui % this._guiName ":New",, % title
        gui Add, Edit, ReadOnly, % A_ScriptHwnd
        gui % prevDG ":Default"
        if (A_IsAdmin)
            dllCall("User32.dll\ChangeWindowMessageFilterEx", "Ptr",A_ScriptHwnd, "UInt",message, "UInt",MSGFLT_ALLOW, "Ptr",0)
        onMessage(message, this._objbmOnMutexSingleInstanceTerminate, -1)
        critical % prevIC
    }
    unregister(name := "", message := -1)    {
        static MSGFLT_ALLOW         := 1
            ,MSGFLT_DISALLOW        := 2
            ,MSGFLT_RESET           := 0
        critical % format("{2}", prevIC := A_IsCritical, "On")
        name    := name !== "" ? subStr(name, 1, 259) : this._DefaultMutexName
        message := message !== -1 ? message : this.WM_MUTEXSINGLEINSTANCETERMINATE
        title   := "\MutexSingleInstance\Force\" this._NS "\" name
        if (this._hMutex)
            dllCall("Kernel32.dll\CloseHandle", "Ptr",this._hMutex), this._hMutex := 0
        prevDG := A_DefaultGui
        gui % this._guiName ":+LastFoundExist"
        if (winExist())
            gui % this._guiName ":Destroy"
        gui % prevDG ":Default"
        if (A_IsAdmin)
            dllCall("User32.dll\ChangeWindowMessageFilterEx", "Ptr",A_ScriptHwnd, "UInt",message, "UInt",MSGFLT_RESET, "Ptr",0)
        onMessage(message, this._objbmOnMutexSingleInstanceTerminate, 0)
        critical % prevIC
    }
    isAlreadyExisting(name := "")    {
        static ERROR_ALREADY_EXISTS := 183
        critical % format("{2}", prevIC := A_IsCritical, "On")
        hMutex  := dllCall("Kernel32.dll\CreateMutex", "Ptr",0, "Int",false, "Str",name !== "" ? subStr(name, 1, 259) : this._DefaultMutexName, "Ptr")
        bRet    := (A_LastError == ERROR_ALREADY_EXISTS)
        if (hMutex)
            dllCall("Kernel32.dll\CloseHandle", "Ptr",hMutex)
        critical % prevIC
        return bRet
    }
    getExistingHwnd(name := "")    {
        static ERROR_ALREADY_EXISTS := 183
        hWnd    := 0
        name    := name !== "" ? subStr(name, 1, 259) : this._DefaultMutexName
        title   := "\MutexSingleInstance\Force\" this._NS "\" name
        hMutex  := dllCall("Kernel32.dll\CreateMutex", "Ptr",0, "Int",false, "Str",name, "Ptr")
        if (A_LastError == ERROR_ALREADY_EXISTS)    {
            detectHiddenWindows % format("{2}", prevDHH := A_DetectHiddenWindows, "On")
            setTitleMatchMode   % format("{2}", prevTMM := A_TitleMatchMode, 3)
            setTitleMatchMode   % format("{2}", prevTMMS := A_TitleMatchModeSpeed, "Fast")
            winGet id, List, % title
            loop % id    {
                controlGetText mainHwnd, % "Edit1", % "ahk_id " (id%A_Index%)
                mainHwnd += 0
                if (!mainHwnd)
                    continue
                if (!errorLevel && A_ScriptHwnd&0xffffffff !== mainHwnd&0xffffffff)    {
                    hWnd := mainHwnd
                    break
                }
            }
            detectHiddenWindows % prevDHH
            setTitleMatchMode   % prevTMM
            setTitleMatchMode   % prevTMMS
        }
        if (hMutex)
            dllCall("Kernel32.dll\CloseHandle", "Ptr",hMutex)
        return hWnd
    }
    ;--------------------------------------------------------------------------
    closeInstances(name := "", message := -1, includeSelf := false)    {
        static ERROR_ALREADY_EXISTS := 183
        critical % format("{2}", prevIC := A_IsCritical, "On")
        name    := name !== "" ? subStr(name, 1, 259) : this._DefaultMutexName
        message := message !== -1 ? message : this.WM_MUTEXSINGLEINSTANCETERMINATE
        title   := "\MutexSingleInstance\Force\" this._NS "\" name
        hMutex  := dllCall("Kernel32.dll\CreateMutex", "Ptr",0, "Int",false, "Str",name, "Ptr")
        ret := 0
        if (A_LastError == ERROR_ALREADY_EXISTS)    {
            detectHiddenWindows % format("{2}", prevDHH := A_DetectHiddenWindows, "On")
            setTitleMatchMode   % format("{2}", prevTMM := A_TitleMatchMode, 3)
            setTitleMatchMode   % format("{2}", prevTMMS := A_TitleMatchModeSpeed, "Fast")
            winGet id, List, % title
            loop % id    {
                controlGetText mainHwnd, % "Edit1", % "ahk_id " (id%A_Index%)
                mainHwnd += 0
                if (!mainHwnd)
                    continue
                if (!errorLevel)    {
                    if (!includeSelf && A_ScriptHwnd&0xffffffff == mainHwnd&0xffffffff)
                        continue
                    dllCall("User32.dll\PostMessage", "Ptr",mainHwnd, "UInt",message, "UPtr",0, "Ptr",0)
                    ++ret
                }
            }
            detectHiddenWindows % prevDHH
            setTitleMatchMode   % prevTMM
            setTitleMatchMode   % prevTMMS
        }            
        if (hMutex)
            dllCall("Kernel32.dll\CloseHandle", "Ptr",hMutex)
        critical % prevIC
        return ret
    }
    ;--------------------------------------------------------------------------
    terminateProcesses(name := "", winCloseTimeout := 4, processCloseTimeout := 4)    {
        static ERROR_ALREADY_EXISTS := 183
        name    := name !== "" ? subStr(name, 1, 259) : this._DefaultMutexName
        title   := "\MutexSingleInstance\Force\" this._NS "\" name
        hMutex  := dllCall("Kernel32.dll\CreateMutex", "Ptr",0, "Int",false, "Str",name, "Ptr")
        if (A_LastError == ERROR_ALREADY_EXISTS)    {
            detectHiddenWindows % format("{2}", prevDHH := A_DetectHiddenWindows, "On")
            setTitleMatchMode   % format("{2}", prevTMM := A_TitleMatchMode, 3)
            setTitleMatchMode   % format("{2}", prevTMMS := A_TitleMatchModeSpeed, "Fast")
            winGet id, List, % title
            loop % id    {
                controlGetText mainHwnd, % "Edit1", % "ahk_id " (id%A_Index%)
                mainHwnd += 0
                if (!mainHwnd)
                    continue
                if (!errorLevel)    {
                    winClose % "ahk_id " mainHwnd
                    winWaitClose % "ahk_id " mainHwnd,, % winCloseTimeout
                    if (!errorLevel)
                        continue
                    winGet pid, PID, % "ahk_id " mainHwnd
                    if (pid == "")
                        winGet pid, PID, % "ahk_id " (id%A_Index%)
                }  else  {
                    winGet pid, PID, % "ahk_id " (id%A_Index%)
                }
                if (pid !== "")    {
                    process Close, % pid
                    process WaitClose, % pid, % processCloseTimeout
                }
            }
            detectHiddenWindows % prevDHH
            setTitleMatchMode   % prevTMM
            setTitleMatchMode   % prevTMMS
        }
        if (hMutex)
            dllCall("Kernel32.dll\CloseHandle", "Ptr",hMutex)
    }
    ;--------------------------------------------------------------------------
    _onMutexSingleInstanceTerminate(wParam, lParam, msg, hWnd)    {
        objbm := this._objbmExitApp
        setTimer % objbm, -1
        return 0
    }
    _exitApp()    {
        ExitApp
    }
    ;--------------------------------------------------------------------------
    _DefaultMutexName    {
        get  {
            static name
            if (!isSet(name))    {
                winGetTitle title, % "ahk_id " A_ScriptHwnd
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
    _NS    {
        get  {
            return "{2A89458C-A9A1-402F-A3E6-BCB6848608EA}"
        }
    }
    WM_MUTEXSINGLEINSTANCETERMINATE    {
        get  {
            return dllCall("User32.dll\RegisterWindowMessage", "Str",this._NS ":MutexSingleInstance:Terminate", "UInt")
        }
    }
}