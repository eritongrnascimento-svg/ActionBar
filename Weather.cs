using System;
using System.Globalization;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
namespace BarraInteligente;
internal sealed record WeatherData(string Place,string Origin,double Temperature,double FeelsLike,int Humidity,double Wind,int Code,DateTimeOffset Updated);
internal sealed class WeatherService
{
 private readonly HttpClient http;private readonly Func<(double Latitude,double Longitude)?> locate;private WeatherData? cached;private readonly SemaphoreSlim gate=new(1);private DateTimeOffset retryAfter;
 internal WeatherService(HttpClient? client=null,Func<(double Latitude,double Longitude)?>? location=null){http=client??new HttpClient{Timeout=TimeSpan.FromSeconds(10)};locate=location??WindowsLocation.TryGet;}
 internal async Task<WeatherData> Get(CancellationToken token,bool force=false)
 {
  await gate.WaitAsync(token);try
  {
   if(!force && cached!=null && DateTimeOffset.UtcNow-cached.Updated<TimeSpan.FromMinutes(15))return cached;if(!force && DateTimeOffset.UtcNow<retryAfter)throw new InvalidOperationException("Clima temporariamente indisponível. Tente atualizar em um minuto.");
   try
   {
    var location=await Task.Run(locate,token);string place="Localização do Windows",origin="Windows";double lat,lon;
    if(location.HasValue){(lat,lon)=location.Value;}
    else{using var response=await http.GetAsync("https://ipwho.is/?fields=success,latitude,longitude,city,country",token);response.EnsureSuccessStatusCode();using var data=JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));var root=data.RootElement;if(!root.TryGetProperty("success",out var success)||!success.GetBoolean())throw new InvalidOperationException("Localização automática indisponível");lat=root.GetProperty("latitude").GetDouble();lon=root.GetProperty("longitude").GetDouble();place=root.GetProperty("city").GetString()??"Sua região";origin="Localização aproximada pela conexão · IPWho.is";}
    var url="https://api.open-meteo.com/v1/forecast?latitude="+Math.Round(lat,2).ToString(CultureInfo.InvariantCulture)+"&longitude="+Math.Round(lon,2).ToString(CultureInfo.InvariantCulture)+"&current=temperature_2m,relative_humidity_2m,apparent_temperature,weather_code,wind_speed_10m&timezone=auto";
    using var weather=await http.GetAsync(url,token);weather.EnsureSuccessStatusCode();using var json=JsonDocument.Parse(await weather.Content.ReadAsStringAsync(token));var c=json.RootElement.GetProperty("current");cached=new(place,origin,c.GetProperty("temperature_2m").GetDouble(),c.GetProperty("apparent_temperature").GetDouble(),c.GetProperty("relative_humidity_2m").GetInt32(),c.GetProperty("wind_speed_10m").GetDouble(),c.GetProperty("weather_code").GetInt32(),DateTimeOffset.UtcNow);retryAfter=default;return cached;
   }catch(OperationCanceledException)when(token.IsCancellationRequested){throw;}catch{retryAfter=DateTimeOffset.UtcNow.AddMinutes(1);throw;}
  }finally{gate.Release();}
 }
 internal static string Description(int code)=>code switch{0=>"Céu limpo",1=>"Predominantemente limpo",2=>"Parcialmente nublado",3=>"Nublado",45 or 48=>"Neblina",>=51 and <=57=>"Garoa",>=61 and <=67=>"Chuva",>=71 and <=77=>"Neve",>=80 and <=82=>"Pancadas de chuva",85 or 86=>"Pancadas de neve",>=95=>"Trovoadas",_=>"Condições atuais"};
}
internal static class WindowsLocation
{
 internal static (double Latitude,double Longitude)? TryGet()
 {
  object? location=null,report=null;
  try{location=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("E5B8E079-EE6D-4E33-A438-C87F2E959254"))!);var iid=typeof(ILatLongReport).GUID;if(((ILocation)location!).GetReport(ref iid,out report)!=0)return null;var ll=(ILatLongReport)report;if(ll.GetLatitude(out var lat)!=0||ll.GetLongitude(out var lon)!=0||Math.Abs(lat)>90||Math.Abs(lon)>180)return null;if(ll.GetTimestamp(out var time)==0){var stamp=new DateTime(time.Year,time.Month,time.Day,time.Hour,time.Minute,time.Second,DateTimeKind.Utc);if(DateTime.UtcNow-stamp>TimeSpan.FromHours(6))return null;}return(lat,lon);}catch{return null;}finally{if(report!=null)Marshal.ReleaseComObject(report);if(location!=null)Marshal.ReleaseComObject(location);}
 }
 [StructLayout(LayoutKind.Sequential)]private struct SystemTime{public ushort Year,Month,DayOfWeek,Day,Hour,Minute,Second,Milliseconds;}
 [ComImport,Guid("AB2ECE69-56D9-4F28-B525-DE1B0EE44237"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]private interface ILocation{[PreserveSig]int RegisterForReport(IntPtr events,ref Guid type,uint interval);[PreserveSig]int UnregisterForReport(ref Guid type);[PreserveSig]int GetReport(ref Guid type,[MarshalAs(UnmanagedType.Interface)]out object report);}
 [ComImport,Guid("7FED806D-0EF8-4f07-80AC-36A0BEAE3134"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]private interface ILatLongReport{[PreserveSig]int GetSensorID(out Guid id);[PreserveSig]int GetTimestamp(out SystemTime timestamp);[PreserveSig]int GetValue(IntPtr key,IntPtr value);[PreserveSig]int GetLatitude(out double latitude);[PreserveSig]int GetLongitude(out double longitude);}
}

