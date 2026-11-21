using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
namespace BarraInteligente;
// Identidade visual do ActionBar: ícone "AB" (Assets\ActionBar.ico, embutido no executável) e tema neon.
internal static class Brand
{
 internal const string Name="ActionBar";
 internal static readonly Color Cyan=Color.FromRgb(0,229,255),Magenta=Color.FromRgb(255,43,214);
 private static Stream? IconStream()=>typeof(Brand).Assembly.GetManifestResourceStream("ActionBar.ico");
 private static ImageSource? windowIcon;
 // Ícone da borda das janelas e da barra de tarefas.
 internal static ImageSource? WindowIcon
 {
  get
  {
   if(windowIcon!=null)return windowIcon;
   try{using var s=IconStream();if(s==null)return null;var decoder=new IconBitmapDecoder(s,BitmapCreateOptions.None,BitmapCacheOption.OnLoad);windowIcon=decoder.Frames[0];windowIcon.Freeze();}catch{}
   return windowIcon;
  }
 }
 private static BitmapSource? Logo(int size)
 {
  try{using var s=IconStream();if(s==null)return null;var decoder=new IconBitmapDecoder(s,BitmapCreateOptions.None,BitmapCacheOption.OnLoad);BitmapSource best=decoder.Frames[0];foreach(var f in decoder.Frames)if(f.PixelWidth>=size && f.PixelWidth<best.PixelWidth || best.PixelWidth<size && f.PixelWidth>best.PixelWidth)best=f;best.Freeze();return best;}catch{return null;}
 }
 // Ícone da bandeja no tamanho pequeno do sistema (respeita a escala da tela).
 internal static System.Drawing.Icon TrayIcon()
 {
  try{using var s=IconStream();if(s!=null)return new System.Drawing.Icon(s,System.Windows.Forms.SystemInformation.SmallIconSize);}catch{}
  return System.Drawing.SystemIcons.Application;
 }
 // Gradiente ciano → magenta → ciano que se repete; animado, as cores "correm" pelas letras/borda.
 private static LinearGradientBrush Flow(bool animate,double seconds)
 {
  var brush=new LinearGradientBrush{StartPoint=new Point(0,0),EndPoint=new Point(1,0),SpreadMethod=GradientSpreadMethod.Repeat};
  brush.GradientStops.Add(new GradientStop(Cyan,0));brush.GradientStops.Add(new GradientStop(Magenta,.5));brush.GradientStops.Add(new GradientStop(Cyan,1));
  var shift=new TranslateTransform();brush.RelativeTransform=shift;
  if(animate)shift.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(0,1,TimeSpan.FromSeconds(seconds)){RepeatBehavior=RepeatBehavior.Forever});
  return brush;
 }
 // Título "ActionBar" com logo: cores fluindo e brilho pulsando que alterna entre ciano e magenta.
 internal static FrameworkElement NeonTitle(bool animate)
 {
  var row=new StackPanel{Orientation=Orientation.Horizontal};
  if(Logo(32) is BitmapSource logo)row.Children.Add(new Image{Source=logo,Width=20,Height=20,Margin=new Thickness(0,0,8,0),VerticalAlignment=VerticalAlignment.Center});
  var text=new TextBlock{Text=Name,FontSize=19,FontWeight=FontWeights.SemiBold,Foreground=Flow(animate,4),VerticalAlignment=VerticalAlignment.Center};
  var glow=new DropShadowEffect{Color=Cyan,ShadowDepth=0,BlurRadius=12,Opacity=.9};text.Effect=glow;row.Children.Add(text);
  if(animate)
  {
   glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty,new DoubleAnimation(8,20,TimeSpan.FromSeconds(1.6)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,EasingFunction=new SineEase{EasingMode=EasingMode.EaseInOut}});
   glow.BeginAnimation(DropShadowEffect.ColorProperty,new ColorAnimation(Cyan,Magenta,TimeSpan.FromSeconds(2)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever});
  }
  System.Windows.Automation.AutomationProperties.SetName(row,Name);
  return row;
 }
 // Borda da barra no mesmo gradiente, mais lenta e discreta.
 internal static Brush NeonBorder(bool animate){var b=Flow(animate,8);b.Opacity=.75;return b;}
}
