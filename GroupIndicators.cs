using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
namespace BarraInteligente;
internal sealed class GroupIndicators : IDisposable
{
 private readonly HashSet<IntPtr> members=new();
 private readonly Dictionary<string,Window> overlays=new();
 private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(200)};
 internal string? ActiveName {get;private set;}
 internal GroupIndicators(){timer.Tick+=(_,_)=>Refresh();}
 internal void Activate(string name,IEnumerable<IntPtr> windows)
 {
  Deactivate();foreach(var window in windows.Where(Native.IsWindow))members.Add(window);
  if(members.Count==0)return;ActiveName=name;Refresh();if(ActiveName!=null)timer.Start();
 }
 internal void Deactivate()
 {
  timer.Stop();ActiveName=null;members.Clear();foreach(var overlay in overlays.Values)overlay.Close();overlays.Clear();
 }
 private void Refresh()
 {
  members.RemoveWhere(h=>!Native.IsWindow(h));if(members.Count==0){Deactivate();return;}
  var screens=members.Where(h=>Native.IsWindowVisible(h)&&!Native.IsIconic(h)).Select(System.Windows.Forms.Screen.FromHandle).DistinctBy(s=>s.DeviceName).ToDictionary(s=>s.DeviceName);
  foreach(var key in overlays.Keys.Where(k=>!screens.ContainsKey(k)).ToArray()){overlays[key].Close();overlays.Remove(key);}
  foreach(var (key,screen) in screens)
  {
   if(!overlays.TryGetValue(key,out var overlay))
   {
    overlay=new Window{Title="Agrupamento ativo · "+ActiveName,WindowStyle=WindowStyle.None,AllowsTransparency=true,Background=Brushes.Transparent,ShowInTaskbar=false,ShowActivated=false,Topmost=true,ResizeMode=ResizeMode.NoResize,IsHitTestVisible=false,Focusable=false};
    overlay.Content=new Border{BorderThickness=new Thickness(2),BorderBrush=new SolidColorBrush(Color.FromArgb(145,90,160,245)),Background=Brushes.Transparent};
    overlay.SourceInitialized+=(_,_)=>{var hwnd=new WindowInteropHelper(overlay).Handle;Native.SetWindowLong(hwnd,-20,Native.GetWindowLong(hwnd,-20)|0x80000|0x20|0x8000000|0x80);};
    overlays[key]=overlay;overlay.Show();
   }
   var bounds=screen.Bounds;Native.SetWindowPos(new WindowInteropHelper(overlay).Handle,new IntPtr(-1),bounds.Left,bounds.Top,bounds.Width,bounds.Height,0x10);
  }
 }
 public void Dispose()=>Deactivate();
}
