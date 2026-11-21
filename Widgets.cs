using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
namespace BarraInteligente;
internal sealed class Widgets : ContentControl,IDisposable
{
 private readonly Store store;private readonly Action<DiskInfo> openDisk;private readonly WeatherService weather=new();private ResourceMonitor? hardware;private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(1)};private CancellationTokenSource? pending;private int version;private bool hardwareVisible;private TextBlock? cpu,memory,gpu,details;private ProgressBar? cpuBar,memBar,gpuBar;
 internal Widgets(Store store,Action<DiskInfo> openDisk){this.store=store;this.openDisk=openDisk;Visibility=Visibility.Collapsed;Margin=new Thickness(0,0,0,10);timer.Tick+=(_,_)=>Sample();}
 internal static bool HardwareQuery(string query)=>System.Text.RegularExpressions.Regex.IsMatch(Commands.Normalize(query),@"\b(cpu\d*|gpu\d*|ram|memoria\w*|hardware|recurso\w*|processador\w*|desempenho)\b|placa de video");
 internal void Set(string mode,string query,bool expanded)
 {
  Stop();if(!expanded)return;if((mode==""||mode=="config") && HardwareQuery(query)){ShowHardware();return;}if(mode=="arquivo" && query.Length==0){_=ShowDisks(version,pending!.Token);return;}if(mode=="web" && store.Data.WeatherEnabled){_=ShowWeather(version,pending!.Token);}
 }
 private void Stop(){version++;pending?.Cancel();pending?.Dispose();pending=new CancellationTokenSource();timer.Stop();hardwareVisible=false;Visibility=Visibility.Collapsed;Content=null;}
 private StackPanel Card(string title)
 {
  Visibility=Visibility.Visible;var stack=new StackPanel();stack.Children.Add(new TextBlock{Text=title,FontSize=15,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,10)});Content=new Border{Padding=new Thickness(14),CornerRadius=new CornerRadius(10),BorderThickness=new Thickness(1),BorderBrush=new SolidColorBrush(Color.FromArgb(55,140,160,185)),Background=new SolidColorBrush(Color.FromArgb(22,130,160,200)),Child=stack};return stack;
 }
 private async Task ShowDisks(int current,CancellationToken token)
 {
  var card=Card("Armazenamento disponível");var body=new StackPanel();card.Children.Add(new ScrollViewer{HorizontalContentAlignment=HorizontalAlignment.Stretch,Content=body,MaxHeight=210,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});body.Children.Add(new TextBlock{Text="Lendo unidades…",Opacity=.65});
  try{var disks=await Task.Run(DiskInfo.Read,token);if(current!=version || token.IsCancellationRequested)return;body.Children.Clear();foreach(var disk in disks){var content=new StackPanel();content.Children.Add(new TextBlock{Text="▰  "+disk.Name,FontWeight=FontWeights.SemiBold});if(disk.Ready){content.Children.Add(new ProgressBar{Minimum=0,Maximum=100,Value=disk.UsedPercent,Height=5,Margin=new Thickness(0,9,0,6)});content.Children.Add(new TextBlock{Text=$"{DiskInfo.Bytes(disk.Used)} usados  ·  {DiskInfo.Bytes(disk.Free)} livres  ·  {DiskInfo.Bytes(disk.Total)} total",FontSize=11,Opacity=.7});}else content.Children.Add(new TextBlock{Text="Sem mídia ou unidade indisponível",FontSize=11,Opacity=.65});var button=new Button{Content=content,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(12,9,12,9),Margin=new Thickness(0,0,0,7),IsEnabled=disk.Ready};button.Click+=(_,_)=>openDisk(disk);body.Children.Add(button);}}catch(OperationCanceledException){}catch{if(current==version){body.Children.Clear();body.Children.Add(new TextBlock{Text="Não foi possível consultar os discos.",Opacity=.7});}}
 }
 private void ShowHardware()
 {
  hardwareVisible=true;hardware??=new ResourceMonitor();var card=Card("Monitor de recursos · ao vivo");
  var grid=new Grid();for(int i=0;i<3;i++)grid.ColumnDefinitions.Add(new ColumnDefinition());card.Children.Add(grid);
  (TextBlock,ProgressBar) Metric(string title,int column){var stack=new StackPanel{Margin=new Thickness(column==0?0:14,0,0,0)};stack.Children.Add(new TextBlock{Text=title,Opacity=.65,FontSize=12});var value=new TextBlock{Text="Coletando…",FontSize=23,Margin=new Thickness(0,5,0,8)};stack.Children.Add(value);var bar=new ProgressBar{Height=5,Maximum=100};stack.Children.Add(bar);Grid.SetColumn(stack,column);grid.Children.Add(stack);return(value,bar);}
  (cpu,cpuBar)=Metric("CPU",0);(memory,memBar)=Metric("MEMÓRIA",1);(gpu,gpuBar)=Metric("GPU",2);details=new TextBlock{FontSize=11,Opacity=.6,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,0)};card.Children.Add(details);Sample();timer.Start();_=Task.Run(hardware.LoadGpuName);
 }
 private void Sample()
 {
  if(!hardwareVisible || hardware==null || cpu==null)return;try{var s=hardware.Read();cpu.Text=s.Cpu.HasValue?$"{s.Cpu:0.#}%":"Coletando…";memory!.Text=$"{s.MemoryPercent:0.#}%";gpu!.Text=s.Gpu.HasValue?$"{s.Gpu:0.#}%":"Indisponível";cpuBar!.Value=s.Cpu??0;memBar!.Value=s.MemoryPercent;gpuBar!.Value=s.Gpu??0;details!.Text=$"{s.CpuName}\nRAM: {DiskInfo.Bytes((long)(s.TotalMemory-s.FreeMemory))} / {DiskInfo.Bytes((long)s.TotalMemory)}  ·  {Environment.ProcessorCount} processadores lógicos\n{s.GpuName} · uso do mecanismo mais ocupado · atualizado a cada segundo";}catch{details!.Text="Algumas leituras de hardware não estão disponíveis.";}
 }
 private async Task ShowWeather(int current,CancellationToken token,bool force=false)
 {
  var card=Card("Clima · localização automática");var value=new TextBlock{Text="Consultando a localização e o clima…",TextWrapping=TextWrapping.Wrap,Opacity=.8};card.Children.Add(value);
  var footer=new DockPanel{Margin=new Thickness(0,10,0,0)};card.Children.Add(footer);var refresh=new Button{Content="Atualizar",Padding=new Thickness(8,3,8,3)};DockPanel.SetDock(refresh,Dock.Right);footer.Children.Add(refresh);refresh.Click+=(_,_)=>_=ShowWeather(current,token,true);footer.Children.Add(new TextBlock{Text="Dados: Open-Meteo",FontSize=10,Opacity=.55,VerticalAlignment=VerticalAlignment.Center});
  try{var data=await weather.Get(token,force);if(current!=version || token.IsCancellationRequested)return;value.Text=$"{data.Place}    {data.Temperature:0.#} °C · {WeatherService.Description(data.Code)}\nSensação {data.FeelsLike:0.#} °C  ·  Umidade {data.Humidity}%  ·  Vento {data.Wind:0.#} km/h";card.Children.Insert(2,new TextBlock{Text=data.Origin+" · "+data.Updated.ToLocalTime().ToString("HH:mm"),FontSize=10,Opacity=.5,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,7,0,0)});}catch(OperationCanceledException){}catch{if(current==version)value.Text="Clima indisponível no momento. Verifique a conexão e tente atualizar.";}
 }
 internal void Pause(){Stop();}
 public void Dispose(){Stop();pending?.Cancel();timer.Stop();hardware?.Dispose();}
}


