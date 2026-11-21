using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace BarraInteligente;
internal static class Native
{
    internal delegate void WinEvent(IntPtr hook, uint evt, IntPtr hwnd, int obj, int child, uint thread, uint time);
    internal delegate bool EnumProc(IntPtr hwnd, IntPtr lparam);
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; public int Width => Right-Left; public int Height => Bottom-Top; }
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Auto)] internal struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X,Y; }
    [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr h,int id,uint modifiers,uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr h,int id);
    [DllImport("user32.dll")] internal static extern IntPtr SetWinEventHook(uint min,uint max,IntPtr module,WinEvent callback,uint pid,uint tid,uint flags);
    [DllImport("user32.dll")] internal static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc proc,IntPtr param);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] internal static extern bool IsZoomed(IntPtr h);
    [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr h,uint flags);
    [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr h,uint cmd);
    [DllImport("user32.dll")] internal static extern int GetWindowLong(IntPtr h,int index);
    [DllImport("user32.dll")] internal static extern int SetWindowLong(IntPtr h,int index,int value);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern int GetWindowText(IntPtr h,StringBuilder text,int max);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern int GetClassName(IntPtr h,StringBuilder text,int max);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr h,out Rect r);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int height,uint flags);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr h,int cmd);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] internal static extern IntPtr MonitorFromWindow(IntPtr h,uint flags);
    [DllImport("user32.dll")] internal static extern IntPtr MonitorFromPoint(Point p,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Auto)] internal static extern bool GetMonitorInfo(IntPtr h,ref MonitorInfo info);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point p);
    [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(IntPtr h,int attr,ref int value,int size);
    [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(IntPtr h,int attr,out int value,int size);
    [DllImport("dwmapi.dll",EntryPoint="DwmGetWindowAttribute")] internal static extern int DwmGetWindowRect(IntPtr h,int attr,out Rect value,int size);
    [DllImport("dwmapi.dll")] internal static extern int DwmExtendFrameIntoClientArea(IntPtr h,ref Margins margins);
    [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr h,uint msg,IntPtr w,IntPtr l);
    [StructLayout(LayoutKind.Sequential)] internal struct Margins { public int Left,Right,Top,Bottom; }
    [DllImport("user32.dll")] internal static extern IntPtr SendMessage(IntPtr h,uint msg,IntPtr w,IntPtr l);
    [DllImport("user32.dll")] internal static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr extra);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] internal static extern bool AttachThreadInput(uint first,uint second,bool attach);
    [DllImport("user32.dll")] internal static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] internal static extern IntPtr SetFocus(IntPtr h);
    internal static void FocusLauncher(IntPtr h)
    {
        if(!SetForegroundWindow(h)){var foreground=GetForegroundWindow();var other=GetWindowThreadProcessId(foreground,out _);var current=GetCurrentThreadId();var attached=other!=0 && other!=current && AttachThreadInput(current,other,true);try{BringWindowToTop(h);SetForegroundWindow(h);SetFocus(h);}finally{if(attached)AttachThreadInput(current,other,false);}}
        else SetFocus(h);
    }
    internal static string Title(IntPtr h) { var s=new StringBuilder(1024); GetWindowText(h,s,s.Capacity); return s.ToString(); }
    internal static string Path(IntPtr h) { try { GetWindowThreadProcessId(h,out var pid); using var p=Process.GetProcessById((int)pid); return p.MainModule?.FileName??""; } catch { return ""; } }
    internal static bool Eligible(IntPtr h)
    {
        if(h==IntPtr.Zero || !IsWindowVisible(h) || GetAncestor(h,2)!=h || GetWindow(h,4)!=IntPtr.Zero) return false;
        GetWindowThreadProcessId(h,out var pid); if(pid==Environment.ProcessId) return false;
        if((GetWindowLong(h,-20)&0x80)!=0 || string.IsNullOrWhiteSpace(Title(h))) return false;
        DwmGetWindowAttribute(h,14,out var cloaked,4); if(cloaked!=0) return false;
        var cls=new StringBuilder(256); GetClassName(h,cls,256);
        if(cls.ToString() is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Progman" or "WorkerW" or "Windows.UI.Core.CoreWindow") return false;
        return GetWindowRect(h,out var r) && r.Width>120 && r.Height>80;
    }
    internal static List<IntPtr> Windows() { var list=new List<IntPtr>(); EnumWindows((h,_)=>{ if(Eligible(h)) list.Add(h); return true; },IntPtr.Zero); return list; }
    internal static Rect WorkArea(IntPtr h) { var m=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()}; GetMonitorInfo(MonitorFromWindow(h,2),ref m); return m.Work; }
}

