#pragma warning disable WPF0001 // WPF Fluent ThemeMode is explicitly opted in on the pinned .NET 10 target.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Forms=System.Windows.Forms;
namespace BarraInteligente;

internal sealed class Launcher : Window
{
 private readonly BarPanels panels;
 private readonly Store store;private readonly WindowManager manager;private readonly FileIndex index;private readonly NotificationCenter notifications;
 private readonly TextBox input=new();private readonly ListBox results=new();private readonly TextBlock status=new();private readonly TextBlock icon=new();private readonly Border surface=new();private readonly Border image=new();
 private readonly Widgets widgets;private readonly Dictionary<string,Button> filterButtons=new();private string selectedMode="",widgetKey="";private bool filterOpened,expanded,opening;
 private List<AppEntry> browsers=new();private List<AppEntry> apps=new();private readonly Forms.NotifyIcon tray;private Hotkeys? hotkeys;private CancellationTokenSource? search;private readonly System.Windows.Threading.DispatcherTimer updateTimer=new(){Interval=TimeSpan.FromMinutes(15)};
 private bool exit,busy,configuring,disposed;private IntPtr hwnd;private int request;
 internal bool HotkeyRegistered {get;private set;}
 internal Launcher(Store store)
 {
  this.store=store;panels=new BarPanels(this,Path.GetDirectoryName(store.FilePath)!,()=>store.Data);manager=new WindowManager(store);index=new FileIndex(store);notifications=new NotificationCenter(store);widgets=new Widgets(store,disk=>_=ExecuteResult(OpenItem(disk.Name,disk.Root,"▰",disk.Root,"files")));
  Title="ActionBar";WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;
  var shell=new Grid();Content=shell;shell.Children.Add(image);surface.CornerRadius=new CornerRadius(14);surface.BorderThickness=new Thickness(1);surface.BorderBrush=new SolidColorBrush(Color.FromArgb(80,130,150,185));surface.Padding=new Thickness(22);shell.Children.Add(surface);
  var panel=new DockPanel();surface.Child=panel;
  // Topo: nome "ActionBar" centralizado em neon animado; configurações à direita.
  var heading=new Grid{Margin=new Thickness(0,0,0,12)};DockPanel.SetDock(heading,Dock.Top);panel.Children.Add(heading);
  var animate=store.Data.Appearance.Animations && SystemParameters.ClientAreaAnimation;
  var brandTitle=Brand.NeonTitle(animate);brandTitle.HorizontalAlignment=HorizontalAlignment.Center;brandTitle.VerticalAlignment=VerticalAlignment.Center;heading.Children.Add(brandTitle);
  var settings=new Button{Content="⚙",ToolTip="Configurações",Padding=new Thickness(10,4,10,4),HorizontalAlignment=HorizontalAlignment.Right};settings.Click+=(_,_)=>Configure();heading.Children.Add(settings);
  surface.BorderBrush=Brand.NeonBorder(animate);surface.BorderThickness=new Thickness(1.2);Icon=Brand.WindowIcon;
  var searchRow=new DockPanel{Margin=new Thickness(0,0,0,12)};DockPanel.SetDock(searchRow,Dock.Top);panel.Children.Add(searchRow);icon.FontSize=26;icon.VerticalAlignment=VerticalAlignment.Center;icon.Margin=new Thickness(0,0,12,0);DockPanel.SetDock(icon,Dock.Left);searchRow.Children.Add(icon);
  input.Padding=new Thickness(12);System.Windows.Automation.AutomationProperties.SetName(input,"Pesquisar aplicativos, arquivos ou ações");searchRow.Children.Add(input);
  var filters=new WrapPanel{Margin=new Thickness(0,0,0,12)};DockPanel.SetDock(filters,Dock.Top);panel.Children.Add(filters);
  foreach(var (label,prefix) in new[]{("Tudo",""),("Apps","app: "),("Arquivos","arquivo: "),("Windows","config: "),("Janelas","janela: "),("Grupos","grupo: "),("Web","web: ")}.Concat(BarPanels.Modes.Select(m=>(m.Label,m.Key+": ")))){var button=new Button{Content=label,Padding=new Thickness(9,4,9,4),Margin=new Thickness(0,0,6,4)};var mode=prefix.Trim().TrimEnd(':');filterButtons[mode]=button;button.Click+=(_,_)=>{if(BarPanels.Modes.Any(item=>item.Key==mode))ShowPanel(mode,mode=="terminal"?"":input.Text.Trim());else SelectFilter(mode);};filters.Children.Add(button);}
  status.FontSize=11;status.Opacity=.65;status.TextWrapping=TextWrapping.Wrap;status.Margin=new Thickness(0,12,0,0);DockPanel.SetDock(status,Dock.Bottom);panel.Children.Add(status);
  results.BorderThickness=new Thickness(0);results.Background=Brushes.Transparent;results.HorizontalContentAlignment=HorizontalAlignment.Stretch;
  DockPanel.SetDock(widgets,Dock.Top);panel.Children.Add(widgets);
  var template=new DataTemplate(typeof(Result));var row=new FrameworkElementFactory(typeof(DockPanel));row.SetValue(FrameworkElement.MarginProperty,new Thickness(7,6,7,6));
  var recent=new FrameworkElementFactory(typeof(TextBlock));recent.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("RecentLabel"));recent.SetValue(DockPanel.DockProperty,Dock.Right);recent.SetValue(TextBlock.FontSizeProperty,9d);recent.SetValue(UIElement.OpacityProperty,.6);recent.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);recent.SetValue(FrameworkElement.MarginProperty,new Thickness(8,0,0,0));row.AppendChild(recent);
  var iconBox=new FrameworkElementFactory(typeof(Grid));iconBox.SetBinding(FrameworkElement.WidthProperty,new System.Windows.Data.Binding("IconBoxWidth"));iconBox.SetValue(FrameworkElement.MarginProperty,new Thickness(0,0,9,0));iconBox.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);
  var bitmap=new FrameworkElementFactory(typeof(System.Windows.Controls.Image));bitmap.SetBinding(System.Windows.Controls.Image.SourceProperty,new System.Windows.Data.Binding("Bitmap"));bitmap.SetBinding(UIElement.VisibilityProperty,new System.Windows.Data.Binding("ImageVisibility"));bitmap.SetBinding(FrameworkElement.WidthProperty,new System.Windows.Data.Binding("IconSize"));bitmap.SetBinding(FrameworkElement.HeightProperty,new System.Windows.Data.Binding("IconSize"));bitmap.SetValue(System.Windows.Controls.Image.StretchProperty,Stretch.Uniform);iconBox.AppendChild(bitmap);
  var glyph=new FrameworkElementFactory(typeof(TextBlock));glyph.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("Icon"));glyph.SetBinding(UIElement.VisibilityProperty,new System.Windows.Data.Binding("GlyphVisibility"));glyph.SetValue(TextBlock.FontSizeProperty,23d);glyph.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);iconBox.AppendChild(glyph);row.AppendChild(iconBox);
  // Barra de ajuste (volume, microfone…): arrastar, rolar o mouse sobre a linha ou ← → com a linha selecionada.
  var bar=new FrameworkElementFactory(typeof(DockPanel));bar.SetValue(DockPanel.DockProperty,Dock.Right);bar.SetBinding(UIElement.VisibilityProperty,new System.Windows.Data.Binding("SliderVisibility"));bar.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);bar.SetValue(FrameworkElement.MarginProperty,new Thickness(10,0,0,0));
  var amount=new FrameworkElementFactory(typeof(TextBlock));amount.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("SliderText"));amount.SetValue(DockPanel.DockProperty,Dock.Right);amount.SetValue(FrameworkElement.WidthProperty,42d);amount.SetValue(TextBlock.TextAlignmentProperty,TextAlignment.Right);amount.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);bar.AppendChild(amount);
  var slider=new FrameworkElementFactory(typeof(Slider));slider.SetValue(System.Windows.Controls.Primitives.RangeBase.MinimumProperty,0d);slider.SetValue(System.Windows.Controls.Primitives.RangeBase.MaximumProperty,100d);slider.SetValue(FrameworkElement.WidthProperty,190d);slider.SetValue(Slider.IsMoveToPointEnabledProperty,true);slider.SetValue(UIElement.FocusableProperty,false);slider.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);slider.SetBinding(System.Windows.Controls.Primitives.RangeBase.ValueProperty,new System.Windows.Data.Binding("SliderValue"){Mode=System.Windows.Data.BindingMode.TwoWay});bar.AppendChild(slider);row.AppendChild(bar);
  var text=new FrameworkElementFactory(typeof(StackPanel));var title=new FrameworkElementFactory(typeof(TextBlock));title.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("Title"));title.SetValue(TextBlock.FontSizeProperty,16d);title.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);text.AppendChild(title);var description=new FrameworkElementFactory(typeof(TextBlock));description.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("Description"));description.SetValue(TextBlock.FontSizeProperty,11d);description.SetValue(UIElement.OpacityProperty,.6);description.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);description.SetValue(FrameworkElement.MarginProperty,new Thickness(0,4,0,0));text.AppendChild(description);row.AppendChild(text);template.VisualTree=row;results.ItemTemplate=template;var body=new Grid();body.Children.Add(results);body.Children.Add(panels.View);panel.Children.Add(body);  var context=new ContextMenu();var reveal=new MenuItem{Header="Abrir pasta do arquivo"};reveal.Click+=(_,_)=>{if(results.SelectedItem is Result r && File.Exists(r.Target)){var folder=Path.GetDirectoryName(r.Target);if(folder!=null)ShowPanel("pastas",folder);}};context.Items.Add(reveal);var copy=new MenuItem{Header="Copiar caminho"};copy.Click+=(_,_)=>{if(results.SelectedItem is Result r && r.Target.Length>0)Clipboard.SetText(r.Target);};context.Items.Add(copy);results.ContextMenu=context;
  input.TextChanged+=(_,_)=>Refresh();results.MouseDoubleClick+=async(_,e)=>{if(e.OriginalSource is DependencyObject d && FindParent<Slider>(d)!=null)return;await Execute();};
  // Roda do mouse sobre uma linha com barra ajusta o valor em vez de rolar a lista.
  results.PreviewMouseWheel+=(_,e)=>{if(e.OriginalSource is FrameworkElement f && f.DataContext is Result r && r.HasSlider){r.Nudge(e.Delta>0?2:-2);e.Handled=true;}};
  PreviewKeyDown+=async(_,e)=>{
   if(panels.IsOpen){
    if(e.Key==Key.Escape && !panels.TerminalHasFocus){SelectFilter("");e.Handled=true;}
    else if(e.Key==Key.Enter && input.IsKeyboardFocused && panels.CurrentMode is string mode){
     var query=input.Text.Trim();var separator=query.IndexOf(':');
     if(separator>=0 && Commands.Normalize(query[..separator])==mode)query=query[(separator+1)..].Trim();
     panels.Show(mode,query);e.Handled=true;
    }
    return;
   }
   if(e.Key==Key.Escape){Dismiss();e.Handled=true;}else if(e.Key==Key.Enter){e.Handled=true;await Execute();}else if(e.Key is Key.Left or Key.Right && Keyboard.Modifiers==ModifierKeys.None && input.IsKeyboardFocused && input.SelectionLength==0)
   {
    var forward=e.Key==Key.Right;var atEnd=input.CaretIndex==input.Text.Length;
    // Linha com barra selecionada e cursor no fim do texto: as setas ajustam o valor.
    if(results.SelectedItem is Result r && r.HasSlider && atEnd){r.Nudge(forward?5:-5);e.Handled=true;}
    // → no fim do texto e ← no início trocam de aba (no meio do texto as setas continuam editando).
    else if(forward?atEnd:input.CaretIndex==0){MoveFilter(forward?1:-1);e.Handled=true;}
   }
   else if(e.Key==Key.Tab){MoveFilter(Keyboard.Modifiers==ModifierKeys.Shift?-1:1);e.Handled=true;}
   else if(e.Key is Key.Down or Key.Up){if(results.Items.Count>0){results.SelectedIndex=Math.Clamp(results.SelectedIndex+(e.Key==Key.Down?1:-1),0,results.Items.Count-1);results.ScrollIntoView(results.SelectedItem);}e.Handled=true;}};
  SourceInitialized+=(_,_)=>{hwnd=new WindowInteropHelper(this).Handle;HwndSource.FromHwnd(hwnd).AddHook(Hook);hotkeys=new Hotkeys(hwnd);var error=hotkeys.Configure(store.Data,()=>Toggle(),RunTarget);HotkeyRegistered=error==null;ApplyAppearance();if(error!=null)notifications.Show("Atalho indisponível",error);};
  Deactivated+=(_,_)=>{if(store.Data.HideOnBlur && !panels.IsOpen && !opening && !configuring && !busy && results.ContextMenu?.IsOpen!=true)Dismiss();};
  Activated+=(_,_)=>QueueInputFocus();ContentRendered+=(_,_)=>QueueInputFocus();
  Closing+=(_,e)=>{if(!exit){e.Cancel=true;Dismiss();}};
  tray=new Forms.NotifyIcon{Text="ActionBar",Icon=Brand.TrayIcon(),Visible=true};var menu=new Forms.ContextMenuStrip();menu.Items.Add("Abrir",null,(_,_)=>Toggle(true));menu.Items.Add("Configurações",null,(_,_)=>Configure());menu.Items.Add("Atualizar índice de arquivos",null,(_,_)=>_=index.Rebuild());menu.Items.Add("Encerrar agrupamento",null,(_,_)=>manager.EndGroup());menu.Items.Add("Sair",null,(_,_)=>Quit());tray.ContextMenuStrip=menu;tray.DoubleClick+=(_,_)=>Toggle(true);
  index.Changed+=OnIndexChanged;updateTimer.Tick+=(_,_)=>{if(store.Data.AutoIndex && store.Data.IndexFiles)_=index.Rebuild();};updateTimer.Start();ApplyAppearance();_=Initialize();
 }
 private async Task Initialize(){apps=await Task.Run(Catalog.Apps);browsers=await Task.Run(()=>AppDiscovery.Browsers(apps));await index.Load();Refresh();if(store.Data.IndexFiles && store.Data.AutoIndex)await index.Rebuild();}
 private void OnIndexChanged(){if(!disposed)Dispatcher.BeginInvoke(()=>{if(!disposed)status.Text=$"{apps.Count:N0} apps · {index.Status} · {store.Data.Hotkey} · Enter executar";});}
 private void ApplyAppearance()
 {
  var a=store.Data.Appearance;Visuals.Theme(this,a);panels.ApplyMusicTheme(Visuals.IsDark(a));var screen=Forms.Screen.FromPoint(Forms.Cursor.Position);var dpi=VisualTreeHelper.GetDpi(this);Width=Math.Clamp(a.Width,500,Math.Max(500,screen.WorkingArea.Width/dpi.DpiScaleX-30));var availableHeight=Math.Max(300,screen.WorkingArea.Height/dpi.DpiScaleY-36);Height=expanded?Math.Min(availableHeight,Math.Max(panels.IsOpen?610:400,a.Height)):242;Topmost=store.Data.AlwaysOnTop;input.FontSize=a.FontSize;icon.Text=a.SearchIcon=="Nenhum"?"":a.SearchIcon;icon.Visibility=a.SearchIcon=="Nenhum"?Visibility.Collapsed:Visibility.Visible;
  image.Background=Visuals.Image(a);image.ClipToBounds=true;surface.Background=new SolidColorBrush(Visuals.IsDark(a)?Color.FromArgb(a.Glass?(byte)55:(byte)250,12,14,18):Color.FromArgb(a.Glass?(byte)110:(byte)245,246,247,251));
 }
 private IntPtr Hook(IntPtr h,int msg,IntPtr w,IntPtr l,ref bool handled){if(msg==0x312)handled=hotkeys?.Dispatch(w.ToInt32())==true;return IntPtr.Zero;}
 internal void Toggle(bool force=false)
 {
  if(configuring)return;panels.Hide();if(IsVisible && !force){Dismiss();return;}if(!IsVisible)WindowActions.Capture(hwnd);opening=true;selectedMode="";filterOpened=false;expanded=false;input.Clear();ApplyAppearance();Show();Native.GetCursorPos(out var p);var screen=Forms.Screen.FromPoint(new System.Drawing.Point(p.X,p.Y));var dpi=VisualTreeHelper.GetDpi(this);Left=(screen.WorkingArea.Left+(screen.WorkingArea.Width-Width*dpi.DpiScaleX)/2)/dpi.DpiScaleX;Top=(screen.WorkingArea.Top+Math.Max(20,(screen.WorkingArea.Height-Height*dpi.DpiScaleY)*.23))/dpi.DpiScaleY;Activate();Native.FocusLauncher(hwnd);Visuals.Open(this,store.Data.Appearance);Refresh();QueueInputFocus();
 }
 private void QueueInputFocus()
 {
  Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input,()=>{if(!IsVisible || configuring || disposed || panels.IsOpen)return;FocusManager.SetFocusedElement(this,input);Native.FocusLauncher(hwnd);input.Focus();Keyboard.Focus(input);input.CaretIndex=input.Text.Length;});
  Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle,()=>{if(!IsVisible || configuring || disposed || panels.IsOpen)return;if(IsActive){input.Focus();Keyboard.Focus(input);}opening=false;});
 }
 private void SelectFilter(string mode){panels.Hide();selectedMode=mode;filterOpened=true;input.Clear();Refresh();QueueInputFocus();}
 private static readonly string[] FilterOrder=new[]{"","app","arquivo","config","janela","grupo","web"}.Concat(BarPanels.Modes.Select(m=>m.Key)).ToArray();
 // Troca de aba pelo teclado mantendo o texto digitado.
 private void MoveFilter(int step){var i=Array.IndexOf(FilterOrder,selectedMode);selectedMode=FilterOrder[((i<0?0:i)+step+FilterOrder.Length)%FilterOrder.Length];filterOpened=true;Refresh();QueueInputFocus();}
 private static T? FindParent<T>(DependencyObject? d) where T:DependencyObject{while(d!=null && d is not T)d=d is Visual or System.Windows.Media.Media3D.Visual3D?VisualTreeHelper.GetParent(d):LogicalTreeHelper.GetParent(d);return d as T;}
 private void ShowPanel(string mode,string value)
 {
  selectedMode=mode;filterOpened=true;expanded=true;request++;search?.Cancel();
  results.Visibility=status.Visibility=widgets.Visibility=Visibility.Collapsed;widgets.Pause();widgetKey="";
  foreach(var (key,button) in filterButtons){button.FontWeight=key==mode?FontWeights.SemiBold:FontWeights.Normal;button.BorderBrush=key==mode?SystemColors.HighlightBrush:null;}
  panels.Show(mode,value);
  ApplyAppearance();
  var screen=Forms.Screen.FromPoint(Forms.Cursor.Position);var dpi=VisualTreeHelper.GetDpi(this);
  Top=Math.Min(Top,Math.Max(screen.WorkingArea.Top/dpi.DpiScaleY,screen.WorkingArea.Bottom/dpi.DpiScaleY-Height));
 }
 private void Dismiss(){panels.Hide();opening=false;widgets.Pause();widgetKey="";Hide();}
 private Result Action(string title,string description,string glyph,Action action,string target="",string category="windows",string? id=null)=>new(title,description,glyph,()=>{action();return Task.CompletedTask;},target,category,id);
 private Result OpenItem(string title,string description,string glyph,string target,string category,string? id=null)=>new(title,description,glyph,async()=>{if(category=="files" && Directory.Exists(target)){ShowPanel("pastas",target);return;}var message=await SystemIntegration.Execute(target,Confirm);Dismiss();if(message!=null)notifications.Show(title,message);},target,category,id);
 // Ações com valor contínuo ganham uma barra na própria linha.
 private static Result WithBar(Result item)
 {
  switch(item.Target)
  {
   case "action:volume-bar":if(Audio.Volume() is int volume)item.UseSlider(volume,Audio.SetVolume);break;
   case "action:mic-bar":if(Audio.MicrophoneLevel() is int level)item.UseSlider(level,Audio.SetMicrophoneLevel);break;
  }
  return item;
 }
 private static string Describe(SystemAction action){if(!ActionRegistry.NeedsTargetWindow(action.Target) || WindowActions.TargetTitle is not string title)return action.Description;return action.Description+" · "+(title.Length>48?title[..47]+"…":title);}
 private bool Confirm(string text)=>MessageBox.Show(this,text,"Confirmar ação",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes;
 // Atalhos personalizados: a janela em foco no momento do atalho é o alvo das ações de janela.
 private async void RunTarget(string target){try{if(!IsVisible)WindowActions.Capture(hwnd);var message=await SystemIntegration.Execute(target,Confirm);Dismiss();if(message!=null)notifications.Show("ActionBar",message);}catch(Exception ex){notifications.Show("Não foi possível executar",ex.Message);}}
 private static string Category(string mode)=>mode switch{"app"=>"apps","arquivo"=>"files","config"=>"windows","janela"=>"janelas","grupo"=>"groups",_=>mode};
 private async void Refresh()
 {
  if(panels.IsOpen)return;
  var version=++request;search?.Cancel();search?.Dispose();search=new CancellationTokenSource();var token=search.Token;var raw=input.Text.Trim();var q=Commands.Normalize(raw);var mode=selectedMode;var colon=q.IndexOf(':');if(colon>=0 && filterButtons.ContainsKey(q[..colon])){mode=q[..colon];raw=raw[(colon+1)..].Trim();q=Commands.Normalize(raw);}
  if(mode=="" && System.Text.RegularExpressions.Regex.Match(q,@"^(?:pesquisar |buscar |encontrar |procurar )?((?:arquivo|arquivos|pasta|pastas) .+)$") is var fileQuery && fileQuery.Success){mode="arquivo";q=fileQuery.Groups[1].Value;raw=q;}
  expanded=filterOpened || raw.Length>0;results.Visibility=expanded?Visibility.Visible:Visibility.Collapsed;status.Visibility=expanded?Visibility.Visible:Visibility.Collapsed;ApplyAppearance();
  foreach(var (key,button) in filterButtons){button.FontWeight=key==mode?FontWeights.SemiBold:FontWeights.Normal;button.BorderBrush=key==mode?SystemColors.HighlightBrush:null;}
  input.ToolTip=mode switch{"app"=>"Pesquisar aplicativos instalados","arquivo"=>"Pesquisar arquivos e pastas","config"=>"Pesquisar ações e configurações do Windows","janela"=>"Alternar para uma janela aberta","grupo"=>"Pesquisar agrupamentos","web"=>"Pesquisar na web",_=>"Pesquisar aplicativos, arquivos ou ações"};
  var nextWidgetKey=mode+"|"+q+"|"+expanded;if(widgetKey!=nextWidgetKey){widgetKey=nextWidgetKey;widgets.Set(mode,q,expanded);}
  if(!expanded){results.ItemsSource=Array.Empty<Result>();return;}
  try
  {
   if(q.Length>0)await Task.Delay(90,token);var list=new List<Result>();
   if(mode=="" && VideoAddress.TryParse(raw,out _)) list.Add(Action("Tocar link do YouTube","Player do ActionBar","▶",()=>ShowPanel("youtube",raw),category:"panel",id:"video-link"));
   if((mode=="" || mode=="config" || mode=="pastas") && (q.Contains("wallpaper") || q.Contains("papel de parede") || q.Contains("plano de fundo")))
       list.Add(Action("Trocar wallpaper","Sugestões da galeria de imagens","▧",()=>ShowPanel("pastas","\0wallpapers"),category:"panel",id:"wallpaper"));
   if(mode=="" && q is "navegar em pastas" or "abrir pasta" or "navegar pastas")
       list.Add(Action("Navegar em pastas","Navegador de arquivos do ActionBar","▰",()=>ShowPanel("pastas",""),category:"panel",id:"browse"));
   if((mode=="" || mode=="config") && (q.Contains("verificar atualiz") || q.Contains("windows update")))
       list.Add(Action("Verificar atualizações","Consultar o Windows Update na barra","↻",()=>ShowPanel("atualizacoes",""),category:"panel",id:"updates"));
   foreach(var (label,key) in BarPanels.Modes)
   {
    if(mode!=key && !(mode=="" && (q==key || q.StartsWith(key+" ") || (key=="ocr" && q.Contains("ocr")))))continue;
    var term=mode==key?raw:q==key?"":raw.Length>key.Length?raw[(key.Length+1)..]:"";
    var title=key=="ocr"?"Selecionar área, ler e traduzir · OCR":"Abrir "+label;
    list.Add(Action(title,"Painel do ActionBar","▣",()=>ShowPanel(key,term),category:"panel",id:key));
   }
   if((mode==""||mode=="config") && (q.Length==0 || new[]{"configurar","configuracoes da barra","aparencia","atalhos","indexar","sobre"}.Any(k=>q.Contains(k))))list.Add(Action("Configurações do ActionBar","Windows · Aparência, indexação, atalhos e comportamento","⚙",Configure,id:"bar-settings"));
   if(mode=="" || mode=="config")foreach(var action in ActionRegistry.Interpret(q,raw))list.Add(OpenItem(action.Title,"Windows · "+action.Description,action.Glyph,action.Target,"windows"));
   if(mode=="" && store.Data.Calculator && Calculator.Try(raw,out var calculated))list.Add(Action(calculated,"Calculadora · Enter para copiar o resultado","∑",()=>{Clipboard.SetText(calculated);status.Text="Resultado copiado: "+calculated;},category:"calculator"));
   if(manager.ActiveGroupName is string active && (mode=="grupo" || (mode=="" && (q.Length==0 || q.Contains("encerrar") || q.Contains("agrupamento")))))list.Add(Action("Encerrar agrupamento · "+active,"Grupo · Remover a marcação e manter os aplicativos abertos","▦",()=>{manager.EndGroup();Refresh();},category:"groups",id:"end-group"));
   if(mode=="" || mode=="grupo")foreach(var ws in store.Data.Workspaces.Where(w=>q.Length==0 || Commands.Normalize(w.Name).Contains(q.Replace("abrir ",""))))list.Add(new Result(ws.Name,$"Grupo · {ws.Apps.Count} aplicativos • aplicar layout","▦",async()=>{Dismiss();notifications.Show("Organizando workspace",ws.Name);var message=await manager.Launch(ws);notifications.Show("Workspace",message);},category:"groups",id:ws.Name));
   if(mode=="" || mode=="app")
   {
    var appQuery=q.StartsWith("abrir ")?q[6..]:q;var words=appQuery.Split(' ',StringSplitOptions.RemoveEmptyEntries);
    var found=apps.Where(a=>words.All(t=>Commands.Normalize(a.Name).Contains(t))).OrderBy(a=>Commands.Normalize(a.Name)==appQuery?0:Commands.Normalize(a.Name).StartsWith(appQuery)?1:2).ToList();
    if(found.Count==0 && words.Length>0)found=apps.Select(a=>(App:a,Ok:Fuzzy.Matches(Commands.Normalize(a.Name),words,out var cost),Cost:cost)).Where(x=>x.Ok).OrderBy(x=>x.Cost).Take(5).Select(x=>x.App).ToList();foreach(var app in q.Length==0?found:found.Take(25))list.Add(OpenItem(app.Name,"Aplicativo · "+app.Source,"▣",app.Target,"apps"));
   }
   if(mode=="janela" || (mode=="" && q.Length>=3))
   {
    var windowQuery=System.Text.RegularExpressions.Regex.Replace(q,@"^(?:janelas?|alternar para|ir para|mudar para)\s*","");
    if(mode=="janela" || windowQuery!=q || windowQuery.Length>=3)
     foreach(var w in WindowActions.Find(windowQuery).Take(mode=="janela"?40:4))
     {
      var item=OpenItem(w.Title,"Janela aberta · "+(w.Process.Length>0?w.Process:"aplicativo")+" · alternar","◧","action:window:"+w.Handle.ToInt64(),"janelas","window:"+w.Handle.ToInt64());
      if(w.Path.Length>0)item.IconPath=w.Path;list.Add(item);
     }
   }
   if(mode=="" || mode=="config")foreach(var action in (q.Length==0?Catalog.Visible:Catalog.Find(q)).Where(a=>!ActionRegistry.NeedsTargetWindow(a.Target) || WindowActions.TargetTitle!=null))list.Add(WithBar(OpenItem(action.Title,"Windows · "+Describe(action),action.Glyph,action.Target,"windows")));
   if((mode=="" || mode=="arquivo") && store.Data.IndexFiles)
   {
    if(q.Length>=2){var query=q.StartsWith("abrir ")?q[6..]:q;var favorites=store.Data.HistoryEnabled?store.Data.History.Where(h=>h.Category=="files").Select(h=>h.Target).ToArray():Array.Empty<string>();var files=await Task.Run(()=>index.Find(query,mode=="arquivo"?80:12,token,favorites),token);token.ThrowIfCancellationRequested();foreach(var file in files){var item=OpenItem(file.Name,(file.Folder?"Pasta · ":"Arquivo · ")+file.Path,file.Folder?"▰":"▤",file.Path,"files");item.IconSize=48;list.Add(item);}if((File.Exists(raw)||Directory.Exists(raw)) && !files.Any(f=>f.Path.Equals(raw,StringComparison.OrdinalIgnoreCase)))list.Insert(0,OpenItem(System.IO.Path.GetFileName(raw),raw,"▤",raw,"files"));}
    else if(q.Length==0)foreach(var recent in UsageHistory.Recent(store.Data,"files").Where(e=>File.Exists(e.Target)||Directory.Exists(e.Target)))list.Add(OpenItem(recent.Title,"Arquivo recente · "+recent.Target,"▤",recent.Target,"files"));
   }
   if(mode=="" || mode=="web")
   {
    var web=raw;foreach(var prefix in new[]{"pesquisar ","pesquise ","buscar ","busque "})if(Commands.Normalize(web).StartsWith(prefix)){web=web[prefix.Length..];break;}
    if(raw.Length>0 || mode=="web")list.Add(Action(raw.Length>0?"Pesquisar na Internet":"Abrir Internet na barra",store.Data.SearchEngine+" · Microsoft Edge WebView2","◎",()=>ShowPanel("internet",web),category:"web",id:"default-browser"));
    if(mode=="web")foreach(var browser in browsers){var chosen=browser;list.Add(Action(chosen.Name,web.Length>0?"Pesquisar por “"+web+"” neste navegador":"Navegador disponível · Abrir","◎",()=>{if(web.Length==0)Commands.Open(chosen.Executable);else{var settings=new Settings{Browser=chosen.Executable,SearchEngine=store.Data.SearchEngine};SystemIntegration.Search(settings,web);}Dismiss();},chosen.Executable,"web"));}
   }
   if(version!=request || token.IsCancellationRequested || disposed)return;var ranked=ResultRanking.Apply(list,store.Data,Category(mode),q.Length>0);results.ItemsSource=ranked;results.SelectedIndex=ranked.Count>0?0:-1;status.Text=(ranked.Count>0?$"{ranked.Count} itens · ":"")+$"{apps.Count:N0} apps · {index.Status} · {store.Data.Hotkey}";foreach(var result in ranked)_=result.LoadIcon();
  }catch(OperationCanceledException){}catch(Exception ex){if(version==request)status.Text="Pesquisa: "+ex.Message;}
 }
 private Task Execute()=>results.SelectedItem is Result result?ExecuteResult(result):Task.CompletedTask;
 private async Task ExecuteResult(Result result)
 {
  if(busy)return;busy=true;try{await result.Run();if(store.Data.HistoryEnabled && result.Category is not ("janelas" or "panel")){UsageHistory.Record(store.Data,result.Category,result.Id,result.Title,result.Target);try{store.Save();}catch(IOException){status.Text="Ação executada; histórico não foi salvo.";}}}catch(Exception ex){status.Text=ex.Message;notifications.Show("Ação não concluída",ex.Message);}finally{busy=false;}
 } internal void Configure(){if(configuring)return;configuring=true;Dismiss();try{new SettingsWindow(store,index,apps,ApplySettings,chord=>hotkeys?.Probe(chord)??"Atalho não inicializado",notifications).ShowDialog();}finally{configuring=false;}Refresh();}
 private string? ApplySettings(Settings draft)
 {
  var next=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(draft))!;var previous=store.Data;next.History=previous.History;
  if(!string.IsNullOrWhiteSpace(next.Browser) && !File.Exists(next.Browser))return "O navegador selecionado não existe.";
  if(next.Shortcuts.Any(b=>string.IsNullOrWhiteSpace(b.Target)||string.IsNullOrWhiteSpace(b.Name)))return "Preencha nome e destino de todos os atalhos.";
  var error=hotkeys?.Configure(next,()=>Toggle(),RunTarget);if(error!=null){hotkeys?.Configure(previous,()=>Toggle(),RunTarget);return error;}
  try{SystemIntegration.ApplyBehavior(next);store.Data=next;store.Save();HotkeyRegistered=true;ApplyAppearance();if(!next.IndexFiles)index.Cancel();Refresh();return null;}
  catch(Exception ex){store.Data=previous;hotkeys?.Configure(previous,()=>Toggle(),RunTarget);try{SystemIntegration.ApplyBehavior(previous);}catch{}return ex.Message;}
 }
 internal async Task<bool> TestInteraction()
 {
  var valid=true;var hideOnBlur=store.Data.HideOnBlur;
  void Check(bool ok,string name){Console.WriteLine((ok?"PASS ":"FAIL ")+name);valid&=ok;}
  Toggle(true);await Task.Delay(250);Check(input.IsKeyboardFocused,"input focused without mouse click");Check(Height<250 && results.Visibility==Visibility.Collapsed,"compact opening without empty panel");store.Data.HideOnBlur=false;
  var until=DateTime.UtcNow.AddSeconds(20);while(apps.Count==0 && DateTime.UtcNow<until)await Task.Delay(100);
  SelectFilter("app");await Task.Delay(200);Check(results.Items.Count>0 && results.Items.Cast<Result>().All(r=>r.Category=="apps"),"apps filter populated and isolated");
  var sample=results.Items.Cast<Result>().FirstOrDefault();if(sample!=null){await sample.LoadIcon();Check(sample.Bitmap!=null,"real shell icon for app result");}
  var sampleBrowser=browsers.FirstOrDefault();if(sampleBrowser!=null){var browserIcon=await ShellIcons.Get(sampleBrowser.Executable);Check(browserIcon!=null,"real browser executable icon");}
  Check(apps.Select(a=>AppDiscovery.CanonicalName(a.Name)).Distinct().Count()==apps.Count,"no duplicate browser aliases in Apps");
  SelectFilter("config");await Task.Delay(150);Check(results.Items.Count>=70 && results.Items.Cast<Result>().All(r=>r.Category=="windows"),"Windows filter includes all actions only");
  SelectFilter("arquivo");await Task.Delay(300);Check(widgets.Visibility==Visibility.Visible,"file filter shows storage cards");
  SelectFilter("web");await Task.Delay(200);Check(results.Items.Count>0 && results.Items.Cast<Result>().All(r=>r.Category=="web"),"web filter lists browsers immediately");
  SelectFilter("");input.Text="memória";await Task.Delay(250);Check(widgets.Visibility==Visibility.Visible,"hardware query shows live resource widget");
  input.Text="sqrt(144)";await Task.Delay(250);Check(results.Items.Cast<Result>().Any(r=>r.Category=="calculator"&&r.Title=="12"),"calculator remains available");
  input.Text="volme";await Task.Delay(250);Check(results.Items.Cast<Result>().Any(r=>r.Target=="ms-settings:sound"),"typo-tolerant search");
  input.Text="desligar em 30 min";await Task.Delay(250);Check(results.Items.Cast<Result>().FirstOrDefault()?.Target=="action:shutdown-in:1800","scheduled shutdown is interpreted (not executed)");
  input.Text="saída de áudio";await Task.Delay(300);Check(results.Items.Cast<Result>().Any(r=>r.Target.StartsWith("action:audio-default:")),"audio outputs listed");
  input.Clear();SelectFilter("janela");await Task.Delay(300);Check(results.Items.Cast<Result>().All(r=>r.Category=="janelas"),"windows filter lists only open windows");SelectFilter("");
  // Barras: só lê o valor; não altera o volume real.
  input.Text="volume";await Task.Delay(300);var volumeBar=results.Items.Cast<Result>().FirstOrDefault(r=>r.Target=="action:volume-bar");
  Check(volumeBar!=null && volumeBar.HasSlider && (int)volumeBar.SliderValue==Audio.Volume(),"volume shows a live slider with the real level");
  Check(!results.Items.Cast<Result>().Any(r=>r.Target is "action:volumeup" or "action:volumedown"),"slider replaces increase/decrease results");
  Snapshot("volume");
  input.Text="microfone";await Task.Delay(300);Check(results.Items.Cast<Result>().Any(r=>r.Target=="action:mic-bar" && r.HasSlider),"microphone level slider");
  // Setas: → no fim do texto troca de aba e mantém a pesquisa.
  input.Text="obs";input.CaretIndex=3;await Task.Delay(200);MoveFilter(1);await Task.Delay(250);Check(selectedMode=="app" && input.Text=="obs","arrow switches to next tab keeping the query");
  MoveFilter(1);await Task.Delay(400);Check(selectedMode=="arquivo","arrow reaches files tab");
  var file=results.Items.Cast<Result>().FirstOrDefault(r=>r.Category=="files");
  if(file!=null){await file.LoadIcon();Check(file.IconSize>=48 && file.Bitmap!=null,"file results show a larger thumbnail");}
  until=DateTime.UtcNow.AddSeconds(3);while(DateTime.UtcNow<until && results.Items.Cast<Result>().Any(r=>r.Category=="files" && r.Bitmap==null))await Task.Delay(100);
  Snapshot("arquivos-obs");
  MoveFilter(-1);MoveFilter(-1);await Task.Delay(200);Check(selectedMode=="","arrow returns to the first tab");MoveFilter(-1);Check(selectedMode==FilterOrder[^1],"tabs wrap around");SelectFilter("");
  store.Data.HideOnBlur=hideOnBlur;return valid;
 }
 // Captura opcional da janela para revisão visual (definir BARRA_SNAPSHOTS com uma pasta).
 private void Snapshot(string name)
 {
  var folder=Environment.GetEnvironmentVariable("BARRA_SNAPSHOTS");if(string.IsNullOrEmpty(folder) || Content is not FrameworkElement root)return;
  UpdateLayout();var dpi=VisualTreeHelper.GetDpi(this);var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)(root.ActualWidth*dpi.DpiScaleX),(int)(root.ActualHeight*dpi.DpiScaleY),96*dpi.DpiScaleX,96*dpi.DpiScaleY,PixelFormats.Pbgra32);bitmap.Render(root);
  var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));Directory.CreateDirectory(folder);using var stream=File.Create(System.IO.Path.Combine(folder,name+".png"));encoder.Save(stream);
 }
 internal bool TestSettingsPages(){configuring=true;try{var settings=new SettingsWindow(store,index,apps,ApplySettings,chord=>hotkeys?.Probe(chord)??"",notifications);settings.Show();var count=settings.ValidatePages();settings.Close();return count==9;}finally{configuring=false;}}
 internal bool TestHotkeyToggle(){if(hotkeys==null)return false;var id=hotkeys.PrimaryId;Native.SendMessage(hwnd,0x312,new IntPtr(id),IntPtr.Zero);var hidden=!IsVisible;Native.SendMessage(hwnd,0x312,new IntPtr(id),IntPtr.Zero);return hidden && IsVisible;}
 internal void Quit(){if(disposed)return;disposed=true;exit=true;search?.Cancel();updateTimer.Stop();index.Changed-=OnIndexChanged;index.Dispose();widgets.Dispose();panels.Dispose();hotkeys?.Dispose();manager.Dispose();notifications.Close();tray.Dispose();Close();Application.Current.Shutdown();}
}
internal static class Program
{
 [STAThread] private static void Main(string[] args)
 {
  using var mutex=new Mutex(true,@"Local\ActionBar.Session",out var fresh);if(!fresh){if(args.Contains("--smoke-test")){Console.WriteLine("FAIL instância já em execução");Environment.ExitCode=1;}else MessageBox.Show("O ActionBar já está em execução. Use o atalho ou o ícone na bandeja.");return;}
  var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown,ThemeMode=ThemeMode.Dark};var smoke=args.Contains("--smoke-test");
  app.DispatcherUnhandledException+=(_,e)=>{if(smoke){Console.WriteLine("FAIL "+e.Exception);app.Shutdown(1);}else MessageBox.Show(e.Exception.Message,"ActionBar");e.Handled=true;};
  var store=new Store();if(store.LoadError!=null && !smoke)MessageBox.Show(store.LoadError+"\nO arquivo será preservado até você salvar.");
  if(args.Contains("--ui-review"))store.Data.HideOnBlur=false;
  SystemIntegration.ApplyPriority(store.Data);var launcher=new Launcher(store);app.MainWindow=launcher;if(args.Contains("--ui-review"))launcher.ShowInTaskbar=true;
  var source=new WindowInteropHelper(launcher).EnsureHandle();if(smoke || !args.Contains("--startup") || store.Data.StartupMode=="Barra aberta")launcher.Toggle(true);
  if(smoke){var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(3)};timer.Tick+=async(_,_)=>{timer.Stop();var visible=launcher.IsVisible && Native.IsWindowVisible(source);Console.WriteLine(visible?"PASS launcher startup and visible native window":"FAIL launcher window");var shortcut=launcher.HotkeyRegistered && launcher.TestHotkeyToggle();Console.WriteLine(shortcut?"PASS global hotkey registration and window toggle":"FAIL hotkey");if(args.Contains("--interaction-test")){var interaction=await launcher.TestInteraction();visible&=interaction;}if(args.Contains("--settings-render-test")){var pages=launcher.TestSettingsPages();Console.WriteLine(pages?"PASS all nine settings pages rendered":"FAIL settings pages");visible&=pages;}if(args.Contains("--settings-test")){launcher.Configure();Console.WriteLine("PASS settings window closed");}Environment.ExitCode=visible && shortcut?0:1;launcher.Quit();};timer.Start();}
  app.Run();
 }
}













