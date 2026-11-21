using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
namespace BarraInteligente;
internal sealed record FileEntry(string Name,string Path,bool Folder)
{
 [System.Text.Json.Serialization.JsonIgnore] public string SearchName {get;}=Commands.Normalize(Name);
 [System.Text.Json.Serialization.JsonIgnore] public string SearchPath {get;}=Commands.Normalize(Path);
}
internal sealed class FileIndex : IDisposable
{
 private readonly Store store; private CancellationTokenSource? cancellation; private FileEntry[] entries=Array.Empty<FileEntry>(); private readonly object gate=new(); private Task? running;
 internal string Status {get;private set;}="Índice não iniciado"; internal int Count=>entries.Length; internal event Action? Changed;
 private string Cache=>System.IO.Path.Combine(System.IO.Path.GetDirectoryName(store.FilePath)!,"files.json");
 internal FileIndex(Store store){this.store=store;}
 internal async Task Load(){try{if(File.Exists(Cache)){var data=await Task.Run(()=>JsonSerializer.Deserialize<CacheData>(File.ReadAllText(Cache)));if(data!=null && data.Roots.SequenceEqual(store.Data.IndexRoots,StringComparer.OrdinalIgnoreCase)){entries=data.Entries;Status=$"{Count:N0} itens indexados";Changed?.Invoke();}}}catch{Status="Índice será reconstruído";}}
 public sealed class CacheData {public string[] Roots {get;set;}=Array.Empty<string>();public FileEntry[] Entries {get;set;}=Array.Empty<FileEntry>();}
 internal Task Rebuild()
 {
  if(running is {IsCompleted:false}){cancellation?.Cancel();return Restart();}
  cancellation?.Dispose(); cancellation=new CancellationTokenSource();var token=cancellation.Token;var roots=store.Data.IndexRoots.ToArray();var max=Math.Clamp(store.Data.IndexLimit,1000,2000000);
  Status="Indexando…";Changed?.Invoke();
  running=Task.Run(()=>
  {
   var found=new List<FileEntry>();var visited=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var skipped=0;
   var excluded=new HashSet<string>(new[]{"Windows","$Recycle.Bin","System Volume Information","node_modules",".git","obj","AppData","ProgramData","WinSxS"},StringComparer.OrdinalIgnoreCase);
   foreach(var root in roots)
   {
    var pending=new Stack<string>();if(Directory.Exists(root))pending.Push(root);
    while(pending.Count>0 && found.Count<max)
    {
     token.ThrowIfCancellationRequested();var dir=pending.Pop();if(!visited.Add(dir.TrimEnd('\\')))continue;
     try
     {
      foreach(var path in Directory.EnumerateFileSystemEntries(dir,"*",new EnumerationOptions{IgnoreInaccessible=true,RecurseSubdirectories=false,AttributesToSkip=FileAttributes.ReparsePoint|FileAttributes.System}))
      {
       token.ThrowIfCancellationRequested();try{var attributes=File.GetAttributes(path);var folder=(attributes&FileAttributes.Directory)!=0;var name=System.IO.Path.GetFileName(path);if(folder && excluded.Contains(name))continue;found.Add(new(name,path,folder));if(folder)pending.Push(path);if(found.Count%10000==0){if(entries.Length==0)entries=found.ToArray();/* com um índice anterior completo, a busca continua nele até o novo terminar */Status=$"Indexando… {found.Count:N0} itens";Changed?.Invoke();}if(found.Count>=max)break;}catch(IOException){skipped++;}catch(UnauthorizedAccessException){skipped++;}
      }
     }catch(IOException){skipped++;}catch(UnauthorizedAccessException){skipped++;}
    }
   }
   token.ThrowIfCancellationRequested();entries=found.ToArray();Status=$"{Count:N0} itens • {(found.Count>=max?"limite atingido":"atualizado às "+DateTime.Now.ToString("HH:mm"))}";
   Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Cache)!);var temp=Cache+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(new CacheData{Roots=roots,Entries=entries}));File.Move(temp,Cache,true);Changed?.Invoke();
  },token);
  return Observe(running);
 }
 private async Task Observe(Task task){try{await task;}catch(OperationCanceledException){Status="Indexação interrompida";Changed?.Invoke();}catch(Exception ex){Status="Índice: "+ex.Message;Changed?.Invoke();}}
 private async Task Restart(){var previous=running!;cancellation?.Cancel();await Observe(previous);await Rebuild();}
 internal void Cancel()=>cancellation?.Cancel();
 private static DateTime drivesRead;private static HashSet<char> drives=new();
 // Unidades presentes agora (atualizado a cada 30 s), usadas para reconhecer "obs d".
 internal static bool DriveExists(char letter)
 {
  if(DateTime.UtcNow-drivesRead>TimeSpan.FromSeconds(30)){try{drives=DriveInfo.GetDrives().Select(d=>char.ToLowerInvariant(d.Name[0])).ToHashSet();}catch{}drivesRead=DateTime.UtcNow;}
  return drives.Contains(char.ToLowerInvariant(letter));
 }
 private (FileEntry[] Source,FolderFacts Facts)? facts;
 // Recalculado só quando o índice muda (uma passada linear pelo índice).
 private FolderFacts Facts(FileEntry[] snapshot){lock(gate){if(facts is {} f && ReferenceEquals(f.Source,snapshot))return f.Facts;var built=FolderFacts.Build(snapshot);facts=(snapshot,built);return built;}}
 internal IReadOnlyList<FileEntry> Find(string query,int limit=12,CancellationToken token=default,IReadOnlyCollection<string>? favorites=null)
 {
  if(!store.Data.IndexFiles || query.Length<2)return Array.Empty<FileEntry>();
  var q=FileQuery.Parse(query,DriveExists);if(q.Tokens.Length==0 || q.Text.Length<2 && q.Drive==null)return Array.Empty<FileEntry>();
  var roots=store.Data.IndexRoots.Select(r=>r.TrimEnd('\\')+"\\").ToArray();var snapshot=entries;var examined=0;var facts=Facts(snapshot);
  var matches=new List<(FileEntry Entry,int Score)>();
  foreach(var e in snapshot)
  {
   if((++examined & 4095)==0)token.ThrowIfCancellationRequested();
   if(q.FoldersOnly is bool folders && e.Folder!=folders)continue;
   if(!q.Tokens.All(t=>e.SearchName.Contains(t) || e.SearchPath.Contains(t)))continue;
   if(!roots.Any(root=>e.Path.StartsWith(root,StringComparison.OrdinalIgnoreCase)))continue;
   matches.Add((e,FileRanking.Score(e,q,favorites,facts)));
  }
  token.ThrowIfCancellationRequested();var now=DateTime.Now;
  // Data de modificação só para os melhores candidatos: desempata a favor do que foi mexido recentemente.
  return matches.OrderByDescending(m=>m.Score).Take(Math.Max(limit*3,60)).Select(m=>(m.Entry,Score:m.Score+FileRanking.Recency(m.Entry.Path,now)))
   .OrderByDescending(m=>m.Score).ThenBy(m=>m.Entry.Name.Length).Take(limit).Select(m=>m.Entry).ToArray();
 }
 public void Dispose(){cancellation?.Cancel();}
}


