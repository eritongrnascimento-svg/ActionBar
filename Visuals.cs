#pragma warning disable WPF0001 // WPF Fluent ThemeMode is explicitly opted in on the pinned .NET 10 target.
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace BarraInteligente;
internal static class Visuals
{
 internal static bool IsDark(Appearance a)=>a.Theme!="Claro" && (a.Theme!="Sistema" || Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","AppsUseLightTheme",0) is not int v || v==0);
 internal static void Theme(Window window,Appearance a)
 {
  window.ThemeMode=a.Theme switch{"Claro"=>ThemeMode.Light,"Sistema"=>ThemeMode.System,_=>ThemeMode.Dark};
  window.FontFamily=new FontFamily(a.Font);window.Foreground=IsDark(a)?Brushes.WhiteSmoke:new SolidColorBrush(Color.FromRgb(25,29,38));
  window.Background=new SolidColorBrush(IsDark(a)?Color.FromRgb(13,15,20):Color.FromRgb(245,246,250));
  var hwnd=new WindowInteropHelper(window).Handle;if(hwnd==IntPtr.Zero)return;var dark=IsDark(a)?1:0;Native.DwmSetWindowAttribute(hwnd,20,ref dark,4);var corner=2;Native.DwmSetWindowAttribute(hwnd,33,ref corner,4);var caption=IsDark(a)?0x00120E0C:0x00FBF7F6;Native.DwmSetWindowAttribute(hwnd,35,ref caption,4);
  if(OperatingSystem.IsWindowsVersionAtLeast(10,0,22621))
  {var type=a.Glass?3:1;var margins=new Native.Margins{Left=a.Glass?-1:0};Native.DwmExtendFrameIntoClientArea(hwnd,ref margins);Native.DwmSetWindowAttribute(hwnd,38,ref type,4);if(a.Glass){HwndSource.FromHwnd(hwnd).CompositionTarget.BackgroundColor=Colors.Transparent;window.Background=new SolidColorBrush(IsDark(a)?Color.FromArgb(205,12,14,18):Color.FromArgb(110,246,247,251));}}
 }
 internal static void Open(Window window,Appearance a)
 {
  window.BeginAnimation(UIElement.OpacityProperty,null);window.Opacity=1;
  if(!a.Animations || !SystemParameters.ClientAreaAnimation)return;
  var duration=TimeSpan.FromMilliseconds(Math.Clamp(a.AnimationMs,60,600));window.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,1,duration));
  if(window.Content is FrameworkElement content){var translate=new TranslateTransform();content.RenderTransform=translate;translate.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(12,0,duration){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}});}
 }
 internal static Brush? Image(Appearance a){try{if(!File.Exists(a.BackgroundImage))return null;var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.UriSource=new Uri(a.BackgroundImage);bitmap.DecodePixelWidth=1600;bitmap.EndInit();bitmap.Freeze();return new ImageBrush(bitmap){Stretch=Stretch.UniformToFill,Opacity=a.ImageOpacity};}catch{return null;}}
}
internal sealed class NotificationCenter(Store store)
{
 private Window? current;
 internal void Show(string title,string message,bool preview=false,Settings? options=null)
 {
  var s=options??store.Data;if(!s.Notifications && !preview)return;current?.Close();
  var window=new Window{Title="Notificação · ActionBar",Icon=Brand.WindowIcon,Width=370,SizeToContent=SizeToContent.Height,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,ShowActivated=false,Topmost=true};current=window;
  var stack=new StackPanel();stack.Children.Add(new TextBlock{Text="◈  "+title,FontSize=16,FontWeight=FontWeights.SemiBold});stack.Children.Add(new TextBlock{Text=message,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,0),MaxHeight=130});
  var close=new Button{Content="Fechar",HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};close.Click+=(_,_)=>window.Close();stack.Children.Add(close);
  window.Content=new Border{Padding=new Thickness(20),CornerRadius=new CornerRadius(12),BorderThickness=new Thickness(1),BorderBrush=new SolidColorBrush(Color.FromArgb(90,120,160,210)),Child=stack};
  window.SourceInitialized+=(_,_)=>{Visuals.Theme(window,s.Appearance);var h=new WindowInteropHelper(window).Handle;Native.SetWindowLong(h,-20,Native.GetWindowLong(h,-20)|0x8000000|0x80);};
  window.Show();var screen=System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position);var dpi=VisualTreeHelper.GetDpi(window);var area=screen.WorkingArea;
  window.Left=(s.NotificationPosition.Contains("esquerda")?area.Left+24:area.Right-window.ActualWidth*dpi.DpiScaleX-24)/dpi.DpiScaleX;window.Top=(s.NotificationPosition.StartsWith("Superior")?area.Top+24:area.Bottom-window.ActualHeight*dpi.DpiScaleY-24)/dpi.DpiScaleY;
  if(s.NotificationAnimation)Visuals.Open(window,s.Appearance);
  var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(Math.Clamp(s.NotificationSeconds,2,20))};timer.Tick+=(_,_)=>{timer.Stop();if(!window.IsVisible)return;if(s.NotificationAnimation && s.Appearance.Animations){var fade=new DoubleAnimation(0,TimeSpan.FromMilliseconds(160));fade.Completed+=(_,_)=>window.Close();window.BeginAnimation(UIElement.OpacityProperty,fade);}else window.Close();};window.Closed+=(_,_)=>timer.Stop();timer.Start();
 }
 internal void Close()=>current?.Close();
}




