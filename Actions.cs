using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32;
namespace BarraInteligente;
// Registro único das ações executáveis da barra.
//  • Ações fixas: aparecem no catálogo, na aba Windows e na lista de atalhos personalizados.
//  • Ações interpretadas: nascem do texto digitado ("volume 35", "fechar chrome", "desligar em 30 min").
// Toda ação é identificada por um destino "action:nome" ou "action:nome:argumento", que também é o que
// os atalhos personalizados e o histórico guardam. Ações com risco pedem confirmação no próprio handler.
internal static class ActionRegistry
{
 private delegate Task<string?> Handler(string argument,Func<string,bool> confirm);
 private static Handler Do(Action action)=>(_,_)=>{action();return Task.FromResult<string?>(null);};
 private static Handler Say(Func<string?> action)=>(_,_)=>Task.FromResult(action());
 private static Handler Ask(string question,Action action)=>(_,confirm)=>{if(confirm(question))action();return Task.FromResult<string?>(null);};
 private static SystemAction A(string title,string keywords,string name,string description,string glyph="⚙",Func<bool>? available=null)=>new(title,keywords,"action:"+name,description,glyph,available);

 internal static readonly SystemAction[] Fixed=
 {
  // Aparência e sistema (existentes desde a 0.3; os destinos foram mantidos para não quebrar atalhos salvos)
  A("Ativar tema escuro","mudar trocar tema escuro dark","dark","Aplicar tema escuro agora","◐"),A("Ativar tema claro","mudar trocar tema claro light","light","Aplicar tema claro agora","◑"),
  A("Ativar transparência do Windows","ativar ligar transparencia vidro blur","transparency-on","Aplicar efeitos de transparência do sistema"),A("Desativar transparência do Windows","desativar desligar transparencia vidro blur","transparency-off","Desativar efeitos de transparência do sistema"),
  A("Ativar animações do Windows","ativar ligar animacoes movimento efeitos","animations-on","Ativar animações da área de cliente do Windows"),A("Desativar animações do Windows","desativar desligar animacoes movimento efeitos","animations-off","Reduzir o movimento no Windows"),
  A("Plano de energia equilibrado","energia desempenho plano equilibrado balanced","power-balanced","Ativar o plano equilibrado, quando disponível"),A("Plano de economia de energia","energia plano economia bateria poupar","power-saver","Ativar o plano de economia, quando disponível"),A("Plano de alto desempenho","energia plano desempenho rapido performance","power-performance","Ativar o plano de alto desempenho, quando disponível"),
  // Áudio e mídia
  A("Volume","volume som audio aumentar diminuir alto baixo","volume-bar","Arraste, role o mouse ou use ← → · Enter silencia","♪"),
  A("Volume do microfone","microfone mic volume nivel ganho entrada","mic-bar","Arraste, role o mouse ou use ← → · Enter silencia o microfone","♪"),
  A("Silenciar / reativar áudio","som audio mudo silenciar","mute","Alternar o mudo","♪"),
  // Mantidas para atalhos personalizados; na pesquisa a barra de Volume as substitui.
  A("Aumentar volume","som audio aumentar volume","volumeup","Aumentar um incremento","♪",()=>false),A("Diminuir volume","som audio diminuir volume","volumedown","Diminuir um incremento","♪",()=>false),
  A("Silenciar / reativar microfone","microfone mic mudo mutar desmutar silenciar reativar","mic-toggle","Alternar o mudo do microfone padrão","♪"),
  A("Reproduzir / pausar mídia","musica video pause play pausar","play","Controlar a mídia em reprodução","♪"),A("Próxima faixa","musica proxima pular faixa","next","Avançar a mídia","♪"),A("Faixa anterior","musica anterior voltar faixa","previous","Voltar a mídia","♪"),
  // Janela em foco (a que estava ativa antes de abrir a barra)
  A("Maximizar janela","maximizar janela tela cheia ampliar","win-maximize","Janela em foco","◧"),A("Minimizar janela","minimizar janela esconder","win-minimize","Janela em foco","◧"),A("Restaurar janela","restaurar janela tamanho normal","win-restore","Janela em foco","◧"),
  A("Encaixar janela à esquerda","encaixar snap metade esquerda janela lado","win-left","Janela em foco · metade esquerda","◧"),A("Encaixar janela à direita","encaixar snap metade direita janela lado","win-right","Janela em foco · metade direita","◧"),
  A("Centralizar janela","centralizar centro meio janela","win-center","Janela em foco","◧"),A("Fixar / soltar janela sempre no topo","sempre no topo fixar pin acima frente janela","win-topmost","Janela em foco","◧"),
  A("Fechar janela","fechar janela","win-close","Janela em foco · o aplicativo pode pedir para salvar","◧"),
  A("Mover janela para o próximo monitor","mover janela monitor tela outro","win-monitor","Janela em foco","◧",()=>Capabilities.MultipleMonitors),
  A("Mostrar área de trabalho","mostrar area de trabalho desktop minimizar tudo","show-desktop","Minimizar todas as janelas (Win+D)","◧"),
  A("Visão de tarefas","visao tarefas janelas abertas areas de trabalho task view","task-view","Ver janelas e áreas de trabalho (Win+Tab)","◧"),
  A("Nova área de trabalho virtual","nova area de trabalho virtual desktop criar","desk-new","Win+Ctrl+D","◧"),A("Próxima área de trabalho virtual","proxima area de trabalho virtual desktop direita","desk-next","Win+Ctrl+→","◧"),
  A("Área de trabalho virtual anterior","anterior area de trabalho virtual desktop esquerda","desk-previous","Win+Ctrl+←","◧"),A("Fechar área de trabalho virtual","fechar area de trabalho virtual desktop","desk-close","As janelas vão para a área vizinha (Win+Ctrl+F4)","◧"),
  // Processos
  A("Reiniciar o Explorador do Windows","reiniciar explorer explorador barra de tarefas travada","restart-explorer","Útil quando a barra de tarefas ou o Explorador travam · pede confirmação","✕"),
  // Rede
  A("Copiar IP local","ip local endereco rede meu ip ipv4","ip-local","Endereço na rede interna","⇄"),A("Copiar IP público","ip publico externo internet meu ip","ip-public","Consulta api.ipify.org somente ao executar","⇄"),
  A("Limpar cache DNS","dns flush limpar cache rede internet site nao abre","flush-dns","Equivale a ipconfig /flushdns","⇄"),A("Renovar IP (DHCP)","renovar ip dhcp rede internet reconectar","renew-ip","Pede confirmação e permissão de administrador","⇄"),
  // Sessão e energia
  A("Bloquear computador","bloquear lock sessao","lock","Bloquear a sessão atual","⏻"),A("Encerrar sessão","sair sessao logoff deslogar encerrar sessao trocar usuario","logoff","Sair da conta · pede confirmação","⏻"),
  A("Suspender computador","suspender dormir sleep","sleep","Confirmar e suspender o computador","⏻"),
  A("Desligar computador","desligar shutdown energia","shutdown","Pede confirmação antes de desligar","⏻"),A("Reiniciar computador","reiniciar restart reboot","restart","Pede confirmação antes de reiniciar","⏻"),
  A("Cancelar desligamento agendado","cancelar desligamento agendado reinicio abortar","shutdown-cancel","Desfaz “desligar em…” ou “reiniciar às…”","⏻"),
 };

