using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Media;
namespace BarraInteligente;
internal sealed class Result : INotifyPropertyChanged
{
 internal Result(string title,string description,string icon,Func<Task> run,string target="",string category="windows",string? id=null){Title=title;Description=description;Icon=icon;Run=run;Target=target;Category=category;Id=id??(target.Length>0?target:title);}
 public string Title {get;}public string Description {get;}public string Icon {get;}internal Func<Task> Run {get;}public string Target {get;}public string Category {get;}public string Id {get;}
 private ImageSource? bitmap;public ImageSource? Bitmap{get=>bitmap;private set{bitmap=value;Changed(nameof(Bitmap));Changed(nameof(GlyphVisibility));Changed(nameof(ImageVisibility));}}
 public System.Windows.Visibility GlyphVisibility=>bitmap==null?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;
 public System.Windows.Visibility ImageVisibility=>bitmap!=null?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;
 private bool recent;public bool IsRecent{get=>recent;set{recent=value;Changed(nameof(RecentLabel));}}public string RecentLabel=>IsRecent?"RECENTE":"";
 // Miniaturas de arquivos usam um quadro maior que os ícones comuns.
 private double iconSize=30;public double IconSize{get=>iconSize;set{iconSize=value;Changed(nameof(IconSize));Changed(nameof(IconBoxWidth));}}public double IconBoxWidth=>iconSize+10;
 // Barra de ajuste opcional: o valor é aplicado imediatamente a cada mudança.
 private Action<int>? apply;private double sliderValue;
 internal void UseSlider(int value,Action<int> onChange){sliderValue=value;apply=onChange;}
 public bool HasSlider=>apply!=null;
 public System.Windows.Visibility SliderVisibility=>apply!=null?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;
 public double SliderValue{get=>sliderValue;set{var v=Math.Clamp(Math.Round(value),0,100);if(v==sliderValue)return;sliderValue=v;try{apply?.Invoke((int)v);}catch{}Changed(nameof(SliderValue));Changed(nameof(SliderText));}}
 public string SliderText=>$"{sliderValue:0}%";
 internal void Nudge(int delta)=>SliderValue=sliderValue+delta;
 // Caminho usado só para o ícone, quando o destino não é um arquivo (ex.: janela aberta → executável dela).
 internal string? IconPath {get;set;}
 internal async Task LoadIcon(){if(Category=="files" && Target.Length>0)Bitmap=await ShellIcons.Thumbnail(Target);else if(IconPath!=null)Bitmap=await ShellIcons.Get(IconPath);else if(Target.Length>0 && !Target.StartsWith("action:") && (Category!="windows" || System.IO.File.Exists(Target)))Bitmap=await ShellIcons.Get(Target);}
 public override string ToString()=>Title;
 public event PropertyChangedEventHandler? PropertyChanged;private void Changed(string property)=>PropertyChanged?.Invoke(this,new(property));
}

