using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;



namespace BarraInteligente;
public sealed class LayoutItem
{
    public string Executable {get;set;}="";
    public string Arguments {get;set;}="";
    public string Title {get;set;}="";
    public int X {get;set;} public int Y {get;set;} public int Width {get;set;} public int Height {get;set;}
    public bool Maximized {get;set;}
}
public sealed class Workspace { public string Name {get;set;}=""; public List<LayoutItem> Apps {get;set;}=new(); }

internal sealed class Store
{
    internal readonly string FilePath;
    internal Settings Data=new();
    internal string? LoadError;
    internal Store(string? filePath=null) { FilePath=filePath??Path.Combine(AppContext.BaseDirectory,"data","settings.json"); try { if(File.Exists(FilePath)) Data=JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath))??new(); } catch(Exception ex) { LoadError="Não foi possível ler as configurações: "+ex.Message; } }
    internal void Save() { Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!); var temp=FilePath+".tmp"; File.WriteAllText(temp,JsonSerializer.Serialize(Data,new JsonSerializerOptions{WriteIndented=true})); File.Move(temp,FilePath,true); }
}
internal sealed class WindowManager : IDisposable
{
    private readonly Store store;
    private readonly Native.WinEvent callback;
    private readonly IntPtr hook;
    private readonly HashSet<IntPtr> seen=new();
    private readonly HashSet<IntPtr> queued=new();
    private readonly Dictionary<string,DateTime> reservations=new(StringComparer.OrdinalIgnoreCase);
    private bool disposed;
    private readonly GroupIndicators indicators=new();
    internal string? ActiveGroupName=>indicators.ActiveName;
    internal void EndGroup()=>indicators.Deactivate();
    internal bool Launching {get;private set;}
    internal WindowManager(Store store)
    {
        this.store=store; foreach(var h in Native.Windows()) seen.Add(h);
        callback=OnEvent; hook=Native.SetWinEventHook(0x8001,0x8002,IntPtr.Zero,callback,0,0,2);
        if(hook==IntPtr.Zero) throw new InvalidOperationException("Não foi possível observar as janelas do Windows.");
    }
    private async void OnEvent(IntPtr hook,uint evt,IntPtr h,int obj,int child,uint thread,uint time)
    {
        if(obj!=0 || child!=0 || h==IntPtr.Zero) return;
        if(evt==0x8001) { seen.Remove(h); queued.Remove(h); return; }
        if(disposed || seen.Contains(h) || !Native.Eligible(h) || !queued.Add(h)) return;
        await Task.Delay(450); queued.Remove(h);
        if(disposed || !Native.Eligible(h) || !seen.Add(h)) return;
        var path=Native.Path(h);
        if(reservations.TryGetValue(path,out var expiry) && expiry>DateTime.UtcNow) return;
        if(!store.Data.CenterWindows || Native.IsIconic(h) || Native.IsZoomed(h)) return;
        Native.GetWindowRect(h,out var r); var area=Native.WorkArea(h);
        var x=area.Left+Math.Max(0,(area.Width-r.Width)/2); var y=area.Top+Math.Max(0,(area.Height-r.Height)/2);
        Native.SetWindowPos(h,IntPtr.Zero,x,y,0,0,0x15);
    }
    internal async Task<string> Launch(Workspace workspace)
    {
        if(Launching) return "Um workspace já está sendo aberto.";
        Launching=true;
        indicators.Deactivate();
        var errors=new List<string>();
        // Reserve every executable BEFORE any Process.Start or await: SHOW events cannot race placement.
        foreach(var item in workspace.Apps) reservations[item.Executable]=DateTime.UtcNow.AddSeconds(35);
        var claimed=new HashSet<IntPtr>();
        var positioned=new HashSet<IntPtr>();
        var available=Native.Windows();
        try
        {
            async Task LaunchOne(LayoutItem item)
            {
                try
                {
                    var prior=available;
                    // Reuse existing app windows without disturbing their siblings.
                    var existing=prior.FirstOrDefault(h=>!claimed.Contains(h) && string.Equals(Native.Path(h),item.Executable,StringComparison.OrdinalIgnoreCase) && Native.Title(h)==item.Title);
                    if(existing==IntPtr.Zero) existing=prior.FirstOrDefault(h=>!claimed.Contains(h) && string.Equals(Native.Path(h),item.Executable,StringComparison.OrdinalIgnoreCase));
                    var h=existing; if(h!=IntPtr.Zero) claimed.Add(h);
                    if(h==IntPtr.Zero)
                    {
                        using var process=Process.Start(new ProcessStartInfo(item.Executable,item.Arguments){UseShellExecute=true});
                        var started=DateTime.UtcNow; var deadline=started.AddSeconds(20);
                        while(DateTime.UtcNow<deadline && h==IntPtr.Zero)
                        {
                            await Task.Delay(150);
                            var candidates=Native.Windows().Where(w=>!prior.Contains(w) && !claimed.Contains(w) && string.Equals(Native.Path(w),item.Executable,StringComparison.OrdinalIgnoreCase)).ToList();
                            h=candidates.FirstOrDefault(w=>Native.Title(w)==item.Title);
                            if(h==IntPtr.Zero && DateTime.UtcNow-started>TimeSpan.FromSeconds(2)) h=candidates.FirstOrDefault();
                        }
                    }
                    if(h==IntPtr.Zero) {errors.Add(item.Title+": janela não encontrada em 20 segundos");return;}
                    claimed.Add(h); seen.Add(h);
                    Native.ShowWindow(h,9);
                    var monitor=System.Windows.Forms.Screen.AllScreens.FirstOrDefault(s=>s.WorkingArea.Contains(item.X+item.Width/2,item.Y+item.Height/2))??System.Windows.Forms.Screen.PrimaryScreen!;
                    var area=monitor.WorkingArea; int width=Math.Clamp(item.Width,120,Math.Max(120,area.Width)),height=Math.Clamp(item.Height,80,Math.Max(80,area.Height));
                    int x=Math.Clamp(item.X,area.Left,Math.Max(area.Left,area.Right-width)),y=Math.Clamp(item.Y,area.Top,Math.Max(area.Top,area.Bottom-height));
                    await Task.Delay(250);
                    if(!Native.SetWindowPos(h,IntPtr.Zero,x,y,width,height,0x14)) errors.Add(item.Title+": o Windows recusou o posicionamento");
                    else positioned.Add(h);
                    if(item.Maximized) Native.ShowWindow(h,3);
                }
                catch(Exception ex) { errors.Add(item.Title+": "+ex.Message); }
            }
            await Task.WhenAll(workspace.Apps.Select(LaunchOne));
        }
        finally { Launching=false; foreach(var item in workspace.Apps) reservations[item.Executable]=DateTime.UtcNow.AddSeconds(2); }
        if(!disposed)indicators.Activate(workspace.Name,positioned);
        return errors.Count==0 ? "Workspace aberto: "+workspace.Name : string.Join("\n",errors);
    }
    public void Dispose() { disposed=true; indicators.Dispose(); Native.UnhookWinEvent(hook); }
}










