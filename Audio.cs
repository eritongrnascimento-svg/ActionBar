using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
namespace BarraInteligente;
internal sealed record AudioDevice(string Id,string Name,bool Output,bool IsDefault);
// Core Audio (MMDevice) para listar dispositivos, volume e mudo do microfone.
// Trocar o dispositivo padrão usa IPolicyConfig: interface não documentada, mas estável desde o Windows 7
// e usada pelo próprio painel de Som. Se um dia falhar, a ação devolve erro e a página de Som continua disponível.
internal static class Audio
{
 private const int Render=0,Capture=1,ActiveState=1;
 internal static List<AudioDevice> Devices()
 {
  var list=new List<AudioDevice>();var enumerator=(IMMDeviceEnumerator)new MMDeviceEnumerator();
  try
  {
   foreach(var flow in new[]{Render,Capture})
   {
    var defaultId=DefaultId(enumerator,flow);
    if(enumerator.EnumAudioEndpoints(flow,ActiveState,out var collection)!=0)continue;
    try
    {
     collection.GetCount(out var count);
     for(uint i=0;i<count;i++)
     {
      if(collection.Item(i,out var device)!=0)continue;
      try{device.GetId(out var id);list.Add(new AudioDevice(id,FriendlyName(device)??id,flow==Render,id==defaultId));}
      finally{Marshal.ReleaseComObject(device);}
     }
    }
    finally{Marshal.ReleaseComObject(collection);}
   }
  }
  finally{Marshal.ReleaseComObject(enumerator);}
  return list;
 }
 private static string? DefaultId(IMMDeviceEnumerator enumerator,int flow)
 {
  if(enumerator.GetDefaultAudioEndpoint(flow,0,out var device)!=0)return null;
  try{device.GetId(out var id);return id;}finally{Marshal.ReleaseComObject(device);}
 }
 private static string? FriendlyName(IMMDevice device)
 {
  if(device.OpenPropertyStore(0,out var store)!=0)return null;
  try
  {
   var key=new PropertyKey{Format=new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),Id=14};
   if(store.GetValue(ref key,out var value)!=0)return null;
   try{return value.Type==31?Marshal.PtrToStringUni(value.Pointer):null;}finally{PropVariantClear(ref value);}
  }
  finally{Marshal.ReleaseComObject(store);}
 }
 // Define o dispositivo para as três funções (console, multimídia e comunicações), como o painel de Som faz.
 internal static void SetDefault(string id)
 {
  var policy=(IPolicyConfig)new PolicyConfigClient();
  try{for(var role=0;role<3;role++)Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(id,role));}
  finally{Marshal.ReleaseComObject(policy);}
 }
 private static T WithEndpoint<T>(int flow,Func<IAudioEndpointVolume,T> use)
 {
  var enumerator=(IMMDeviceEnumerator)new MMDeviceEnumerator();IMMDevice? device=null;object? endpoint=null;
  try
  {
   var hr=enumerator.GetDefaultAudioEndpoint(flow,0,out device);
   if(hr!=0)throw new InvalidOperationException(flow==Render?"Nenhum dispositivo de saída de áudio ativo.":"Nenhum microfone ativo.");
   var iid=typeof(IAudioEndpointVolume).GUID;Marshal.ThrowExceptionForHR(device.Activate(ref iid,23,IntPtr.Zero,out endpoint));
   return use((IAudioEndpointVolume)endpoint);
  }
  finally{if(endpoint!=null)Marshal.ReleaseComObject(endpoint);if(device!=null)Marshal.ReleaseComObject(device);Marshal.ReleaseComObject(enumerator);}
 }
 internal static void SetVolume(int percent)=>WithEndpoint(Render,v=>{var g=Guid.Empty;Marshal.ThrowExceptionForHR(v.SetMasterVolumeLevelScalar(Math.Clamp(percent,0,100)/100f,ref g));return 0;});
 internal static int? Volume(){try{return WithEndpoint(Render,v=>v.GetMasterVolumeLevelScalar(out var level)==0?(int?)Math.Round(level*100):null);}catch{return null;}}
 internal static int? MicrophoneLevel(){try{return WithEndpoint(Capture,v=>v.GetMasterVolumeLevelScalar(out var level)==0?(int?)Math.Round(level*100):null);}catch{return null;}}
 internal static void SetMicrophoneLevel(int percent)=>WithEndpoint(Capture,v=>{var g=Guid.Empty;Marshal.ThrowExceptionForHR(v.SetMasterVolumeLevelScalar(Math.Clamp(percent,0,100)/100f,ref g));return 0;});
 internal static bool? MicrophoneMuted(){try{return WithEndpoint(Capture,v=>v.GetMute(out var muted)==0?(bool?)muted:null);}catch{return null;}}
 // Alterna o mudo do microfone padrão e devolve o novo estado.
 internal static bool ToggleMicrophone()=>WithEndpoint(Capture,v=>{Marshal.ThrowExceptionForHR(v.GetMute(out var muted));var g=Guid.Empty;Marshal.ThrowExceptionForHR(v.SetMute(!muted,ref g));return !muted;});

 [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
 [StructLayout(LayoutKind.Sequential)] private struct PropertyKey{public Guid Format;public int Id;}
 [StructLayout(LayoutKind.Sequential)] private struct PropVariant{public ushort Type;public ushort R1,R2,R3;public IntPtr Pointer;public IntPtr Extra;}
 [ComImport,Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class MMDeviceEnumerator{}
 [ComImport,Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IMMDeviceEnumerator
 {
  [PreserveSig] int EnumAudioEndpoints(int flow,int stateMask,out IMMDeviceCollection devices);
  [PreserveSig] int GetDefaultAudioEndpoint(int flow,int role,out IMMDevice device);
 }
 [ComImport,Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IMMDeviceCollection
 {
  [PreserveSig] int GetCount(out uint count);
  [PreserveSig] int Item(uint index,out IMMDevice device);
 }
 [ComImport,Guid("D666063F-1587-4E43-81F1-B948E807363F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IMMDevice
 {
  [PreserveSig] int Activate(ref Guid iid,int context,IntPtr parameters,[MarshalAs(UnmanagedType.IUnknown)] out object endpoint);
  [PreserveSig] int OpenPropertyStore(int access,out IPropertyStore store);
  [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
 }
 [ComImport,Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IPropertyStore
 {
  [PreserveSig] int GetCount(out uint count);
  [PreserveSig] int GetAt(uint index,out PropertyKey key);
  [PreserveSig] int GetValue(ref PropertyKey key,out PropVariant value);
 }
 [ComImport,Guid("5CDF2C82-841E-4546-9722-0CF74078229A"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IAudioEndpointVolume
 {
  [PreserveSig] int RegisterControlChangeNotify(IntPtr p);
  [PreserveSig] int UnregisterControlChangeNotify(IntPtr p);
  [PreserveSig] int GetChannelCount(out uint count);
  [PreserveSig] int SetMasterVolumeLevel(float db,ref Guid context);
  [PreserveSig] int SetMasterVolumeLevelScalar(float level,ref Guid context);
  [PreserveSig] int GetMasterVolumeLevel(out float db);
  [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
  [PreserveSig] int SetChannelVolumeLevel(uint channel,float db,ref Guid context);
  [PreserveSig] int SetChannelVolumeLevelScalar(uint channel,float level,ref Guid context);
  [PreserveSig] int GetChannelVolumeLevel(uint channel,out float db);
  [PreserveSig] int GetChannelVolumeLevelScalar(uint channel,out float level);
  [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute,ref Guid context);
  [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
 }
 [ComImport,Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")] private class PolicyConfigClient{}
 [ComImport,Guid("f8679f50-850a-41cf-9c72-430f290290c8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IPolicyConfig
 {
  [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string id,IntPtr format);
  [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id,int useDefault,IntPtr format);
  [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id);
  [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id,IntPtr endpointFormat,IntPtr mixFormat);
  [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id,int useDefault,IntPtr defaultPeriod,IntPtr minimumPeriod);
  [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id,IntPtr period);
  [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id,IntPtr mode);
  [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id,IntPtr mode);
  [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id,IntPtr key,IntPtr value);
  [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id,IntPtr key,IntPtr value);
  [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id,int role);
  [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id,int visible);
 }
}