 private static readonly Dictionary<string,Handler> handlers=new()
 {
  ["dark"]=Do(()=>Commands.Theme(true)),["light"]=Do(()=>Commands.Theme(false)),
  ["transparency-on"]=Do(()=>Commands.Transparency(true)),["transparency-off"]=Do(()=>Commands.Transparency(false)),
  ["animations-on"]=Do(()=>Animations(true)),["animations-off"]=Do(()=>Animations(false)),
  ["power-balanced"]=Do(()=>Power("balanced")),["power-saver"]=Do(()=>Power("saver")),["power-performance"]=Do(()=>Power("performance")),
  ["mute"]=Do(()=>Media(0xad)),["volume-bar"]=Do(()=>Media(0xad)),["mic-bar"]=Say(()=>Audio.ToggleMicrophone()?"Microfone silenciado.":"Microfone reativado."),["volumeup"]=Do(()=>Media(0xaf)),["volumedown"]=Do(()=>Media(0xae)),
  ["play"]=Do(()=>Media(0xb3)),["next"]=Do(()=>Media(0xb0)),["previous"]=Do(()=>Media(0xb1)),
  ["volume"]=(arg,_)=>{if(!int.TryParse(arg,out var v)||v is <0 or >100)throw new InvalidOperationException("Use um volume entre 0 e 100.");Audio.SetVolume(v);return Task.FromResult<string?>(null);},
  ["mic-toggle"]=Say(()=>Audio.ToggleMicrophone()?"Microfone silenciado.":"Microfone reativado."),
  ["audio-default"]=(arg,_)=>{var device=Audio.Devices().FirstOrDefault(d=>d.Id==arg)??throw new InvalidOperationException("O dispositivo de áudio não está mais conectado.");Audio.SetDefault(device.Id);return Task.FromResult<string?>((device.Output?"Saída":"Entrada")+" de áudio: "+device.Name);},
  ["win-maximize"]=Do(WindowActions.Maximize),["win-minimize"]=Do(WindowActions.Minimize),["win-restore"]=Do(WindowActions.Restore),
  ["win-left"]=Do(()=>WindowActions.Snap("left")),["win-right"]=Do(()=>WindowActions.Snap("right")),["win-center"]=Do(()=>WindowActions.Snap("center")),
  ["win-topmost"]=Say(()=>WindowActions.ToggleTopmost()?"Janela fixada sempre no topo.":"Janela solta: não fica mais sempre no topo."),
  ["win-close"]=Do(WindowActions.Close),["win-monitor"]=Do(WindowActions.NextMonitor),
  ["window"]=(arg,_)=>{var h=new IntPtr(long.Parse(arg));if(!Native.IsWindow(h))throw new InvalidOperationException("A janela foi fechada.");WindowActions.Activate(h);return Task.FromResult<string?>(null);},
  ["show-desktop"]=Do(()=>WindowActions.Chord(0x5B,0x44)),["task-view"]=Do(()=>WindowActions.Chord(0x5B,0x09)),
  ["desk-new"]=Do(()=>WindowActions.Chord(0x5B,0x11,0x44)),["desk-next"]=Do(()=>WindowActions.Chord(0x5B,0x11,0x27)),["desk-previous"]=Do(()=>WindowActions.Chord(0x5B,0x11,0x25)),["desk-close"]=Do(()=>WindowActions.Chord(0x5B,0x11,0x73)),
  ["end-process"]=async(arg,confirm)=>confirm($"Encerrar {arg}?\n\nAs janelas recebem um pedido para fechar; o que não responder em 3 segundos é finalizado e trabalho não salvo pode ser perdido.")?await ProcessTools.End(arg):null,
  ["restart-explorer"]=async(_,confirm)=>{if(!confirm("Reiniciar o Explorador do Windows?\n\nA barra de tarefas e as janelas de pastas vão sumir por alguns segundos."))return null;await ProcessTools.RestartExplorer();return "Explorador reiniciado.";},
  ["ip-local"]=Say(()=>{var list=NetworkTools.LocalAddresses();if(list.Count==0)throw new InvalidOperationException("Nenhum adaptador de rede conectado.");System.Windows.Clipboard.SetText(list[0].Address);return "IP local copiado: "+string.Join(" · ",list.Select(a=>a.Address+" ("+a.Adapter+")"));}),
  ["ip-public"]=async(_,_)=>{var ip=await NetworkTools.PublicAddress();System.Windows.Clipboard.SetText(ip);return "IP público copiado: "+ip;},
  ["flush-dns"]=Say(()=>{NetworkTools.FlushDns();return "Cache DNS limpo.";}),
  ["renew-ip"]=Ask("Renovar o IP de todos os adaptadores?\n\nA conexão pode cair por alguns segundos. O Windows vai pedir permissão de administrador.",()=>Process.Start(new ProcessStartInfo("ipconfig.exe","/renew"){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Minimized})),
  ["ping"]=async(arg,_)=>{if(!NetworkTools.ValidHost(arg))throw new InvalidOperationException("Endereço inválido.");return await NetworkTools.Ping(arg);},
  ["lock"]=Do(()=>LockWorkStation()),
  ["logoff"]=Ask("Encerrar a sessão agora?\n\nSalve seu trabalho: os aplicativos serão fechados.",()=>ExitWindowsEx(0,0)),
  ["sleep"]=Ask("Suspender o computador agora?",()=>SetSuspendState(false,false,false)),
  ["shutdown"]=Ask("Salve seu trabalho. Deseja desligar o computador?",()=>Shutdown("/s /t 0")),
  ["restart"]=Ask("Salve seu trabalho. Deseja reiniciar o computador?",()=>Shutdown("/r /t 0")),
  ["shutdown-in"]=(arg,confirm)=>Scheduled(false,arg,confirm),["restart-in"]=(arg,confirm)=>Scheduled(true,arg,confirm),
  ["shutdown-cancel"]=Say(PowerSchedule.Cancel),
 };
 private static Task<string?> Scheduled(bool restart,string arg,Func<string,bool> confirm)
 {
  if(!int.TryParse(arg,out var seconds) || seconds<=0)throw new InvalidOperationException("Tempo inválido.");var now=DateTime.Now;
  return Task.FromResult(confirm($"{(restart?"Reiniciar":"Desligar")} o computador às {now.AddSeconds(seconds):HH:mm} (em {PowerSchedule.Describe(seconds)})?\n\nO Windows avisará antes, e você pode cancelar digitando “cancelar desligamento”.")?PowerSchedule.Schedule(restart,seconds,now):null);
 }
 internal static bool Handles(string target)=>target.StartsWith("action:",StringComparison.Ordinal);
 internal static Task<string?> Run(string target,Func<string,bool> confirm)
 {
  var body=target["action:".Length..];var colon=body.IndexOf(':');var name=colon<0?body:body[..colon];var argument=colon<0?"":body[(colon+1)..];
  if(!handlers.TryGetValue(name,out var handler))throw new InvalidOperationException("Ação desconhecida: "+name);
  return handler(argument,confirm);
 }
 internal static bool NeedsTargetWindow(string target)=>target.StartsWith("action:win-",StringComparison.Ordinal);

