using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
namespace BarraInteligente;
internal sealed record OpenWindow(IntPtr Handle,string Title,string Process,string Path);
// Ações sobre janelas de outros aplicativos. "Alvo" é a janela que estava em foco
// quando a barra foi aberta (ou quando um atalho personalizado foi pressionado).
internal static class WindowActions
{
 internal static IntPtr Target {get;private set;}
 internal static void Capture(IntPtr launcher){var h=Native.GetForegroundWindow();Target=h!=launcher && Native.Eligible(h)?h:IntPtr.Zero;}
 internal static void Use(IntPtr h)=>Target=h;
 internal static string? TargetTitle=>Target!=IntPtr.Zero && Native.IsWindow(Target)?Native.Title(Target):null;
 private static IntPtr Require()
 {
  if(Target==IntPtr.Zero || !Native.IsWindow(Target))throw new InvalidOperationException("Nenhuma janela estava em foco antes de abrir a barra.");
  return Target;
 }
 internal static void Maximize(){var h=Require();Native.ShowWindow(h,3);Activate(h);}
 internal static void Minimize()=>Native.ShowWindow(Require(),6);
 internal static void Restore(){var h=Require();Native.ShowWindow(h,9);Activate(h);}
 internal static void Close()=>Native.PostMessage(Require(),0x10,IntPtr.Zero,IntPtr.Zero);
 // Retorna true quando a janela passa a ficar sempre no topo.
 internal static bool ToggleTopmost()
 {
  var h=Require();var topmost=(Native.GetWindowLong(h,-20)&0x8)!=0;
  if(!Native.SetWindowPos(h,new IntPtr(topmost?-2:-1),0,0,0,0,0x3|0x10))throw new InvalidOperationException("O Windows recusou a alteração (a janela pode ser de um aplicativo elevado).");
  Activate(h);return !topmost;
 }
 internal static void Snap(string where)
 {
  var h=Require();if(Native.IsZoomed(h) || Native.IsIconic(h))Native.ShowWindow(h,9);var a=Native.WorkArea(h);
  var (x,y,w,height)=where switch
  {
   "left"=>(a.Left,a.Top,a.Width/2,a.Height),
   "right"=>(a.Left+a.Width/2,a.Top,a.Width-a.Width/2,a.Height),
   "top"=>(a.Left,a.Top,a.Width,a.Height/2),
   "bottom"=>(a.Left,a.Top+a.Height/2,a.Width,a.Height-a.Height/2),
   _=>Centered(h,a)
  };
  Place(h,x,y,w,height);Activate(h);
 }
 private static (int,int,int,int) Centered(IntPtr h,Native.Rect a)
 {
  Visible(h,out var r);var w=Math.Min(r.Width,a.Width);var height=Math.Min(r.Height,a.Height);
  return (a.Left+(a.Width-w)/2,a.Top+(a.Height-height)/2,w,height);
 }
 // Move para o próximo monitor mantendo a posição relativa.
 internal static void NextMonitor()
 {
  var h=Require();var screens=System.Windows.Forms.Screen.AllScreens;if(screens.Length<2)throw new InvalidOperationException("Há apenas um monitor conectado.");
  var zoomed=Native.IsZoomed(h);if(zoomed)Native.ShowWindow(h,9);
  Visible(h,out var r);var current=System.Windows.Forms.Screen.FromHandle(h);var next=screens[(Array.IndexOf(screens,current)+1)%screens.Length];
  var from=current.WorkingArea;var to=next.WorkingArea;
  var w=Math.Min(r.Width,to.Width);var height=Math.Min(r.Height,to.Height);
  var x=to.Left+(int)((r.Left-from.Left)*(double)to.Width/Math.Max(1,from.Width));var y=to.Top+(int)((r.Top-from.Top)*(double)to.Height/Math.Max(1,from.Height));
  Place(h,Math.Clamp(x,to.Left,to.Right-w),Math.Clamp(y,to.Top,to.Bottom-height),w,height);if(zoomed)Native.ShowWindow(h,3);Activate(h);
 }
 // Retângulo visível (sem a borda invisível de redimensionamento do Windows 10/11).
 private static void Visible(IntPtr h,out Native.Rect r){if(Native.DwmGetWindowRect(h,9,out r,System.Runtime.InteropServices.Marshal.SizeOf<Native.Rect>())!=0)Native.GetWindowRect(h,out r);}
 private static void Place(IntPtr h,int x,int y,int w,int height)
 {
  Native.GetWindowRect(h,out var outer);Visible(h,out var inner);
  var left=inner.Left-outer.Left;var top=inner.Top-outer.Top;var right=outer.Right-inner.Right;var bottom=outer.Bottom-inner.Bottom;
  if(!Native.SetWindowPos(h,IntPtr.Zero,x-left,y-top,w+left+right,height+top+bottom,0x4|0x10))throw new InvalidOperationException("O Windows recusou o posicionamento (a janela pode ser de um aplicativo elevado).");
 }
 internal static void Activate(IntPtr h){if(Native.IsIconic(h))Native.ShowWindow(h,9);Native.FocusLauncher(h);}
 internal static List<OpenWindow> List()
 {
  var names=new Dictionary<uint,(string,string)>();var list=new List<OpenWindow>();
  foreach(var h in Native.Windows())
  {
   Native.GetWindowThreadProcessId(h,out var pid);
   if(!names.TryGetValue(pid,out var info))
   {
    try{using var p=Process.GetProcessById((int)pid);string path;try{path=p.MainModule?.FileName??"";}catch{path="";}info=(p.ProcessName,path);}catch{info=("","");}
    names[pid]=info;
   }
   list.Add(new OpenWindow(h,Native.Title(h),info.Item1,info.Item2));
  }
  return list;
 }
 internal static List<OpenWindow> Find(string normalizedQuery)
 {
  var all=List();if(normalizedQuery.Length==0)return all;var tokens=normalizedQuery.Split(' ',StringSplitOptions.RemoveEmptyEntries);
  var exact=all.Where(w=>tokens.All(t=>Commands.Normalize(w.Title+" "+w.Process).Contains(t))).ToList();
  return exact.Count>0?exact:all.Where(w=>Fuzzy.Matches(Commands.Normalize(w.Title+" "+w.Process),tokens,out _)).ToList();
 }
 // Combinações com a tecla Windows (área de trabalho, desktops virtuais) só podem ser enviadas como teclado simulado.
 internal static void Chord(params byte[] keys)
 {
  foreach(var k in keys)Native.keybd_event(k,0,Extended(k),UIntPtr.Zero);
  foreach(var k in keys.Reverse())Native.keybd_event(k,0,Extended(k)|2,UIntPtr.Zero);
 }
 private static uint Extended(byte key)=>key is 0x5B or 0x25 or 0x26 or 0x27 or 0x28?1u:0u;
}
