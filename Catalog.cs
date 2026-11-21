using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;
namespace BarraInteligente;
internal sealed record SystemAction(string Title,string Keywords,string Target,string Description="Abrir os controles desta configuração no Windows",string Glyph="⚙",Func<bool>? Available=null)
{
 internal bool IsAvailable{get{try{return Available?.Invoke()??true;}catch{return true;}}}
}
internal sealed record AppEntry(string Name,string Target,string Executable="",string Source="Aplicativo instalado");
internal static class Catalog
{
 // Catálogo completo (usado também na escolha de ações para atalhos personalizados).
 internal static readonly SystemAction[] Actions=Build();
 // Somente o que existe neste PC: sem Wi-Fi, Bluetooth ou segundo monitor, as ações correspondentes somem da pesquisa.
 internal static SystemAction[] Visible=>visible??=Actions.Where(a=>a.IsAvailable).ToArray();
 private static SystemAction[]? visible;
 static SystemAction[] Build()
 {
  var settings=new[]{
   "Tela e monitores|resolucao brilho escala hdr monitor|display","Som e dispositivos de áudio|volume microfone alto falante saida entrada|sound","Mixer de volume|volume aplicativos mixer|apps-volume","Bluetooth e dispositivos|ligar desligar bluetooth parear mouse teclado|bluetooth","Wi-Fi|wifi wireless redes conectar internet|network-wifi","Rede e Internet|rede conexao internet ethernet|network","Ethernet|cabo rede ethernet|network-ethernet","VPN|vpn rede privada|network-vpn","Modo avião|aviao radios wireless|network-airplanemode","Proxy|proxy internet|network-proxy","Hotspot móvel|hotspot compartilhar internet|network-mobilehotspot","Configurações avançadas de rede|adaptadores rede ip dns|network-advancedsettings",
   "Notificações do Windows|notificacao baloes alertas nao perturbe|notifications","Energia e bateria|bateria energia suspensao tela desempenho|powersleep","Armazenamento|disco espaco arquivos temporarios limpeza|storagesense","Multitarefa|snap encaixe janelas areas de trabalho|multitasking","Área de transferência|clipboard historico copiar colar|clipboard","Sobre este computador|hardware memoria ram processador nome pc|about","Exibição avançada|hz frequencia monitor|display-advanced",
   "Personalização|personalizar cores papel parede tema|personalization","Plano de fundo|papel parede wallpaper fundo imagem|personalization-background","Cores do Windows|tema escuro claro transparencia cor destaque|personalization-colors","Temas do Windows|temas aparencia|themes","Barra de tarefas|taskbar icones barra tarefas|taskbar","Menu Iniciar|iniciar menu aplicativos|personalization-start","Tela de bloqueio|bloqueio lockscreen|lockscreen","Fontes|tipografia fontes instalar|fonts",
   "Aplicativos instalados|desinstalar remover programas apps|appsfeatures","Aplicativos padrão|padrao navegador associacao extensoes|defaultapps","Aplicativos de inicialização|iniciar startup autostart|startupapps","Recursos opcionais|recursos windows instalar|optionalfeatures",
   "Configurações para desenvolvedores|modo desenvolvedor terminal|developers","Segurança do Windows|defender antivirus firewall|windowsdefender","Contas de e-mail|email sincronizacao contas|emailandaccounts",
   "Windows Update|atualizar atualizacoes updates|windowsupdate","Histórico de atualizações|atualizacoes instaladas historico|windowsupdate-history","Recuperação|restaurar redefinir backup recuperacao|recovery","Solução de problemas|diagnostico solucionar problemas|troubleshoot","Data e hora|relogio fuso horario data|dateandtime","Idioma e região|idioma regiao portugues teclado|regionlanguage","Digitação|teclado digitacao corretor|typing","Mouse|mouse ponteiro cursor velocidade|mousetouchpad","Touchpad|touchpad gestos notebook|devices-touchpad","Impressoras e scanners|impressora scanner imprimir|printers","Dispositivos USB|usb dispositivos|usb","Câmera|camera webcam|camera",
   "Acessibilidade|acessibilidade facilidade acesso|easeofaccess","Efeitos visuais|animacoes transparencia visual|easeofaccess-visualeffects","Tamanho do texto|fonte texto tamanho acessibilidade|easeofaccess-display","Lupa|zoom lupa ampliar|easeofaccess-magnifier","Contraste|contraste alto temas|easeofaccess-highcontrast","Narrador|narrador leitor voz|easeofaccess-narrator","Legendas|legendas caption|easeofaccess-closedcaptioning","Teclado acessível|teclas aderencia virtual acessibilidade|easeofaccess-keyboard",
   "Contas do usuário|usuario conta perfil|yourinfo","Opções de entrada|senha pin hello entrar|signinoptions","Outros usuários|usuarios contas familia|otherusers","Opções de backup|backup sincronizar|backup","Pesquisa do Windows|indexacao pesquisa arquivos permissoes|search","Modo de jogo|jogo desempenho gaming|gaming-gamemode","Capturas de jogos|gravacao captura xbox|gaming-gamedvr","Área de trabalho remota|remoto desktop rdp|remotedesktop"
  };
  Func<bool>? Requires(string page)=>page switch{"bluetooth"=>()=>Capabilities.Bluetooth,"network-wifi" or "network-mobilehotspot"=>()=>Capabilities.Wifi,"network-airplanemode"=>()=>Capabilities.Wireless,_=>null};
  var list=settings.Select(row=>{var p=row.Split('|');return new SystemAction(p[0],p[1],"ms-settings:"+p[2],Available:Requires(p[2]));}).ToList();
  list.AddRange(new[]{
   new SystemAction("Explorador de arquivos","explorer pastas arquivos gerenciador","explorer.exe","Abrir o Explorador"),
   new SystemAction("Gerenciador de tarefas","task manager processos desempenho cpu memoria","taskmgr.exe","Abrir o Gerenciador de Tarefas"),
   new SystemAction("Painel de Controle","painel control configuracoes antigas","control.exe"),
   new SystemAction("Gerenciador de dispositivos","drivers dispositivos hardware","devmgmt.msc"),
   new SystemAction("Serviços do Windows","servicos services iniciar parar reiniciar","services.msc","Gerenciar serviços do Windows"),
   new SystemAction("Gerenciamento de disco","particoes discos volumes formatar","diskmgmt.msc"),
   new SystemAction("Conexões de rede","adaptadores placas ip dns","ncpa.cpl"),
   new SystemAction("Programas e Recursos","programas desinstalar remover","appwiz.cpl"),
   new SystemAction("Ferramenta de Captura","screenshot print captura recorte tela","ms-screenclip:","Selecionar uma região da tela"),
   new SystemAction("Lixeira","reciclagem lixo arquivos excluidos","shell:RecycleBinFolder"),
   new SystemAction("Documentos","pasta documentos","shell:Personal"),new SystemAction("Downloads","pasta downloads baixar","shell:Downloads"),
   new SystemAction("Área de trabalho","desktop pasta trabalho","shell:Desktop"),new SystemAction("Este computador","meu computador unidades discos","shell:MyComputerFolder"),
   new SystemAction("Calculadora do Windows","calculadora calc","calculator:"),new SystemAction("Bloco de Notas","notepad texto notas","notepad.exe"),
   new SystemAction("Configurações do Windows","configuracoes ajustes sistema","ms-settings:")
  });list.AddRange(ActionRegistry.Fixed);return list.ToArray();
 }
 internal static (string Path,string Arguments) ResolveProgram(string target)
 {
  if(!target.EndsWith(".lnk",StringComparison.OrdinalIgnoreCase))return (target,"");object? shell=null,link=null;
  try{shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);dynamic sh=shell!;link=sh.CreateShortcut(target);dynamic shortcut=link;return ((string)shortcut.TargetPath,(string)shortcut.Arguments);}catch{throw new InvalidOperationException("Não foi possível resolver o executável deste atalho.");}finally{if(link!=null)Marshal.ReleaseComObject(link);if(shell!=null)Marshal.ReleaseComObject(shell);}
 }
 internal static List<AppEntry> Apps()=>AppDiscovery.Discover();
 private static readonly HashSet<string> Stop=new(){"abrir","o","a","do","da","de","para","configurar","configuracao","configuracoes","ajustar","mudar","trocar","mostrar","acessar"};
 internal static IReadOnlyList<SystemAction> Find(string query)
 {
  var q=Commands.Normalize(query);var tokens=q.Split(' ',StringSplitOptions.RemoveEmptyEntries).Where(t=>!Stop.Contains(t)).ToArray();
  int Rank(SystemAction a){var title=Commands.Normalize(a.Title);return title==q?0:title.StartsWith(q)?1:title.Contains(q)?2:3;}
  var exact=Visible.Where(a=>tokens.All(t=>Commands.Normalize(a.Title+" "+a.Keywords).Contains(t))).OrderBy(Rank).Take(12).ToArray();
  if(exact.Length>0 || tokens.Length==0)return exact;
  // Nada encontrado: tenta de novo tolerando erros de digitação ("blutooth", "volme").
  return Visible.Select(a=>(Action:a,Ok:Fuzzy.Matches(Commands.Normalize(a.Title+" "+a.Keywords),tokens,out var cost),Cost:cost)).Where(x=>x.Ok).OrderBy(x=>x.Cost).ThenBy(x=>Rank(x.Action)).Take(8).Select(x=>x.Action).ToArray();
 }
}
internal static class SystemIntegration
{
 // Executa qualquer destino da barra: ação registrada, serviço, pasta do shell, página de configuração, programa ou arquivo.
 // Devolve uma mensagem opcional para mostrar ao usuário (ex.: "IP local copiado: …").
 internal static async System.Threading.Tasks.Task<string?> Execute(string target,Func<string,bool> confirm)
 {
  if(ActionRegistry.Handles(target))return await ActionRegistry.Run(target,confirm);
  if(target.StartsWith("service:")){var parts=target.Split(':');if(parts.Length!=3 || !new[]{"start","stop"}.Contains(parts[1]) || !System.Text.RegularExpressions.Regex.IsMatch(parts[2],@"^[\w.\-]+$"))throw new InvalidOperationException("Use service:start:Nome ou service:stop:Nome.");if(confirm($"{(parts[1]=="start"?"Iniciar":"Parar")} o serviço {parts[2]}?")){var p=new ProcessStartInfo("sc.exe"){UseShellExecute=true,Verb="runas"};p.ArgumentList.Add(parts[1]);p.ArgumentList.Add(parts[2]);Process.Start(p);}return null;}
  if(target.StartsWith("shell:")){var p=new ProcessStartInfo("explorer.exe"){UseShellExecute=true};p.ArgumentList.Add(target);Process.Start(p);}else Commands.Open(Environment.ExpandEnvironmentVariables(target));
  return null;
 }
 internal static void Search(Settings settings,string query)
 {
  var url=(settings.SearchEngine switch{"Bing"=>"https://www.bing.com/search?q=","DuckDuckGo"=>"https://duckduckgo.com/?q=",_=>"https://www.google.com/search?q="})+Uri.EscapeDataString(query);
  if(string.IsNullOrWhiteSpace(settings.Browser))Commands.Open(url);else{if(!File.Exists(settings.Browser))throw new FileNotFoundException("Navegador não encontrado",settings.Browser);var p=new ProcessStartInfo(settings.Browser){UseShellExecute=true};p.ArgumentList.Add(url);Process.Start(p);}
 }
 internal static void ApplyBehavior(Settings settings)
 {
  var path=Environment.ProcessPath??"";if(System.IO.Path.GetFileNameWithoutExtension(path).Equals("dotnet",StringComparison.OrdinalIgnoreCase))path=System.IO.Path.Combine(AppContext.BaseDirectory,"ActionBar.exe");
  using(var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")){if(settings.StartWithWindows)key.DeleteValue("BarraInteligente",false);/* nome antigo (até a 0.4) */if(settings.StartWithWindows)key.SetValue("ActionBar","\""+path+"\" --startup");else key.DeleteValue("ActionBar",false);}
  ApplyPriority(settings);
 }
 internal static void ApplyPriority(Settings settings)
 {
  using var process=Process.GetCurrentProcess();process.PriorityClass=settings.Priority switch{"Baixa"=>ProcessPriorityClass.Idle,"Abaixo do normal"=>ProcessPriorityClass.BelowNormal,"Acima do normal"=>ProcessPriorityClass.AboveNormal,_=>ProcessPriorityClass.Normal};
 }
}