 // Ações nascidas do texto digitado. Recebe o texto já normalizado (minúsculo e sem acentos).
 internal static IEnumerable<SystemAction> Interpret(string q,string raw)
 {
  if(q.Length==0)yield break;
  if(Commands.VolumeValue(q) is int volume)yield return new($"Volume em {volume}%","","action:volume:"+volume,"Ajustar o volume principal agora"+(Audio.Volume() is int now?$" · atual {now}%":""),"♪");
  if(Regex.Match(q,@"^(iniciar|parar) (?:o )?servico ([\w.\-]+)$") is {Success:true} service)
   yield return new(raw,"","service:"+(service.Groups[1].Value=="iniciar"?"start":"stop")+":"+service.Groups[2].Value,"Serviço do Windows · requer confirmação e elevação");
  if(PowerSchedule.Parse(q,DateTime.Now) is ScheduledPower power)
   yield return new($"{(power.Restart?"Reiniciar":"Desligar")} em {PowerSchedule.Describe(power.Seconds)}","",$"action:{(power.Restart?"restart-in":"shutdown-in")}:{power.Seconds}",$"Agendar para {DateTime.Now.AddSeconds(power.Seconds):HH:mm} · pede confirmação","⏻");
  if(Regex.Match(q,@"^ping ([^\s]+)$") is {Success:true} ping && NetworkTools.ValidHost(ping.Groups[1].Value))
   yield return new("Ping "+ping.Groups[1].Value,"","action:ping:"+ping.Groups[1].Value,"Rede · 4 pacotes, resultado na notificação","⇄");
  foreach(var a in AudioChoices(q))yield return a;
  foreach(var a in ProcessChoices(q))yield return a;
 }
 private static readonly string[] OutputWords={"saida","alto","falante","falantes","fone","fones","headset","headphone","caixa","caixas","som","audio"};
 private static readonly string[] InputWords={"entrada","microfone","mic"};
 private static readonly string[] Filler={"trocar","mudar","usar","alternar","selecionar","dispositivo","de","do","da","para","o","a","padrao","como"};
 // "saída de áudio", "usar fones", "microfone realtek" → lista os dispositivos ativos daquele tipo.
 private static IEnumerable<SystemAction> AudioChoices(string q)
 {
  var words=q.Split(' ',StringSplitOptions.RemoveEmptyEntries);var output=words.Any(OutputWords.Contains);var input=words.Any(InputWords.Contains);
  if(!output && !input)return Array.Empty<SystemAction>();
  // "aumentar volume", "silenciar microfone" etc. já são ações fixas; só listamos dispositivos quando o pedido é de troca.
  if(words.Any(w=>w is "volume" or "silenciar" or "mudo" or "mutar" or "desmutar" or "reativar" or "aumentar" or "diminuir"))return Array.Empty<SystemAction>();
  var rest=words.Where(w=>!OutputWords.Contains(w) && !InputWords.Contains(w) && !Filler.Contains(w)).ToArray();
  List<AudioDevice> devices;try{devices=Audio.Devices();}catch{return Array.Empty<SystemAction>();}
  return devices.Where(d=>(d.Output?output:input) && (rest.Length==0 || Fuzzy.Matches(Commands.Normalize(d.Name),rest,out _)))
   .OrderBy(d=>d.IsDefault).Select(d=>new SystemAction($"Usar {d.Name}",d.Name,"action:audio-default:"+d.Id,(d.Output?"Saída":"Entrada")+" de áudio"+(d.IsDefault?" · em uso agora":" · tornar padrão"),"♪")).ToList();
 }
 private static readonly HashSet<string> NotProcesses=new(){"janela","sessao","agrupamento","desligamento","area de trabalho virtual","tudo"};
 // "fechar chrome", "encerrar processo discord", "processos" → candidatos com memória usada.
 private static IEnumerable<SystemAction> ProcessChoices(string q)
 {
  List<ProcessGroup> found;
  if(q is "processos" or "processos abertos" or "listar processos" or "top processos" or "uso de memoria")found=ProcessTools.Top(8);
  else if(Regex.Match(q,@"^(?:fechar|encerrar|matar|finalizar|kill)(?: o| a)?(?: processo| programa| aplicativo| app)? (.+)$") is {Success:true} m && !NotProcesses.Contains(m.Groups[1].Value))found=ProcessTools.Find(m.Groups[1].Value);
  else return Array.Empty<SystemAction>();
  return found.Select(g=>new SystemAction("Encerrar "+g.Name,g.Name,"action:end-process:"+g.Name,$"Processo · {g.Count} instância{(g.Count>1?"s":"")} · {ProcessTools.Size(g.Memory)} · pede confirmação","✕"));
 }

