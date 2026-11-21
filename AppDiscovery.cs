using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;
namespace BarraInteligente;
internal static class AppDiscovery
{
 internal static bool IsSystemTool(string name,string target)
 {
  var n=Commands.Normalize(name);var file=Path.GetFileNameWithoutExtension(target).ToLowerInvariant();
  if(new[]{"configuracoes","settings","painel de controle","control panel","gerenciador de tarefas","task manager","gerenciador de dispositivos","services","servicos","administrative tools","ferramentas administrativas","windows tools","ferramentas do windows"}.Any(t=>n==t))return true;
  if(target.Contains("ImmersiveControlPanel",StringComparison.OrdinalIgnoreCase))return true;
  var windows=Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\')+"\\";
  if(target.StartsWith(windows,StringComparison.OrdinalIgnoreCase) && !new[]{"notepad","mspaint","calc","snippingtool","wordpad","write"}.Contains(file))return true;
  return false;
 }
 internal static bool IsNoise(string name,string target)
 {
  var text=Commands.Normalize(name+" "+Path.GetFileNameWithoutExtension(target));
  return new[]{"uninstall","desinstal","unins000","unins001","crashpad","crashreport","updater","update service","maintenance service","notification helper","readme","release notes","documentation","documentacao","license","licenca","website","pagina inicial","help file","redistributable","webview2 runtime","update assistant","runtime package"}.Any(text.Contains);
 }
 internal static bool IsGuiExecutable(string path)
 {
  try{using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);using var reader=new BinaryReader(stream);if(reader.ReadUInt16()!=0x5a4d)return false;stream.Position=0x3c;var pe=reader.ReadInt32();if(pe<0 || pe>stream.Length-94)return false;stream.Position=pe;if(reader.ReadUInt32()!=0x4550)return false;stream.Position=pe+24+68;return reader.ReadUInt16()==2;}catch{return false;}
 }
 internal static List<AppEntry> Discover()
 {
  var list=new List<AppEntry>();var identities=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  void Add(string name,string target,string executable="",string source="Aplicativo instalado")
  {
   if(string.IsNullOrWhiteSpace(name)||IsNoise(name,target))return;
   if(target.EndsWith(".lnk",StringComparison.OrdinalIgnoreCase))try{executable=Catalog.ResolveProgram(target).Path;}catch{return;}
   if(string.IsNullOrEmpty(executable) && File.Exists(target))executable=target;
   if(IsSystemTool(name,executable.Length>0?executable:target))return;
   if(executable.Length>0 && (!executable.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)||!IsGuiExecutable(executable)))return;
   if(executable.Length==0 && !target.StartsWith("shell:AppsFolder\\",StringComparison.OrdinalIgnoreCase))return;
   var identity=executable.Length>0?executable:target;
   if(identities.Add(identity))list.Add(new AppEntry(name,target,executable,source));
  }
  foreach(var shortcut in Commands.IndexApps())Add(shortcut.Name,shortcut.Path,source:"Menu Iniciar");
  foreach(var hive in new[]{Registry.CurrentUser,Registry.LocalMachine})
  {
   foreach(var prefix in new[]{@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths",@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths"})
    try{using var root=hive.OpenSubKey(prefix);if(root==null)continue;foreach(var keyName in root.GetSubKeyNames()){using var key=root.OpenSubKey(keyName);var path=(key?.GetValue("") as string)?.Trim('"');if(path!=null && File.Exists(path))Add(Path.GetFileNameWithoutExtension(path),path,source:"Registro de aplicativos");}}catch{}
   foreach(var prefix in new[]{@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"})
    try{using var root=hive.OpenSubKey(prefix);if(root==null)continue;foreach(var keyName in root.GetSubKeyNames()){using var key=root.OpenSubKey(keyName);var name=key?.GetValue("DisplayName") as string;if(string.IsNullOrWhiteSpace(name)||key?.GetValue("SystemComponent") is int hidden && hidden==1)continue;var icon=key?.GetValue("DisplayIcon") as string;var path=ExtractExecutable(icon??"");if(path!=null && File.Exists(path))Add(name,path,source:"Aplicativo instalado");var install=key?.GetValue("InstallLocation") as string;if(!string.IsNullOrWhiteSpace(install)&&Directory.Exists(install)){var candidates=Directory.EnumerateFiles(install,"*.exe",new EnumerationOptions{IgnoreInaccessible=true}).Where(p=>!IsNoise(name,p)&&IsGuiExecutable(p)).Take(12).ToArray();var best=candidates.FirstOrDefault(p=>Commands.Normalize(name).Replace(" ","").Contains(Commands.Normalize(Path.GetFileNameWithoutExtension(p)).Replace(" ","")))??(candidates.Length==1?candidates[0]:null);if(best!=null)Add(name,best,source:"Pasta de aplicativo instalado");}}}catch{}
  }
  object? shell=null,folder=null;
  try{shell=Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);dynamic sh=shell!;folder=sh.NameSpace("shell:AppsFolder");dynamic f=folder!;foreach(dynamic item in f.Items()){try{string name=item.Name;string path=item.Path;if(!string.IsNullOrWhiteSpace(path))Add(name,File.Exists(path)?path:@"shell:AppsFolder\"+path,source:"Windows / Microsoft Store");}finally{Marshal.ReleaseComObject(item);}}}catch{}finally{if(folder!=null)Marshal.ReleaseComObject(folder);if(shell!=null)Marshal.ReleaseComObject(shell);}
  return list.GroupBy(a=>CanonicalName(a.Name)).Select(g=>g.OrderByDescending(a=>a.Name.Length).ThenByDescending(a=>a.Target.StartsWith("shell:")).First()).OrderBy(a=>a.Name).ToList();
 }
 internal static string CanonicalName(string name)=>Commands.Normalize(name) switch{"chrome" or "google chrome"=>"google chrome","msedge" or "edge" or "microsoft edge"=>"microsoft edge","firefox" or "mozilla firefox"=>"mozilla firefox","brave" or "brave browser"=>"brave",var other=>other};
 internal static string? ExtractExecutable(string command){var m=Regex.Match(Environment.ExpandEnvironmentVariables(command.Trim()),"^\\\"?(.+?\\.exe)(?:\\\"|,|\\s|$)",RegexOptions.IgnoreCase);return m.Success?m.Groups[1].Value:null;}
 internal static List<AppEntry> Browsers(IEnumerable<AppEntry> apps)
 {
  var list=new List<AppEntry>();
  foreach(var hive in new[]{Registry.CurrentUser,Registry.LocalMachine})foreach(var prefix in new[]{@"SOFTWARE\Clients\StartMenuInternet",@"SOFTWARE\WOW6432Node\Clients\StartMenuInternet"})
   try{using var root=hive.OpenSubKey(prefix);if(root==null)continue;foreach(var browser in root.GetSubKeyNames()){using var key=root.OpenSubKey(browser);using var command=key?.OpenSubKey(@"shell\open\command");var path=ExtractExecutable(command?.GetValue("") as string??"");if(path!=null && File.Exists(path))list.Add(new(key?.GetValue("") as string??browser,path,path,"Navegador"));}}catch{}
  list.AddRange(apps.Where(a=>new[]{"chrome","firefox","msedge","brave","opera","vivaldi","zen","arc"}.Contains(Path.GetFileNameWithoutExtension(a.Executable).ToLowerInvariant())));
  return list.Where(a=>!Path.GetFileName(a.Executable).Equals("iexplore.exe",StringComparison.OrdinalIgnoreCase)).DistinctBy(a=>CanonicalName(a.Name)).OrderBy(a=>a.Name).ToList();
 }
}

