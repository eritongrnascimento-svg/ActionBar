using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
namespace BarraInteligente;
internal sealed record ProcessGroup(string Name,int Count,long Memory,string Path);
internal sealed record ScheduledPower(bool Restart,int Seconds);
// Recursos do aparelho, usados para esconder ações que não fazem sentido neste PC.
internal static class Capabilities
{
 private static readonly Lazy<bool> wifi=new(()=>{try{return NetworkInterface.GetAllNetworkInterfaces().Any(n=>n.NetworkInterfaceType==NetworkInterfaceType.Wireless80211);}catch{return true;}});
 private static readonly Lazy<bool> bluetooth=new(()=>
 {
  try{var p=new BluetoothFindRadioParams{Size=Marshal.SizeOf<BluetoothFindRadioParams>()};var find=BluetoothFindFirstRadio(ref p,out var radio);if(find==IntPtr.Zero)return false;CloseHandle(radio);BluetoothFindRadioClose(find);return true;}
  catch{return true;}
 });
 private static readonly Lazy<bool> battery=new(()=>{try{return !System.Windows.Forms.SystemInformation.PowerStatus.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.NoSystemBattery);}catch{return true;}});
 internal static bool Wifi=>wifi.Value;internal static bool Bluetooth=>bluetooth.Value;internal static bool Battery=>battery.Value;internal static bool Wireless=>Wifi || Bluetooth;
 internal static bool MultipleMonitors=>System.Windows.Forms.Screen.AllScreens.Length>1;
 [StructLayout(LayoutKind.Sequential)] private struct BluetoothFindRadioParams{public int Size;}
 [DllImport("bluetoothapis.dll")] private static extern IntPtr BluetoothFindFirstRadio(ref BluetoothFindRadioParams p,out IntPtr radio);
 [DllImport("bluetoothapis.dll")] private static extern bool BluetoothFindRadioClose(IntPtr find);
 [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
}
internal static class ProcessTools
{
 // Processos do sistema que nunca devem ser oferecidos para encerramento.
 private static readonly HashSet<string> Protected=new(StringComparer.OrdinalIgnoreCase){"system","idle","registry","smss","csrss","wininit","winlogon","services","lsass","lsaiso","svchost","dwm","fontdrvhost","memory compression","secure system","sihost","ctfmon","audiodg","spoolsv","explorer","searchhost","startmenuexperiencehost","shellexperiencehost","textinputhost","runtimebroker","taskhostw","conhost","dllhost","wudfhost","msmpeng","nissrv","securityhealthservice","smartscreen","applicationframehost","lockapp","userinit","barrainteligente","actionbar"};
 internal static bool IsProtected(string name)=>Protected.Contains(name);
 internal static List<ProcessGroup> Groups()
 {
  var groups=new List<ProcessGroup>();var own=Environment.ProcessId;
  foreach(var g in Process.GetProcesses().Where(p=>p.Id!=own && p.Id>4).GroupBy(p=>p.ProcessName,StringComparer.OrdinalIgnoreCase))
  {
   long memory=0;foreach(var p in g){try{memory+=p.WorkingSet64;}catch{}}
   if(!IsProtected(g.Key))groups.Add(new ProcessGroup(g.Key,g.Count(),memory,""));
   foreach(var p in g)p.Dispose();
  }
  return groups;
 }
 internal static string PathOf(string name)
 {
  foreach(var p in Process.GetProcessesByName(name)){using(p){try{if(p.MainModule?.FileName is string f)return f;}catch{}}}
  return "";
 }
 internal static List<ProcessGroup> Find(string normalizedName)
 {
  var groups=Groups();var q=normalizedName.Replace(".exe","").Trim();if(q.Length==0)return new();
  var exact=groups.Where(g=>Commands.Normalize(g.Name).Contains(q)).ToList();
  var found=exact.Count>0?exact:groups.Where(g=>Fuzzy.Matches(Commands.Normalize(g.Name),new[]{q},out _)).ToList();
  return found.OrderBy(g=>Commands.Normalize(g.Name)==q?0:1).ThenByDescending(g=>g.Memory).Take(6).Select(g=>g with{Path=PathOf(g.Name)}).ToList();
 }
 internal static List<ProcessGroup> Top(int count)=>Groups().OrderByDescending(g=>g.Memory).Take(count).Select(g=>g with{Path=PathOf(g.Name)}).ToList();
 // Pede para as janelas fecharem normalmente; o que continuar aberto após 3 s é finalizado.
 internal static async Task<string> End(string name)
 {
  if(IsProtected(name))throw new InvalidOperationException(name+" é um processo do sistema e não pode ser encerrado pela barra.");
  var processes=Process.GetProcessesByName(name).Where(p=>p.Id!=Environment.ProcessId).ToArray();
  if(processes.Length==0)return name+" já não está em execução.";
  try
  {
   foreach(var p in processes){try{if(p.MainWindowHandle!=IntPtr.Zero)p.CloseMainWindow();}catch{}}
   var until=DateTime.UtcNow.AddSeconds(3);while(DateTime.UtcNow<until && processes.Any(Alive))await Task.Delay(150);
   var denied=0;foreach(var p in processes.Where(Alive)){try{p.Kill();}catch{denied++;}}
   await Task.Delay(200);
   return denied==0?$"{name} encerrado ({processes.Length} processo{(processes.Length>1?"s":"")}).":$"{name}: {denied} processo(s) exigem administrador e continuaram abertos.";
  }
  finally{foreach(var p in processes)p.Dispose();}
 }
 private static bool Alive(Process p){try{return !p.HasExited;}catch{return false;}}
 internal static async Task RestartExplorer()
 {
  foreach(var p in Process.GetProcessesByName("explorer")){using(p){try{p.Kill();}catch{}}}
  await Task.Delay(1500);
  // O Windows normalmente reinicia o shell sozinho; só iniciamos se ele não voltou.
  if(Process.GetProcessesByName("explorer").Length==0)Process.Start(new ProcessStartInfo("explorer.exe"){UseShellExecute=true});
 }
 internal static string Size(long bytes)=>bytes>=1L<<30?$"{bytes/(double)(1L<<30):0.0} GB":$"{bytes/(1L<<20):N0} MB";
}
internal static class NetworkTools
{
 private static bool Virtual(NetworkInterface n)=>new[]{"virtual","hyper-v","vmware","vbox","wsl","loopback","tap-","tunnel","bluetooth"}.Any(k=>(n.Description+" "+n.Name).Contains(k,StringComparison.OrdinalIgnoreCase));
 // IPv4 dos adaptadores físicos conectados (ignora Hyper-V/WSL/VirtualBox).
 internal static List<(string Adapter,string Address)> LocalAddresses()=>NetworkInterface.GetAllNetworkInterfaces()
  .Where(n=>n.OperationalStatus==OperationalStatus.Up && n.NetworkInterfaceType!=NetworkInterfaceType.Loopback && !Virtual(n))
  .SelectMany(n=>n.GetIPProperties().UnicastAddresses.Where(a=>a.Address.AddressFamily==AddressFamily.InterNetwork).Select(a=>(n.Name,a.Address.ToString()))).ToList();
 // Consulta feita somente quando o usuário executa a ação.
 internal static async Task<string> PublicAddress()
 {
  using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(6)};
  var text=(await http.GetStringAsync("https://api.ipify.org")).Trim();
  if(!System.Net.IPAddress.TryParse(text,out _))throw new InvalidOperationException("Resposta inválida do serviço de IP público.");
  return text;
 }
 [DllImport("dnsapi.dll")] private static extern bool DnsFlushResolverCache();
 internal static void FlushDns(){if(!DnsFlushResolverCache())throw new InvalidOperationException("O Windows não limpou o cache DNS.");}
 internal static bool ValidHost(string host)=>host.Length<=253 && Regex.IsMatch(host,@"^[a-z0-9]([a-z0-9\-.:]*[a-z0-9])?$",RegexOptions.IgnoreCase);
 internal static async Task<string> Ping(string host)
 {
  using var ping=new Ping();var times=new List<long>();var lost=0;string? address=null;
  for(var i=0;i<4;i++)
  {
   try{var reply=await ping.SendPingAsync(host,1500);if(reply.Status==IPStatus.Success){times.Add(reply.RoundtripTime);address??=reply.Address.ToString();}else lost++;}
   catch(PingException){throw new InvalidOperationException("Não foi possível resolver "+host+".");}
   if(i<3)await Task.Delay(250);
  }
  return times.Count==0?$"{host}: sem resposta (4 de 4 perdidos).":$"{host} ({address}): média {times.Average():0} ms · mín {times.Min()} · máx {times.Max()} · perda {lost*25}%";
 }
}
internal static class PowerSchedule
{
 // "desligar em 30 min", "reiniciar daqui a 2 horas", "desligar às 23:30", "desligar 23h".
 internal static ScheduledPower? Parse(string normalized,DateTime now)
 {
  var m=Regex.Match(normalized,@"^(desligar|reiniciar)(?: o (?:computador|pc))? (?:em|daqui a|daqui|dentro de) (\d{1,4}) ?(s|seg|segundos?|m|min|mins|minutos?|h|hr|hrs|horas?)$");
  if(m.Success)
  {
   var n=int.Parse(m.Groups[2].Value);var unit=m.Groups[3].Value;
   var seconds=unit.StartsWith("s")?n:unit.StartsWith("h")?n*3600:n*60;
   return seconds is >0 and <=7*86400?new ScheduledPower(m.Groups[1].Value=="reiniciar",seconds):null;
  }
  m=Regex.Match(normalized,@"^(desligar|reiniciar)(?: o (?:computador|pc))? (?:as|a|para as) (\d{1,2})(?:(?::|h)(\d{2})?)?h?$");
  if(m.Success)
  {
   var hour=int.Parse(m.Groups[2].Value);var minute=m.Groups[3].Success?int.Parse(m.Groups[3].Value):0;if(hour>23 || minute>59)return null;
   var at=now.Date.AddHours(hour).AddMinutes(minute);if(at<=now)at=at.AddDays(1);
   return new ScheduledPower(m.Groups[1].Value=="reiniciar",(int)Math.Ceiling((at-now).TotalSeconds));
  }
  return null;
 }
 internal static string Describe(int seconds)=>seconds<60?$"{seconds} s":seconds<3600?$"{seconds/60} min":seconds%3600==0?$"{seconds/3600} h":$"{seconds/3600} h {seconds%3600/60} min";
 internal static string Schedule(bool restart,int seconds,DateTime now)
 {
  using var p=Process.Start(new ProcessStartInfo("shutdown.exe",(restart?"/r":"/s")+" /t "+seconds){UseShellExecute=false,CreateNoWindow=true});
  if(p==null || !p.WaitForExit(4000))throw new InvalidOperationException("O Windows não respondeu ao agendamento.");
  if(p.ExitCode==1190)throw new InvalidOperationException("Já existe um desligamento agendado. Use “cancelar desligamento” primeiro.");
  if(p.ExitCode!=0)throw new InvalidOperationException("O Windows recusou o agendamento (código "+p.ExitCode+").");
  return $"{(restart?"Reinício":"Desligamento")} agendado para {now.AddSeconds(seconds):HH:mm}. Digite “cancelar desligamento” para desfazer.";
 }
 internal static string Cancel()
 {
  using var p=Process.Start(new ProcessStartInfo("shutdown.exe","/a"){UseShellExecute=false,CreateNoWindow=true});
  if(p==null || !p.WaitForExit(4000))throw new InvalidOperationException("O Windows não respondeu.");
  return p.ExitCode==0?"Desligamento agendado cancelado.":p.ExitCode==1116?"Não havia desligamento agendado.":"O Windows recusou o cancelamento (código "+p.ExitCode+").";
 }
}