 [DllImport("user32.dll")] private static extern bool LockWorkStation();
 [DllImport("user32.dll")] private static extern bool ExitWindowsEx(uint flags,uint reason);
 [DllImport("user32.dll",SetLastError=true)] private static extern bool SystemParametersInfo(uint action,uint parameter,ref int value,uint flags);
 [DllImport("powrprof.dll",SetLastError=true)] private static extern bool SetSuspendState(bool hibernate,bool force,bool disableWake);
 private static void Animations(bool value){var enabled=value?1:0;if(!SystemParametersInfo(0x1043,0,ref enabled,3))throw new InvalidOperationException("O Windows não aplicou a preferência de animações.");}
 private static void Power(string plan)
 {
  var guid=plan switch{"balanced"=>"381b4222-f694-41f0-9685-ff5bb260df2e","saver"=>"a1841308-3541-4fab-bc81-f71556f20b4a",_=>"8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"};
  using var process=Process.Start(new ProcessStartInfo("powercfg.exe","/setactive "+guid){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true});if(process==null || !process.WaitForExit(3000) || process.ExitCode!=0)throw new InvalidOperationException("Este plano de energia não está disponível ou foi bloqueado neste computador.");
 }
 private static void Shutdown(string args)=>Process.Start(new ProcessStartInfo("shutdown.exe",args){UseShellExecute=false,CreateNoWindow=true});
 internal static void Media(byte key){Native.keybd_event(key,0,0,UIntPtr.Zero);Native.keybd_event(key,0,2,UIntPtr.Zero);}
}
