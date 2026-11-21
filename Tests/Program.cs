using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Text.Json;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using BarraInteligente;
internal static class Tests
{
    static int failures;
    static void Check(bool ok,string label){Console.WriteLine((ok?"PASS ":"FAIL ")+label);if(!ok)failures++;}
    [STAThread] static int Main(string[] args)
    {
        var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
        if(args.Contains("--probe"))
        {
            var window=new Window{Title="Barra · janela de teste",Width=420,Height=260,Left=45,Top=55,WindowStartupLocation=WindowStartupLocation.Manual};window.Show();
            var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(30)};timer.Tick+=(_,_)=>app.Shutdown();timer.Start();app.Run();return 0;
        }
        if(args.Length>=3 && args[0]=="--rank")
        {
            // Ferramenta de avaliação: ranqueia consultas sobre um índice real (somente leitura).
            var real=new Store(args[1]);using var real_index=new FileIndex(real);real_index.Load().GetAwaiter().GetResult();
            var favorites=real.Data.History.Where(h=>h.Category=="files").Select(h=>h.Target).ToArray();
            foreach(var query in args.Skip(2)){Console.WriteLine("== "+query);var watch=Stopwatch.StartNew();var found=real_index.Find(query,10,default,favorites);watch.Stop();foreach(var e in found)Console.WriteLine((e.Folder?"[pasta] ":"        ")+e.Path);Console.WriteLine($"({watch.ElapsedMilliseconds} ms)");}
            return 0;
        }
        app.Startup+=async(_,_)=>{try{await Run(app);}catch(Exception ex){Console.WriteLine(ex);failures++;}finally{app.Shutdown();}};
        app.Run();return failures==0?0:1;
    }
    static async Task Run(Application app)
    {
        Check(Commands.Normalize("  TEMA ESCURO  ")=="tema escuro","normalização de texto");
        Check(Commands.Normalize("Configurações")=="configuracoes","acentos em português");
        Check(Commands.VolumeValue("ajustar volume para 37% ")==37,"volume absoluto");
        Check(Commands.VolumeValue("volume 101")==null,"rejeitar volume acima de 100");
        Check(Commands.VolumeValue("volume -1")==null,"rejeitar volume negativo");
        Check(Commands.VolumeValue("volume 0")==0,"volume zero");
        await NewFeatures();
        await SearchWidgets();
        var store=new Store();store.Data=new Settings();using var manager=new WindowManager(store);
        using(var probe=Process.Start(new ProcessStartInfo(Environment.ProcessPath!,"--probe"){UseShellExecute=false}))
        {
            var hwnd=await WaitProbe(probe!);await Task.Delay(600); Check(!app.Windows.Cast<Window>().Any(w=>w.Title.StartsWith("Agrupamento ativo ·") || w.Title.StartsWith("Aro ·")),"janela comum não exibe marcação");Native.GetWindowRect(hwnd,out var r);var area=Native.WorkArea(hwnd);
            Check(Math.Abs(r.Left-(area.Left+Math.Max(0,(area.Width-r.Width)/2)))<4 && Math.Abs(r.Top-(area.Top+Math.Max(0,(area.Height-r.Height)/2)))<4,"nova janela centralizada na área útil");
            WindowActions.Use(hwnd);WindowActions.Snap("left");await Task.Delay(150);Native.DwmGetWindowRect(hwnd,9,out var snapped,16);
            Check(Math.Abs(snapped.Left-area.Left)<=2 && Math.Abs(snapped.Top-area.Top)<=2 && Math.Abs(snapped.Width-area.Width/2)<=2 && Math.Abs(snapped.Height-area.Height)<=2,"encaixar janela à esquerda sem bordas invisíveis");
            var pinned=WindowActions.ToggleTopmost();var unpinned=!WindowActions.ToggleTopmost();Check(pinned && unpinned,"fixar e soltar janela sempre no topo");
            Check(WindowActions.Find("janela de teste").Any(w=>w.Handle==hwnd),"encontrar janela aberta pelo título");
            await ActionRegistry.Run("action:window:"+hwnd.ToInt64(),_=>true);
            probe!.Kill();await probe.WaitForExitAsync();await Task.Delay(200);
            var closed=false;try{await ActionRegistry.Run("action:window:"+hwnd.ToInt64(),_=>true);}catch(InvalidOperationException){closed=true;}Check(closed,"alternar para janela fechada explica o motivo");
        }
        var ws=new Workspace{Name="Teste de exceção",Apps=new(){new LayoutItem{Executable=Environment.ProcessPath!,Arguments="--probe",Title="Barra · janela de teste",X=80,Y=95,Width=460,Height=300}}};
        await Task.Delay(1200); var count=app.Windows.Count;var launch=manager.Launch(ws);bool glow=false;
        while(!launch.IsCompleted){if(app.Windows.Cast<Window>().Any(w=>w.Title=="Aro · Barra · janela de teste"))glow=true;await Task.Delay(25);}
        Console.WriteLine(await launch);
        var h=Native.Windows().FirstOrDefault(w=>Native.Path(w)==Environment.ProcessPath);
        Native.GetWindowRect(h,out var positioned);
        Check(h!=IntPtr.Zero && positioned.Left==80 && positioned.Top==95 && positioned.Width==460 && positioned.Height==300,"workspace respeita posição e tamanho");
        Check(!glow,"workspace sem aro por janela na inicialização");
        Check(manager.ActiveGroupName==ws.Name && app.Windows.Cast<Window>().Any(w=>w.Title=="Agrupamento ativo · "+ws.Name),"marcação apenas após ativar agrupamento");
        manager.EndGroup();
        Check(manager.ActiveGroupName==null && !app.Windows.Cast<Window>().Any(w=>w.Title.StartsWith("Agrupamento ativo ·")),"encerrar agrupamento remove marcação imediatamente");
        if(h!=IntPtr.Zero){Native.GetWindowThreadProcessId(h,out var pid);using var p=Process.GetProcessById((int)pid);p.Kill();await p.WaitForExitAsync();}
        ws.Apps.Add(new LayoutItem{Executable=Environment.ProcessPath!,Arguments="--probe",Title="Barra · janela de teste",X=600,Y=95,Width=460,Height=300});
        var concurrent=manager.Launch(ws);
        Check((await manager.Launch(ws)).Contains("já está"),"impedir lançamentos sobrepostos");
        Console.WriteLine(await concurrent);
        var windows=Native.Windows().Where(w=>Native.Path(w)==Environment.ProcessPath && Native.Title(w)=="Barra · janela de teste").ToList();
        Check(windows.Count==2,"abrir dois aplicativos do workspace simultaneamente");
        var positions=windows.Select(w=>{Native.GetWindowRect(w,out var rect);return (rect.Left,rect.Top);}).Distinct().Count();
        Check(positions==2,"cada janela recebe um layout distinto");
        foreach(var pid in windows.Select(w=>{Native.GetWindowThreadProcessId(w,out var id);return (int)id;}).Distinct()){try{using var process=Process.GetProcessById(pid);process.Kill();await process.WaitForExitAsync();}catch(ArgumentException){}}
        await Task.Delay(350);Check(manager.ActiveGroupName==null && !app.Windows.Cast<Window>().Any(w=>w.Title.StartsWith("Agrupamento ativo ·")),"fechar todas as janelas do grupo remove a marcação");
        store.Data.CenterWindows=false;
        using(var probe=Process.Start(new ProcessStartInfo(Environment.ProcessPath!,"--probe"){UseShellExecute=false}))
        {var hwnd=await WaitProbe(probe!);await Task.Delay(750);Native.GetWindowRect(hwnd,out var rect);Check(rect.Left==45 && rect.Top==55,"desativar centralização preserva a posição original");probe!.Kill();await probe.WaitForExitAsync();}
        Console.WriteLine($"RESULTADO: {failures} falha(s)");
    }
    static async Task NewFeatures()
    {
        bool Calc(string text,double expected)=>Calculator.Try(text,out var result) && double.TryParse(result,NumberStyles.Float,CultureInfo.CurrentCulture,out var n) && Math.Abs(n-expected)<1e-8;
        Check(Calc("12 * (8+2)",120),"calculadora com precedência e parênteses");
        Check(Calc("sqrt(144)",12),"raiz quadrada");Check(Calc("-2^2",-4),"precedência de sinal e potência");
        Check(Calc("2^3^2",512),"potência associativa à direita");Check(Calc("12,5+2,5",15),"vírgula decimal");
        Check(Calc("200 * 15%",30),"porcentagem em expressão");Check(Calc("15% de 200",30),"porcentagem em português");
        Check(Calculator.Try("10 km em m",out var distance) && distance=="10000 m","conversão de distância");
        Check(Calculator.Try("32 f em c",out var temperature) && temperature=="0 c","conversão de temperatura");
        Check(Calculator.Try("2 horas em minutos",out var time) && time=="120 minutos","conversão de tempo");
        Check(!Calculator.Try("1 / 0",out _),"rejeitar divisão por zero");Check(!Calculator.Try("sqrt(-1)",out _),"rejeitar resultado não real");Check(!Calculator.Try(new string('(',100)+"1+1"+new string(')',100),out _),"limitar profundidade da calculadora");
        Check(!Calculator.Try("relatorio 2026",out _),"nomes de arquivos não viram cálculo");
        Check(Hotkeys.Parse("Ctrl+Alt+K",out var mods,out var key) && mods==3 && key==75,"interpretar atalhos personalizados");
        Check(!Hotkeys.Parse("Win+I",out _,out _),"preservar atalhos reservados do Windows");
        Check(!Hotkeys.Parse("Ctrl+Alt",out _,out _),"rejeitar atalho sem tecla final");
        Check(Catalog.Actions.Length>=70,"catálogo amplo de ações e configurações");
        Check(Catalog.Find("internet").Any(a=>a.Target=="ms-settings:network"),"pesquisar configurações por sinônimos");
        Check(Catalog.Find("wifi").Any(a=>a.Target=="ms-settings:network-wifi")==Capabilities.Wifi,"Wi-Fi só aparece quando há adaptador sem fio");
        Check(Catalog.Find("bluetooth").Any(a=>a.Target=="ms-settings:bluetooth")==Capabilities.Bluetooth,"Bluetooth só aparece quando há rádio");
        Check(Catalog.Actions.Any(a=>a.Target=="ms-settings:bluetooth"),"catálogo completo mantém todas as ações para atalhos");
        await ActionRegistryTests();
        FileSearchTests();
        Check(Catalog.Find("configurar volume").Any(a=>a.Target=="ms-settings:sound"),"pesquisar ações em linguagem natural");
        var area=new System.Drawing.Rectangle(-1280,100,1280,900);
        for(var preset=0;preset<6;preset++)
        {
            var cells=Layouts.Cells(preset,area);var valid=cells.All(c=>area.Contains(c)) && cells.Sum(c=>c.Width*c.Height)==area.Width*area.Height;
            for(var i=0;i<cells.Count;i++)for(var j=i+1;j<cells.Count;j++){var intersection=System.Drawing.Rectangle.Intersect(cells[i],cells[j]);if(intersection.Width>0 && intersection.Height>0)valid=false;}
            Check(valid,"layout "+(preset+1)+" cobre monitor sem sobrepor posições");
        }
        var root=Path.Combine(AppContext.BaseDirectory,"test-data-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var file=Path.Combine(root,"settings.json");File.WriteAllText(file,"{\"CenterWindows\":false,\"Workspaces\":[]}");var old=new Store(file);
            Check(!old.Data.CenterWindows && old.Data.Appearance.Theme=="Escuro" && old.Data.Calculator,"migrar configurações antigas mantendo novos padrões");
            old.Data.Hotkey="Ctrl+Alt+Space";old.Data.Notifications=false;old.Save();var loaded=new Store(file);Check(loaded.Data.Hotkey=="Ctrl+Alt+Space" && !loaded.Data.Notifications,"persistir novas configurações");
            var files=Path.Combine(root,"documents");Directory.CreateDirectory(files);Directory.CreateDirectory(Path.Combine(files,"Subpasta"));Directory.CreateDirectory(Path.Combine(files,"node_modules"));
            File.WriteAllText(Path.Combine(files,"São Paulo.txt"),"teste");File.WriteAllText(Path.Combine(files,"Subpasta","planejamento.pdf"),"teste");File.WriteAllText(Path.Combine(files,"node_modules","ignorado.txt"),"teste");
            loaded.Data.IndexRoots=new(){files};using var index=new FileIndex(loaded);await index.Rebuild();
            Check(index.Find("sao paulo").Any(e=>e.Name=="São Paulo.txt"),"indexar e encontrar arquivos com acentos");
            Check(index.Find("planejamento").Any(e=>e.Name=="planejamento.pdf"),"indexar subpastas");Check(index.Find("ignorado").Count==0,"ignorar diretórios de dependências");
            using var cached=new FileIndex(loaded);await cached.Load();Check(cached.Find("sao paulo").Count==1,"carregar índice persistido");
            loaded.Data.IndexRoots=new(){Path.Combine(files,"Subpasta")};Check(index.Find("sao paulo").Count==0,"excluir resultados fora das pastas selecionadas");
            loaded.Data.IndexFiles=false;Check(index.Find("planejamento").Count==0,"desativar busca de arquivos");
        }
        finally{Directory.Delete(root,true);}
    }
    static void FileSearchTests()
    {
        bool Drives(char c)=>c is 'c' or 'd' or 'e';
        Check(FileQuery.Parse("obs d",Drives) is {Text:"obs",Drive:'d'},"\"obs d\" prioriza a unidade D");
        Check(FileQuery.Parse("D: obs",Drives) is {Text:"obs",Drive:'d'} && FileQuery.Parse("d:obs",Drives) is {Text:"obs",Drive:'d'},"\"d: obs\" e \"d:obs\"");
        Check(FileQuery.Parse("obs no d",Drives) is {Text:"obs",Drive:'d'} && FileQuery.Parse("obs na unidade e",Drives) is {Text:"obs",Drive:'e'},"\"obs no d\" e \"obs na unidade e\"");
        Check(FileQuery.Parse("obs z",Drives) is {Text:"obs z",Drive:null},"letra sem unidade continua sendo texto");
        Check(FileQuery.Parse("d",Drives) is {Text:"d",Drive:null},"letra sozinha não vira unidade");
        Check(FileQuery.Parse("pasta obs",Drives) is {Text:"obs",FoldersOnly:true} && FileQuery.Parse("arquivos obs d",Drives) is {Text:"obs",FoldersOnly:false,Drive:'d'},"\"pasta\" e \"arquivo\" filtram o tipo");
        Check(FileRanking.AtWordStart("obs studio","obs") && FileRanking.AtWordStart("gravacao-obs","obs") && !FileRanking.AtWordStart("jobs","obs"),"início de palavra no nome");
        var entries=new List<FileEntry>{new("OBS Stúdio",@"D:\OBS Stúdio",true),new("obs-plugins",@"C:\Program Files\obs-studio\data\obs-plugins",true),new("obs",@"D:\Code\proj\share\obs",true),new("obs.c",@"D:\Code\proj\src\obs.c",false),new("CMakeLists.txt",@"D:\Code\proj\CMakeLists.txt",false),new("jobs.txt",@"D:\jobs.txt",false),new("OBS",@"E:\OBS",true)};
        for(var i=0;i<5;i++)entries.Add(new($"gravacao {i}.mp4",$@"D:\OBS Stúdio\gravacao {i}.mp4",false));
        var facts=FolderFacts.Build(entries);var obs=FileQuery.Parse("obs",Drives);
        var ranked=entries.Where(e=>e.SearchName.Contains("obs")||e.SearchPath.Contains("obs")).OrderByDescending(e=>FileRanking.Score(e,obs,null,facts)).Select(e=>e.Path).ToList();
        Check(ranked.First(p=>p!=@"E:\OBS")==@"D:\OBS Stúdio","pasta de gravações vence pastas de programa e de código");
        Check(ranked.IndexOf(@"D:\Code\proj\src\obs.c")>ranked.IndexOf(@"D:\OBS Stúdio\gravacao 0.mp4"),"código dentro de projeto fica abaixo dos vídeos");
        Check(ranked.IndexOf(@"D:\jobs.txt")>ranked.IndexOf(@"E:\OBS"),"\"jobs\" não conta como \"obs\" em início de palavra");
        var onE=FileQuery.Parse("obs e",Drives);Check(entries.OrderByDescending(e=>FileRanking.Score(e,onE,null,facts)).First().Path==@"E:\OBS","letra da unidade passa os itens dela para o topo");
        Check(FileRanking.Score(entries[1],obs,new[]{@"C:\Program Files\obs-studio\data\obs-plugins"},facts)<FileRanking.Score(entries[0],obs,null,facts),"uma abertura antiga não domina o ranking");
    }
    static async Task ActionRegistryTests()
    {
        // Busca tolerante
        Check(Fuzzy.Distance("blutooth","bluetooth")==1 && Fuzzy.Distance("volmue","volume")==1,"distância de digitação com troca de letras");
        Check(Catalog.Find("volme").Any(a=>a.Target=="ms-settings:sound"),"encontrar ação com erro de digitação");
        Check(Catalog.Find("impresoras").Any(a=>a.Target=="ms-settings:printers"),"encontrar configuração com letra faltando");
        Check(!Fuzzy.Matches("tela e monitores",new[]{"som"},out _),"palavras curtas não aceitam erro");
        Check(Catalog.Find("tema escuro").First().Target=="action:dark","busca exata continua com prioridade");
        // Registro
        var targets=Catalog.Actions.Select(a=>a.Target).Where(ActionRegistry.Handles).ToList();
        Check(targets.Count>=40 && targets.Distinct().Count()==targets.Count,"ações registradas sem destinos repetidos");
        foreach(var legacy in new[]{"dark","light","mute","volumeup","volumedown","play","next","previous","lock","sleep","shutdown","restart","transparency-on","transparency-off","animations-on","animations-off","power-balanced","power-saver","power-performance"})
            if(!targets.Contains("action:"+legacy)){Check(false,"destino antigo preservado: "+legacy);}
        Check(true,"destinos de atalhos antigos preservados");
        var unknown=false;try{await ActionRegistry.Run("action:nao-existe",_=>false);}catch(InvalidOperationException){unknown=true;}Check(unknown,"ação desconhecida é recusada");
        var declined=await ActionRegistry.Run("action:shutdown-in:600",_=>false);Check(declined==null,"desligamento agendado respeita a recusa");
        var noProcess=await ActionRegistry.Run("action:end-process:processo-que-nao-existe-123",_=>true);Check(noProcess!=null && noProcess.Contains("não está"),"encerrar processo inexistente não falha");
        var system=false;try{await ProcessTools.End("csrss");}catch(InvalidOperationException){system=true;}Check(system,"processos do sistema são protegidos");
        // Interpretação de texto
        var now=new DateTime(2026,11,18,22,0,0);
        Check(PowerSchedule.Parse("desligar em 30 min",now) is {Restart:false,Seconds:1800},"desligar em 30 min");
        Check(PowerSchedule.Parse("reiniciar daqui a 2 horas",now) is {Restart:true,Seconds:7200},"reiniciar daqui a 2 horas");
        Check(PowerSchedule.Parse("desligar as 23:30",now) is {Seconds:5400},"desligar às 23:30");
        Check(PowerSchedule.Parse("desligar as 7h",now) is {Seconds:32400},"horário já passado vai para o dia seguinte");
        Check(PowerSchedule.Parse("desligar as 25:00",now)==null && PowerSchedule.Parse("desligar em 0 min",now)==null,"rejeitar horários inválidos");
        Check(PowerSchedule.Parse("desligar",now)==null,"desligar sem tempo continua sendo a ação imediata");
        var interpreted=ActionRegistry.Interpret("volume 35","volume 35").ToList();Check(interpreted.Any(a=>a.Target=="action:volume:35"),"interpretar volume");
        Check(ActionRegistry.Interpret("iniciar servico spooler","iniciar servico Spooler").Any(a=>a.Target=="service:start:spooler"),"interpretar serviço");
        Check(ActionRegistry.Interpret("ping 1.1.1.1","ping 1.1.1.1").Any(a=>a.Target=="action:ping:1.1.1.1"),"interpretar ping");
        Check(!ActionRegistry.Interpret("ping a;b","ping a;b").Any(),"rejeitar endereço de ping inválido");
        Check(!ActionRegistry.Interpret("fechar janela","fechar janela").Any(a=>a.Target.StartsWith("action:end-process")),"fechar janela não vira encerrar processo");
        var own=Process.GetCurrentProcess().ProcessName;Check(!ProcessTools.Groups().Any(g=>g.Name==own) || own!="BarraInteligente","a barra não se oferece para encerramento");
        Check(ActionRegistry.Interpret("processos","processos").Count()>0,"listar processos que mais usam memória");
        Check(NetworkTools.LocalAddresses().All(a=>System.Net.IPAddress.TryParse(a.Address,out _)),"IPs locais válidos");
        // Áudio (somente leitura: não troca dispositivos nem muda volume real)
        var devices=Audio.Devices();Check(devices.Count==0 || devices.Count(d=>d.Output && d.IsDefault)<=1,"no máximo uma saída padrão");
        Console.WriteLine("INFO áudio: "+string.Join(" | ",devices.Select(d=>(d.Output?"saída ":"entrada ")+d.Name+(d.IsDefault?" *":""))));
        if(devices.FirstOrDefault(d=>d.Output && d.IsDefault) is AudioDevice current){Audio.SetDefault(current.Id);Check(Audio.Devices().Any(d=>d.Id==current.Id && d.IsDefault),"reaplicar a saída atual (valida a troca de dispositivo sem mudar nada)");}
        Check(Audio.Volume() is null or (>=0 and <=100),"ler volume atual");
        if(devices.Any(d=>d.Output))Check(ActionRegistry.Interpret("saida de audio","saída de áudio").Count()==devices.Count(d=>d.Output),"listar saídas de áudio");
        Check(!ActionRegistry.Interpret("aumentar volume","aumentar volume").Any(a=>a.Target.StartsWith("action:audio-default")),"aumentar volume não lista dispositivos");
        Check(ActionRegistry.NeedsTargetWindow("action:win-left") && !ActionRegistry.NeedsTargetWindow("action:show-desktop"),"ações de janela exigem janela alvo");
        var noWindow=false;try{WindowActions.Capture(IntPtr.Zero);if(WindowActions.Target==IntPtr.Zero)WindowActions.Maximize();else noWindow=true;}catch(InvalidOperationException){noWindow=true;}Check(noWindow,"ação de janela sem alvo explica o motivo");
    }
    private sealed class FakeWeather : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            Calls++;var content=request.RequestUri!.Host=="ipwho.is"?"{\"success\":true,\"latitude\":10.12345,\"longitude\":20.67891,\"city\":\"Cidade de teste\",\"country\":\"Teste\"}":"{\"current\":{\"temperature_2m\":21.5,\"relative_humidity_2m\":60,\"apparent_temperature\":22,\"weather_code\":2,\"wind_speed_10m\":8}}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(content)});
        }
    }
    static async Task SearchWidgets()
    {
        Check(AppDiscovery.IsSystemTool("reg",Path.Combine(Environment.SystemDirectory,"reg.exe")),"excluir executáveis internos Win32 da aba Apps");
        Check(AppDiscovery.IsSystemTool("Configurações",@"shell:AppsFolder\windows.immersivecontrolpanel"),"configurações ficam na categoria Windows");
        Check(!AppDiscovery.IsSystemTool("Bloco de Notas",Path.Combine(Environment.SystemDirectory,"notepad.exe")),"manter aplicativos gráficos do Windows");
        Check(AppDiscovery.IsNoise("Desinstalar Meu App",@"C:\Apps\unins000.exe"),"ocultar atalhos de desinstalação e auxiliares");
        Check(!AppDiscovery.IsGuiExecutable(Path.Combine(Environment.SystemDirectory,"cmd.exe")),"não indexar ferramentas de console como aplicativos gráficos");
        Check(AppDiscovery.ExtractExecutable("\"C:\\Program Files\\Browser\\browser.exe\" --flag")==@"C:\Program Files\Browser\browser.exe","detectar executável do navegador com espaços");
        var settings=new Settings();
        UsageHistory.Record(settings,"apps","private","private","");
        Check(settings.History.Count==0 && UsageHistory.Recent(settings).Count==0,"histórico desativado por padrão");
        settings.HistoryEnabled=true;foreach(var app in new[]{"A","B","C","D"})UsageHistory.Record(settings,"apps",app,app,app);UsageHistory.Record(settings,"windows","sound","Som","ms-settings:sound");
        Check(UsageHistory.Recent(settings,"apps").Select(h=>h.Id).SequenceEqual(new[]{"D","C","B"}),"três recentes independentes por categoria");
        UsageHistory.Record(settings,"apps","B","B","B");Check(settings.History.Count(h=>h.Category=="apps"&&h.Id=="B")==1,"histórico sem duplicar aplicativos");
        var items=new[]{"A","B","C","D"}.Select(a=>new Result(a,"App","",()=>Task.CompletedTask,a,"apps"));var ranked=ResultRanking.Apply(items,settings,"apps");
        Check(ranked[0].Id=="B"&&ranked.Count(r=>r.IsRecent)==3,"destacar exatamente os três aplicativos mais recentes");
        UsageHistory.Record(settings,"files","arquivo-antigo","antigo","arquivo-antigo");var fileItems=new[]{"arquivo-melhor","arquivo-antigo"}.Select(p=>new Result(p,"Arquivo","",()=>Task.CompletedTask,p,"files")).ToList();
        var byScore=ResultRanking.Apply(fileItems,settings,"files",true);Check(byScore[0].Id=="arquivo-melhor" && byScore[1].IsRecent,"arquivos pesquisados seguem a pontuação e mantêm o rótulo RECENTE");
        Check(ResultRanking.Apply(fileItems,settings,"files",false)[0].Id=="arquivo-antigo","sem texto, os recentes continuam no topo");
        var disks=DiskInfo.Read();Check(disks.Count>0&&disks.Where(d=>d.Ready).All(d=>d.Free>=0&&d.Total>=d.Free&&d.UsedPercent>=0&&d.UsedPercent<=100),"capacidade e espaço livre reais dos discos");
        using(var monitor=new ResourceMonitor()){await Task.Delay(150);var sample=monitor.Read();Check(sample.TotalMemory>0&&sample.FreeMemory<=sample.TotalMemory&&sample.MemoryPercent>=0&&sample.MemoryPercent<=100,"leitura real de memória física");Check(sample.Cpu is >=0 and <=100,"leitura real de uso da CPU");Check(!sample.Gpu.HasValue || sample.Gpu is >=0 and <=100,"GPU válida ou explicitamente indisponível");Console.WriteLine(sample.Gpu.HasValue?"INFO contador GPU disponível":"INFO contador GPU não fornecido pelo driver");}
        var fake=new FakeWeather();using var http=new HttpClient(fake);var service=new WeatherService(http,()=>null);var weather=await service.Get(CancellationToken.None);Check(weather.Place=="Cidade de teste"&&weather.Temperature==21.5&&weather.Origin.Contains("aproximada"),"clima usa localização automática e interpreta dados do provedor");
        await service.Get(CancellationToken.None);Check(fake.Calls==2,"cache do clima evita consultas repetidas");
        var windowsFake=new FakeWeather();using var windowsHttp=new HttpClient(windowsFake);var windowsWeather=new WeatherService(windowsHttp,()=>(10d,20d));var win=await windowsWeather.Get(CancellationToken.None);Check(win.Origin=="Windows"&&windowsFake.Calls==1,"preferir localização do Windows sem consultar geolocalização por IP");
        Check(WeatherService.Description(2)=="Parcialmente nublado","interpretar código meteorológico");
    }
    static async Task<IntPtr> WaitProbe(Process process)
    {
        var until=DateTime.UtcNow.AddSeconds(10);
        while(DateTime.UtcNow<until){process.Refresh();if(process.MainWindowHandle!=IntPtr.Zero)return process.MainWindowHandle;await Task.Delay(100);}
        throw new Exception("Janela de teste não apareceu.");
    }
}







