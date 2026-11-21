using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;
namespace BarraInteligente;
internal sealed record DiskInfo(string Name,string Root,string Kind,long Total,long Free,bool Ready)
{
 internal long Used=>Math.Max(0,Total-Free);internal double UsedPercent=>Total>0?(double)Used/Total*100:0;
 internal static IReadOnlyList<DiskInfo> Read()=>DriveInfo.GetDrives().Select(d=>{try{return d.IsReady?new DiskInfo(string.IsNullOrWhiteSpace(d.VolumeLabel)?d.Name:d.VolumeLabel+" ("+d.Name.TrimEnd('\\')+")",d.Name,d.DriveType.ToString(),d.TotalSize,d.TotalFreeSpace,true):new DiskInfo(d.Name,d.Name,d.DriveType.ToString(),0,0,false);}catch{return new DiskInfo(d.Name,d.Name,d.DriveType.ToString(),0,0,false);}}).ToArray();
 internal static string Bytes(long value){var units=new[]{"B","KB","MB","GB","TB"};double number=value;var unit=0;while(number>=1024 && unit<units.Length-1){number/=1024;unit++;}return $"{number:0.#} {units[unit]}";}
}
internal sealed record ResourceSnapshot(double? Cpu,ulong TotalMemory,ulong FreeMemory,double? Gpu,string CpuName,string GpuName)
{internal double MemoryPercent=>TotalMemory>0?(TotalMemory-FreeMemory)*100d/TotalMemory:0;}
internal sealed class ResourceMonitor : IDisposable
{
 private ulong lastIdle,lastTotal;private IntPtr query,counter;private string cpuName="Processador",gpuName="GPU";
 internal ResourceMonitor(){using var cpu=Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");cpuName=(cpu?.GetValue("ProcessorNameString") as string)?.Trim()??"Processador";GetSystemTimes(out lastIdle,out var kernel,out var user);lastTotal=kernel+user;if(PdhOpenQuery(null,UIntPtr.Zero,out query)==0){if(PdhAddEnglishCounter(query,@"\GPU Engine(*)\Utilization Percentage",UIntPtr.Zero,out counter)!=0)counter=IntPtr.Zero;PdhCollectQueryData(query);}}
 internal void LoadGpuName()
 {
  object? locator=null,service=null,items=null;try{locator=Activator.CreateInstance(Type.GetTypeFromProgID("WbemScripting.SWbemLocator")!);dynamic l=locator!;service=l.ConnectServer(".",@"root\cimv2");dynamic s=service;items=s.ExecQuery("SELECT Name FROM Win32_VideoController");dynamic list=items;var names=new List<string>();foreach(dynamic item in list){try{names.Add((string)item.Name);}finally{Marshal.ReleaseComObject(item);}}gpuName=string.Join(" / ",names.Distinct());if(gpuName.Length==0)gpuName="GPU";}catch{}finally{if(items!=null)Marshal.ReleaseComObject(items);if(service!=null)Marshal.ReleaseComObject(service);if(locator!=null)Marshal.ReleaseComObject(locator);}
 }
 internal ResourceSnapshot Read()
 {
  double? cpu=null;if(GetSystemTimes(out var idle,out var kernel,out var user)){var total=kernel+user;var diff=total-lastTotal;if(diff>0)cpu=Math.Clamp((1d-(double)(idle-lastIdle)/diff)*100,0,100);lastIdle=idle;lastTotal=total;}
  var mem=new MemoryStatus{Length=(uint)Marshal.SizeOf<MemoryStatus>()};GlobalMemoryStatusEx(ref mem);double? gpu=null;
  if(query!=IntPtr.Zero && counter!=IntPtr.Zero && PdhCollectQueryData(query)==0){uint bytes=0,count=0;PdhGetFormattedCounterArray(counter,0x200,ref bytes,out count,IntPtr.Zero);if(bytes>0 && bytes<32*1024*1024){var buffer=Marshal.AllocHGlobal((int)bytes);try{if(PdhGetFormattedCounterArray(counter,0x200,ref bytes,out count,buffer)==0){var engines=new Dictionary<string,double>();var size=Marshal.SizeOf<CounterItem>();for(var i=0;i<count;i++){var item=Marshal.PtrToStructure<CounterItem>(buffer+i*size);if(item.Value.Status>1 || !double.IsFinite(item.Value.Value))continue;var name=Marshal.PtrToStringUni(item.Name)??"";var key=System.Text.RegularExpressions.Regex.Replace(name,@"^pid_\d+_","");engines[key]=engines.GetValueOrDefault(key)+item.Value.Value;}if(engines.Count>0)gpu=Math.Clamp(engines.Values.Max(),0,100);}}finally{Marshal.FreeHGlobal(buffer);}}}
  return new(cpu,mem.TotalPhysical,mem.AvailablePhysical,gpu,cpuName,gpuName);
 }
 public void Dispose(){if(query!=IntPtr.Zero){PdhCloseQuery(query);query=IntPtr.Zero;counter=IntPtr.Zero;}}
 [StructLayout(LayoutKind.Sequential)]private struct MemoryStatus{public uint Length,Load;public ulong TotalPhysical,AvailablePhysical,TotalPageFile,AvailablePageFile,TotalVirtual,AvailableVirtual,AvailableExtended;}
 [StructLayout(LayoutKind.Explicit,Size=16)]private struct CounterValue{[FieldOffset(0)]public uint Status;[FieldOffset(8)]public double Value;}
 [StructLayout(LayoutKind.Sequential)]private struct CounterItem{public IntPtr Name;public CounterValue Value;}
 [DllImport("kernel32.dll")]private static extern bool GetSystemTimes(out ulong idle,out ulong kernel,out ulong user);
 [DllImport("kernel32.dll",SetLastError=true)]private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
 [DllImport("pdh.dll",CharSet=CharSet.Unicode,EntryPoint="PdhOpenQueryW")]private static extern uint PdhOpenQuery(string? source,UIntPtr data,out IntPtr query);
 [DllImport("pdh.dll",CharSet=CharSet.Unicode,EntryPoint="PdhAddEnglishCounterW")]private static extern uint PdhAddEnglishCounter(IntPtr query,string path,UIntPtr data,out IntPtr counter);
 [DllImport("pdh.dll")]private static extern uint PdhCollectQueryData(IntPtr query);
 [DllImport("pdh.dll",CharSet=CharSet.Unicode,EntryPoint="PdhGetFormattedCounterArrayW")]private static extern uint PdhGetFormattedCounterArray(IntPtr counter,uint format,ref uint size,out uint count,IntPtr buffer);
 [DllImport("pdh.dll")]private static extern uint PdhCloseQuery(IntPtr query);
}
