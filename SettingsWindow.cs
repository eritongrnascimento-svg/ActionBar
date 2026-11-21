using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
namespace BarraInteligente;
internal sealed class SettingsWindow : Window
{
 private readonly Store store;private readonly Settings draft;private readonly FileIndex index;private readonly IReadOnlyList<AppEntry> apps;private readonly Func<Settings,string?> commit;private readonly Func<string,string> probe;private readonly NotificationCenter notifications;
 private readonly ContentControl page=new();private readonly TextBlock feedback=new();private readonly ListBox navigation=new();private readonly List<Action> pages=new();private TextBlock indexStatus=new();
 internal SettingsWindow(Store store,FileIndex index,IReadOnlyList<AppEntry> apps,Func<Settings,string?> commit,Func<string,string> probe,NotificationCenter notifications)
 {
  this.store=store;this.index=index;this.apps=apps;this.commit=commit;this.probe=probe;this.notifications=notifications;draft=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(store.Data))!;
  Title="Configurações · ActionBar";Icon=Brand.WindowIcon;Width=1080;Height=790;MinWidth=840;MinHeight=600;WindowStartupLocation=WindowStartupLocation.CenterScreen;
  SourceInitialized+=(_,_)=>Visuals.Theme(this,draft.Appearance);Loaded+=(_,_)=>Visuals.Open(this,draft.Appearance);
  var root=new DockPanel{Margin=new Thickness(22)};Content=root;
  var footer=new DockPanel{Margin=new Thickness(0,16,0,0)};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
  var save=new Button{Content="Salvar e aplicar",Padding=new Thickness(24,10,24,10),HorizontalAlignment=HorizontalAlignment.Right};save.Click+=(_,_)=>Save();DockPanel.SetDock(save,Dock.Right);footer.Children.Add(save);feedback.Text="As alterações são aplicadas ao salvar.";feedback.TextWrapping=TextWrapping.Wrap;feedback.VerticalAlignment=VerticalAlignment.Center;footer.Children.Add(feedback);
  var header=new StackPanel{Margin=new Thickness(10,0,0,24)};header.Children.Add(new TextBlock{Text="ActionBar",FontSize=28,FontWeight=FontWeights.SemiBold});header.Children.Add(new TextBlock{Text="Pesquisa, ações e espaços de trabalho — em um só lugar",Opacity=.65,Margin=new Thickness(0,6,0,0)});DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
  navigation.Width=210;navigation.Margin=new Thickness(0,0,24,0);navigation.BorderThickness=new Thickness(0);navigation.Background=Brushes.Transparent;DockPanel.SetDock(navigation,Dock.Left);root.Children.Add(navigation);root.Children.Add(page);
  AddPage("⌕  Pesquisa e arquivos",SearchPage);AddPage("◎  Pesquisa na web",WebPage);AddPage("◈  Aparência",AppearancePage);AddPage("♧  Notificações",NotificationsPage);AddPage("∑  Calculadora",CalculatorPage);AddPage("⌨  Atalhos e ações",ShortcutsPage);AddPage("▦  Agrupamento de janelas",GroupingPage);AddPage("⚙  Comportamento",BehaviorPage);AddPage("ⓘ  Sobre",AboutPage);
  navigation.SelectionChanged+=(_,_)=>{if(navigation.SelectedIndex>=0){pages[navigation.SelectedIndex]();if(draft.Appearance.Animations && page.Content is FrameworkElement p){p.BeginAnimation(UIElement.OpacityProperty,new System.Windows.Media.Animation.DoubleAnimation(0,1,TimeSpan.FromMilliseconds(150)));}}};navigation.SelectedIndex=0;
  index.Changed+=IndexChanged;Closed+=(_,_)=>index.Changed-=IndexChanged;
 }
 internal int ValidatePages(){for(var i=0;i<pages.Count;i++){navigation.SelectedIndex=i;UpdateLayout();}return pages.Count;}
 private void AddPage(string name,Action build){navigation.Items.Add(new ListBoxItem{Content=name,Padding=new Thickness(10,13,10,13)});pages.Add(build);}
 private StackPanel Page(string title,string description)
 {
  var stack=new StackPanel{Margin=new Thickness(3,0,14,16)};stack.Children.Add(new TextBlock{Text=title,FontSize=25,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,8)});stack.Children.Add(new TextBlock{Text=description,TextWrapping=TextWrapping.Wrap,Opacity=.7,Margin=new Thickness(0,0,0,22)});page.Content=new ScrollViewer{Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};return stack;
 }
 private StackPanel Card(StackPanel parent,string title,string? note=null)
 {
  var content=new StackPanel();content.Children.Add(new TextBlock{Text=title,FontWeight=FontWeights.SemiBold,FontSize=16,Margin=new Thickness(0,0,0,12)});if(note!=null)content.Children.Add(new TextBlock{Text=note,TextWrapping=TextWrapping.Wrap,Opacity=.65,Margin=new Thickness(0,0,0,12)});
  parent.Children.Add(new Border{Child=content,Padding=new Thickness(18),Margin=new Thickness(0,0,0,14),CornerRadius=new CornerRadius(10),BorderBrush=new SolidColorBrush(Color.FromArgb(65,140,150,170)),BorderThickness=new Thickness(1)});return content;
 }
 private static TextBlock Label(string text)=>new(){Text=text,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,7,0,5),Opacity=.8};
 private CheckBox Check(StackPanel parent,string text,bool value,Action<bool> set){var control=new CheckBox{Content=text,IsChecked=value,Margin=new Thickness(0,6,0,10)};control.Click+=(_,_)=>set(control.IsChecked==true);parent.Children.Add(control);return control;}
 private ComboBox Select(StackPanel parent,string label,IEnumerable<string> values,string value,Action<string> set)
 {parent.Children.Add(Label(label));var combo=new ComboBox{ItemsSource=values.ToArray(),SelectedItem=value,Margin=new Thickness(0,0,0,10),MinWidth=160,HorizontalAlignment=HorizontalAlignment.Stretch};combo.SelectionChanged+=(_,_)=>{if(combo.SelectedItem is string s)set(s);};parent.Children.Add(combo);return combo;}
 private TextBox Text(StackPanel parent,string label,string value,Action<string> set)
 {parent.Children.Add(Label(label));var field=new TextBox{Text=value,Padding=new Thickness(9),Margin=new Thickness(0,0,0,10)};field.TextChanged+=(_,_)=>set(field.Text);parent.Children.Add(field);return field;}
 private void Slider(StackPanel parent,string label,double value,double min,double max,Action<double> set)
 {var title=Label(label+": "+value.ToString("0.##"));parent.Children.Add(title);var slider=new Slider{Value=value,Minimum=min,Maximum=max,TickFrequency=1,IsSnapToTickEnabled=max-min>10,Margin=new Thickness(0,6,0,14)};slider.ValueChanged+=(_,_)=>{title.Text=label+": "+slider.Value.ToString("0.##");set(slider.Value);};parent.Children.Add(slider);}
 private Button Button(StackPanel parent,string title,Action action){var button=new Button{Content=title,Padding=new Thickness(12,8,12,8),HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,5,8,8)};button.Click+=(_,_)=>{try{action();}catch(Exception ex){feedback.Text=ex.Message;}};parent.Children.Add(button);return button;}
 private bool Save(){try{var error=commit(draft);feedback.Text=error??"Configurações salvas e aplicadas.";return error==null;}catch(Exception ex){feedback.Text="Não foi possível salvar: "+ex.Message;return false;}}
 private void IndexChanged(){Dispatcher.BeginInvoke(()=>indexStatus.Text=index.Status);}
 private void SearchPage()
 {
  var panel=Page("Pesquisa e arquivos","Encontre documentos, pastas e programas pelo nome. A indexação roda em segundo plano e permanece armazenada neste diretório.");
  var card=Card(panel,"Fontes da pesquisa");Check(card,"Pesquisar arquivos e pastas",draft.IndexFiles,v=>draft.IndexFiles=v);Check(card,"Atualizar o índice ao iniciar e a cada 15 minutos",draft.AutoIndex,v=>draft.AutoIndex=v);
  indexStatus=new TextBlock();indexStatus.Text=index.Status;indexStatus.Margin=new Thickness(0,8,0,8);card.Children.Add(indexStatus);
  var roots=Card(panel,"Unidades e pastas indexadas","Marque unidades inteiras ou adicione pastas específicas. Diretórios protegidos, links simbólicos e pastas de sistema são ignorados.");
  foreach(var drive in DriveInfo.GetDrives()){var root=drive.RootDirectory.FullName;Check(roots,root+"  ·  "+(drive.DriveType==DriveType.Fixed?"Unidade fixa":drive.DriveType==DriveType.Removable?"Removível":drive.DriveType.ToString()),draft.IndexRoots.Contains(root,StringComparer.OrdinalIgnoreCase),v=>{if(v){if(!draft.IndexRoots.Contains(root,StringComparer.OrdinalIgnoreCase))draft.IndexRoots.Add(root);}else draft.IndexRoots.RemoveAll(p=>p.Equals(root,StringComparison.OrdinalIgnoreCase));});}
  var custom=new ListBox{ItemsSource=draft.IndexRoots.ToArray(),MaxHeight=130,Margin=new Thickness(0,8,0,8)};roots.Children.Add(custom);
  Button(roots,"Adicionar pasta…",()=>{var dialog=new OpenFolderDialog{Title="Escolha uma pasta para indexar"};if(dialog.ShowDialog(this)==true){if(!draft.IndexRoots.Contains(dialog.FolderName,StringComparer.OrdinalIgnoreCase))draft.IndexRoots.Add(dialog.FolderName);custom.ItemsSource=draft.IndexRoots.ToArray();}});
  Button(roots,"Remover caminho selecionado",()=>{if(custom.SelectedItem is string p){draft.IndexRoots.Remove(p);SearchPage();}});
  Slider(roots,"Limite de itens",draft.IndexLimit,10000,1000000,v=>draft.IndexLimit=(int)v);
  Button(roots,"Salvar e reconstruir índice",()=>{if(Save())_=index.Rebuild();});Button(roots,"Interromper indexação",index.Cancel);
  BrowseSettings(panel);
  var programs=Card(panel,"Programas disponíveis",$"{apps.Count:N0} entradas encontradas em menu Iniciar, registro de instalação e aplicativos do Windows / Store. Executáveis nas unidades selecionadas também aparecem na pesquisa de arquivos.");
  programs.Children.Add(Label("Use app: nome para filtrar programas; arquivo: nome para filtrar arquivos; config: nome para pesquisar configurações."));
 }
 private void BrowseSettings(StackPanel panel)
 {
  var browser=Card(panel,"Aba Pastas","Escolha onde a aba abre e quais atalhos de unidades aparecem. Essas opções são independentes do índice.");
  var folder=Text(browser,"Pasta inicial (em branco usa a pasta do usuário)",draft.DefaultBrowseFolder,v=>draft.DefaultBrowseFolder=v);
  Button(browser,"Escolher pasta inicial…",()=>{
   var picker=new OpenFolderDialog{Title="Pasta inicial da aba Pastas"};
   if(picker.ShowDialog(this)==true)folder.Text=picker.FolderName;
  });
  var drives=new StackPanel();
  Check(browser,"Mostrar todas as unidades disponíveis",draft.ShowAllBrowseDrives,v=>{draft.ShowAllBrowseDrives=v;drives.IsEnabled=!v;});
  drives.IsEnabled=!draft.ShowAllBrowseDrives;
  browser.Children.Add(drives);
  foreach(var drive in DriveInfo.GetDrives())
  {
   var root=drive.Name;
   Check(drives,root,draft.BrowseDriveRoots.Contains(root,StringComparer.OrdinalIgnoreCase),v=>{
    draft.BrowseDriveRoots.RemoveAll(path=>path.Equals(root,StringComparison.OrdinalIgnoreCase));
    if(v)draft.BrowseDriveRoots.Add(root);
   });
  }
 }
 private void WebPage()
 {
  var panel=Page("Pesquisa na web","Escolha onde a pesquisa será aberta. Essa opção vale apenas para a barra e não altera o navegador padrão do Windows.");var card=Card(panel,"Navegador e mecanismo");
  Select(card,"Mecanismo de pesquisa",new[]{"Google","Bing","DuckDuckGo"},draft.SearchEngine,v=>draft.SearchEngine=v);
  var path=Text(card,"Executável do navegador — deixe vazio para usar o padrão do Windows",draft.Browser,v=>draft.Browser=v.Trim().Trim('"'));
  Button(card,"Usar padrão do Windows",()=>path.Text="");Button(card,"Escolher navegador…",()=>{var dialog=new OpenFileDialog{Filter="Programas (*.exe)|*.exe",Title="Escolha o navegador"};if(dialog.ShowDialog(this)==true)path.Text=dialog.FileName;});
  var browsers=Card(panel,"Navegadores detectados");
  foreach(var app in apps.Where(a=>new[]{"chrome","firefox","msedge","brave","opera","vivaldi","zen"}.Any(n=>System.IO.Path.GetFileNameWithoutExtension(a.Target).Equals(n,StringComparison.OrdinalIgnoreCase)) && File.Exists(a.Target)).DistinctBy(a=>a.Target))Button(browsers,app.Name,()=>path.Text=app.Target);
  var climate=Card(panel,"Clima na aba Web","Localização automática do Windows; quando indisponível, estimativa da região pela conexão (IPWho.is). As coordenadas aproximadas são consultadas no Open-Meteo. Nenhuma permissão do Windows é alterada.");Check(climate,"Exibir widget de clima na aba Web",draft.WeatherEnabled,v=>draft.WeatherEnabled=v);
  Button(card,"Testar pesquisa",()=>SystemIntegration.Search(draft,"ActionBar Windows"));
 }
 private void AppearancePage()
 {
  var panel=Page("Aparência","Controles Fluent, vidro do Windows e uma barra com a sua identidade. As animações respeitam a preferência de movimento do sistema.");var a=draft.Appearance;var card=Card(panel,"Tema e tipografia");
  Select(card,"Tema",new[]{"Escuro","Claro","Sistema"},a.Theme,v=>{a.Theme=v;Visuals.Theme(this,a);});Check(card,"Vidro translúcido (Windows 11 compatível)",a.Glass,v=>{a.Glass=v;Visuals.Theme(this,a);});
  Select(card,"Fonte",Fonts.SystemFontFamilies.Select(f=>f.Source).OrderBy(x=>x).Distinct(),a.Font,v=>a.Font=v);Slider(card,"Tamanho do texto de pesquisa",a.FontSize,16,36,v=>a.FontSize=v);
  var size=Card(panel,"Tamanho e animação");Slider(size,"Largura da barra",a.Width,520,1200,v=>a.Width=v);Slider(size,"Altura da barra",a.Height,360,850,v=>a.Height=v);Check(size,"Animar abertura e troca de páginas",a.Animations,v=>a.Animations=v);Slider(size,"Duração da animação (ms)",a.AnimationMs,80,500,v=>a.AnimationMs=(int)v);Button(size,"Prévia da animação",()=>Visuals.Open(this,a));
  var image=Card(panel,"Ícone e imagem de fundo");Select(image,"Ícone da pesquisa",new[]{"⌕","🔎","◈","Nenhum"},a.SearchIcon,v=>a.SearchIcon=v);var path=Text(image,"Imagem de fundo",a.BackgroundImage,v=>a.BackgroundImage=v);Button(image,"Escolher imagem…",()=>{var dialog=new OpenFileDialog{Filter="Imagens|*.png;*.jpg;*.jpeg;*.bmp;*.webp"};if(dialog.ShowDialog(this)==true)path.Text=dialog.FileName;});Button(image,"Remover imagem",()=>path.Text="");Slider(image,"Opacidade da imagem",a.ImageOpacity,0,1,v=>a.ImageOpacity=v);
 }
 private void NotificationsPage()
 {
  var panel=Page("Notificações","Balões do ActionBar para resultados de ações, workspaces e avisos. As notificações de outros aplicativos continuam sendo gerenciadas pelo Windows.");var card=Card(panel,"Balões animados");Check(card,"Exibir notificações da barra",draft.Notifications,v=>draft.Notifications=v);Check(card,"Animar entrada e saída",draft.NotificationAnimation,v=>draft.NotificationAnimation=v);Select(card,"Posição",new[]{"Inferior direita","Inferior esquerda","Superior direita","Superior esquerda"},draft.NotificationPosition,v=>draft.NotificationPosition=v);Slider(card,"Duração em segundos",draft.NotificationSeconds,2,15,v=>draft.NotificationSeconds=(int)v);Button(card,"Mostrar uma prévia",()=>notifications.Show("Tudo no lugar","Seu workspace foi organizado. Este é um exemplo de notificação da barra.",true,draft));Button(card,"Abrir notificações do Windows",()=>Commands.Open("ms-settings:notifications"));
 }
 private void CalculatorPage()
 {
  var panel=Page("Calculadora inteligente","Resolva expressões diretamente na barra e pressione Enter para copiar o resultado.");var card=Card(panel,"Cálculo local");Check(card,"Habilitar calculadora na barra",draft.Calculator,v=>draft.Calculator=v);card.Children.Add(Label("Exemplos: 12 * (8 + 2), 200 * 15%, 2^8, sqrt(144), sin(pi/2), log(1000). Também converte unidades: 10 km em m, 2 horas em minutos, 32 f em c, 1 gb em mb e 15% de 200. Aceita vírgula decimal. Funções trigonométricas usam radianos."));
  var output=Label("Digite uma expressão para testar");var field=Text(card,"Teste da calculadora","",v=>output.Text=Calculator.Try(v,out var result)?"Resultado: "+result:"Expressão incompleta ou inválida");card.Children.Add(output);
 }
 private TextBox Chord(StackPanel card,string label,string value,Action<string> set)
 {
  var field=Text(card,label,value,set);field.ToolTip="Clique e pressione a combinação desejada, ou digite Ctrl+Alt+K.";
  field.PreviewKeyDown+=(_,e)=>{var key=e.Key==Key.System?e.SystemKey:e.Key;if(key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift)return;var mods=Keyboard.Modifiers;if(mods==ModifierKeys.None || key is Key.Tab or Key.Escape)return;var parts=new List<string>();if(mods.HasFlag(ModifierKeys.Control))parts.Add("Ctrl");if(mods.HasFlag(ModifierKeys.Alt))parts.Add("Alt");if(mods.HasFlag(ModifierKeys.Shift))parts.Add("Shift");if(mods.HasFlag(ModifierKeys.Windows))parts.Add("Win");parts.Add(key.ToString());field.Text=string.Join("+",parts);e.Handled=true;};return field;
 }
 private void ShortcutsPage()
 {
  var panel=Page("Atalhos e ações","Crie atalhos globais para aplicativos, páginas de configuração e serviços. A disponibilidade de cada combinação é verificada antes de aplicar.");var main=Card(panel,"Abrir a barra");var primary=Chord(main,"Atalho global",draft.Hotkey,v=>draft.Hotkey=v);Button(main,"Testar disponibilidade",()=>feedback.Text=probe(primary.Text));
  var reserved=Card(panel,"Atalhos do Windows","O Windows não oferece uma lista completa dos atalhos registrados por outros processos. A barra mostra os atalhos conhecidos e testa o registro das combinações personalizadas. Combinações Win são reservadas ao sistema.");reserved.Children.Add(Label("Win+I · Configurações\nWin+E · Explorador\nWin+A · Ações rápidas\nWin+N · Notificações\nWin+L · Bloquear\nWin+V · Área de transferência\nCtrl+Shift+Esc · Gerenciador de Tarefas\nAlt+Tab · Alternar janelas"));
  var bindings=Card(panel,"Atalhos personalizados");foreach(var binding in draft.Shortcuts.ToArray())
  {var row=Card(bindings,string.IsNullOrWhiteSpace(binding.Name)?"Novo atalho":binding.Name);Text(row,"Nome",binding.Name,v=>binding.Name=v);Chord(row,"Combinação",binding.Chord,v=>binding.Chord=v);var target=Text(row,"Destino: programa, URI ou service:start:Nome / service:stop:Nome",binding.Target,v=>binding.Target=v);TargetChooser(row,target);Button(row,"Testar disponibilidade",()=>feedback.Text=probe(binding.Chord));Button(row,"Remover atalho",()=>{draft.Shortcuts.Remove(binding);ShortcutsPage();});}
  Button(bindings,"Adicionar atalho",()=>{draft.Shortcuts.Add(new ShortcutBinding{Name="Novo atalho",Chord="Ctrl+Alt+K",Target="ms-settings:"});ShortcutsPage();});
 }
 private void TargetChooser(StackPanel parent,TextBox target)
 {
  var choose=new ComboBox{ItemsSource=Catalog.Actions,DisplayMemberPath="Title",Margin=new Thickness(0,0,0,8)};choose.SelectionChanged+=(_,_)=>{if(choose.SelectedItem is SystemAction action)target.Text=action.Target;};parent.Children.Add(Label("Ou escolha uma ação do Windows"));parent.Children.Add(choose);
  Button(parent,"Escolher programa…",()=>{var dialog=new OpenFileDialog{Filter="Programas|*.exe;*.lnk;*.msc;*.cpl|Todos os arquivos|*.*"};if(dialog.ShowDialog(this)==true)target.Text=dialog.FileName;});
 }
 private void GroupingPage()
 {
  var panel=Page("Agrupamento de janelas","Seis modelos fixos, com aplicativos atribuídos a cada posição. Grupos salvos aparecem na busca e respeitam a exceção de centralização.");
  var saved=Card(panel,"Workspaces salvos");var list=new ListBox{ItemsSource=draft.Workspaces.ToArray(),DisplayMemberPath="Name",MaxHeight=140};saved.Children.Add(list);Button(saved,"Excluir selecionado",()=>{if(list.SelectedItem is Workspace ws){draft.Workspaces.Remove(ws);GroupingPage();}});
  var group=Card(panel,"Criar grupo por posições");var groupName="Meu grupo";Text(group,"Nome do grupo",groupName,v=>groupName=v);
  var preset=new ComboBox{ItemsSource=Layouts.Names,SelectedIndex=0,Margin=new Thickness(0,0,0,10)};group.Children.Add(Label("Modelo de agrupamento"));group.Children.Add(preset);
  var screens=System.Windows.Forms.Screen.AllScreens;var monitor=new ComboBox{ItemsSource=screens.Select((s,i)=>$"Monitor {i+1} · {s.Bounds.Width} × {s.Bounds.Height}").ToArray(),SelectedIndex=0};group.Children.Add(Label("Monitor de destino"));group.Children.Add(monitor);
  var slots=new StackPanel{Margin=new Thickness(0,16,0,0)};group.Children.Add(slots);var values=new List<(ComboBox App,TextBox Arguments)>();
  void BuildSlots(){slots.Children.Clear();values.Clear();var cells=Layouts.Cells(Math.Max(0,preset.SelectedIndex),new System.Drawing.Rectangle(0,0,600,220));var preview=new Canvas{Height=230,ClipToBounds=true};slots.Children.Add(preview);for(int i=0;i<cells.Count;i++){var r=cells[i];var b=new Border{Width=r.Width-5,Height=r.Height-5,CornerRadius=new CornerRadius(5),BorderThickness=new Thickness(1),BorderBrush=SystemColors.HighlightBrush,Background=new SolidColorBrush(Color.FromArgb(30,90,160,250)),Child=new TextBlock{Text=(i+1).ToString(),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,FontSize=20}};Canvas.SetLeft(b,r.Left);Canvas.SetTop(b,r.Top);preview.Children.Add(b);var combo=new ComboBox{ItemsSource=apps,DisplayMemberPath="Name",IsEditable=true,IsTextSearchEnabled=true,Margin=new Thickness(0,4,0,6)};slots.Children.Add(Label("Posição "+(i+1)+" — escolha ou digite o caminho do executável"));slots.Children.Add(combo);Button(slots,"Escolher executável…",()=>{var dialog=new OpenFileDialog{Filter="Programas|*.exe"};if(dialog.ShowDialog(this)==true){combo.SelectedItem=null;combo.Text=dialog.FileName;}});var arguments=new TextBox{Padding=new Thickness(7),Margin=new Thickness(0,0,0,10),ToolTip="Argumentos opcionais"};slots.Children.Add(arguments);values.Add((combo,arguments));}}
  preset.SelectionChanged+=(_,_)=>BuildSlots();BuildSlots();
  Button(group,"Salvar grupo",()=>{var area=screens[Math.Max(0,monitor.SelectedIndex)].WorkingArea;var cells=Layouts.Cells(preset.SelectedIndex,area);var ws=new Workspace{Name=groupName.Trim()};for(var i=0;i<values.Count;i++){var selected=values[i].App.SelectedItem as AppEntry;var path=selected?.Target??values[i].App.Text.Trim();if(string.IsNullOrWhiteSpace(path))continue;var resolved=Catalog.ResolveProgram(path);path=resolved.Path;if(path.StartsWith("shell:") || !path.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)){feedback.Text="Para posicionar janelas, escolha o executável .exe real do aplicativo. Atalhos e aplicativos de loja não identificam o processo de forma confiável.";return;}if(!File.Exists(path)){feedback.Text="Executável não encontrado: "+path;return;}var r=cells[i];ws.Apps.Add(new LayoutItem{Executable=path,Arguments=string.IsNullOrWhiteSpace(values[i].Arguments.Text)?resolved.Arguments:values[i].Arguments.Text,Title=selected?.Name??System.IO.Path.GetFileNameWithoutExtension(path),X=r.X,Y=r.Y,Width=r.Width,Height=r.Height});}if(ws.Name.Length==0 || ws.Apps.Count==0){feedback.Text="Informe um nome e escolha ao menos um aplicativo.";return;}var old=draft.Workspaces.FirstOrDefault(w=>w.Name.Equals(ws.Name,StringComparison.OrdinalIgnoreCase));if(old!=null)draft.Workspaces.Remove(old);draft.Workspaces.Add(ws);if(Save())GroupingPage();});
  var capture=Card(panel,"Capturar janelas já organizadas");var choices=new List<(CheckBox Check,LayoutItem Item)>();foreach(var h in Native.Windows()){if(Native.IsIconic(h))continue;var path=Native.Path(h);if(path.Length==0)continue;Native.GetWindowRect(h,out var r);var item=new LayoutItem{Executable=path,Title=Native.Title(h),X=r.Left,Y=r.Top,Width=r.Width,Height=r.Height,Maximized=Native.IsZoomed(h)};var check=Check(capture,item.Title,false,_=>{});choices.Add((check,item));}var captureName="Layout atual";Text(capture,"Nome do workspace",captureName,v=>captureName=v);Button(capture,"Salvar janelas selecionadas",()=>{var items=choices.Where(c=>c.Check.IsChecked==true).Select(c=>c.Item).ToList();if(items.Count==0 || string.IsNullOrWhiteSpace(captureName)){feedback.Text="Escolha um nome e selecione as janelas.";return;}draft.Workspaces.RemoveAll(w=>w.Name.Equals(captureName.Trim(),StringComparison.OrdinalIgnoreCase));draft.Workspaces.Add(new Workspace{Name=captureName.Trim(),Apps=items});if(Save())GroupingPage();});
 }
 private void BehaviorPage()
 {
  var panel=Page("Comportamento da barra","Controle como a barra inicia, aparece e se integra à sessão do Windows.");var startup=Card(panel,"Inicialização e processo");Check(startup,"Guardar histórico de itens usados",draft.HistoryEnabled,v=>draft.HistoryEnabled=v);Check(startup,"Iniciar com o Windows (usuário atual)",draft.StartWithWindows,v=>draft.StartWithWindows=v);Select(startup,"Modo de inicialização",new[]{"Bandeja","Barra aberta"},draft.StartupMode,v=>draft.StartupMode=v);Select(startup,"Prioridade do processo",new[]{"Baixa","Abaixo do normal","Normal","Acima do normal"},draft.Priority,v=>draft.Priority=v);
  var bar=Card(panel,"Ao usar a barra");Check(bar,"Manter a barra acima das janelas normais",draft.AlwaysOnTop,v=>draft.AlwaysOnTop=v);Check(bar,"Fechar ao perder o foco",draft.HideOnBlur,v=>draft.HideOnBlur=v);CenteringSettings(panel);
  Button(bar,"Abrir aplicativos de inicialização do Windows",()=>Commands.Open("ms-settings:startupapps"));
 }
 private void CenteringSettings(StackPanel panel)
 {
  const string all="Todas as elegíveis", selective="Todas, exceto os aplicativos selecionados", off="Desativado";
  var card=Card(panel,"Centralização de novas janelas");
  var exclusions=new StackPanel{IsEnabled=draft.CenterWindows&&draft.CenterWindowsSelective};
  Select(card,"Modo",new[]{all,selective,off},draft.CenterWindows?(draft.CenterWindowsSelective?selective:all):off,value=>{
   draft.CenterWindows=value!=off;draft.CenterWindowsSelective=value==selective;exclusions.IsEnabled=value==selective;
  });
  card.Children.Add(exclusions);
  var choices=apps.Select(app=>(Name:app.Name,Path:app.Executable.Length>0?app.Executable:app.Target))
    .Where(app=>System.IO.Path.IsPathFullyQualified(app.Path)&&app.Path.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))
    .DistinctBy(app=>app.Path,StringComparer.OrdinalIgnoreCase).OrderBy(app=>app.Name).ToArray();
  var picker=new ComboBox{ItemsSource=choices.Select(app=>new{app.Name,app.Path}).ToArray(),DisplayMemberPath="Name",SelectedValuePath="Path",MinWidth=220};
  picker.IsTextSearchEnabled=true;
  exclusions.Children.Add(picker);
  var list=new ListBox{ItemsSource=draft.CenterWindowExclusions.ToArray(),MaxHeight=150};
  void Add(string path){
   if (!draft.CenterWindowExclusions.Contains(path,StringComparer.OrdinalIgnoreCase))draft.CenterWindowExclusions.Add(path);
   list.ItemsSource=draft.CenterWindowExclusions.ToArray();
  }
  Button(exclusions,"Adicionar aplicativo selecionado",()=>{if(picker.SelectedValue is string path)Add(path);});
  Button(exclusions,"Escolher executável…",()=>{
   var file=new OpenFileDialog{Title="Aplicativo que não será centralizado",Filter="Programas|*.exe"};
   if(file.ShowDialog(this)==true)Add(file.FileName);
  });
  exclusions.Children.Add(list);
  Button(exclusions,"Remover exceção selecionada",()=>{
   if(list.SelectedItem is string path){draft.CenterWindowExclusions.Remove(path);list.ItemsSource=draft.CenterWindowExclusions.ToArray();}
  });
 }
 private void AboutPage()
 {
  var panel=Page("Sobre","ActionBar " + typeof(Launcher).Assembly.GetName().Version!.ToString(3) + " · Central de pesquisa e ações rápidas para Windows");var card=Card(panel,"Feito para o seu fluxo de trabalho");card.Children.Add(Label("Interface WPF Fluent no .NET 10, integração Win32, pesquisa local, calculadora e layouts de janelas. Os comandos são interpretados localmente, sem uma API de IA generativa."));card.Children.Add(Label("A indexação usa nomes e caminhos dos arquivos; não lê o conteúdo dos documentos. A busca web só é enviada ao navegador quando você executa o resultado de pesquisa."));card.Children.Add(Label("Aplicativos elevados, jogos e programas que usam processos intermediários podem limitar o posicionamento. Algumas páginas de configuração dependem da versão do Windows."));card.Children.Add(Label("Configurações: "+store.FilePath));Button(card,"Abrir pasta dos dados",()=>{Directory.CreateDirectory(System.IO.Path.GetDirectoryName(store.FilePath)!);Commands.Open(System.IO.Path.GetDirectoryName(store.FilePath)!);});
 }
}







