using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
namespace BarraInteligente;
// O que o usuário pediu: texto, unidade preferida ("obs d", "d: obs", "obs no d") e tipo ("pasta obs").
internal sealed record FileQuery(string Text,string[] Tokens,char? Drive,bool? FoldersOnly)
{
 private static readonly HashSet<string> DriveWords=new(){"unidade","disco","drive","no","na","em"};
 internal static FileQuery Parse(string query,Func<char,bool> driveExists)
 {
  var q=Commands.Normalize(query);bool? folders=null;
  if(Regex.Match(q,@"^(pastas?|arquivos?) (.+)$") is {Success:true} kind){folders=kind.Groups[1].Value.StartsWith("pasta");q=kind.Groups[2].Value;}
  char? drive=null;
  // "d:obs" (sem barra, então não é um caminho digitado)
  if(Regex.Match(q,@"^([a-z]):([^\\/].*)$") is {Success:true} glued && driveExists(glued.Groups[1].Value[0])){drive=glued.Groups[1].Value[0];q=glued.Groups[2].Value.Trim();}
  var words=q.Split(' ',StringSplitOptions.RemoveEmptyEntries).ToList();
  if(drive==null && words.Count>1)
  {
   // Uma letra isolada (com ou sem ":") no começo ou no fim que corresponde a uma unidade existente.
   foreach(var index in new[]{words.Count-1,0})
   {
    var m=Regex.Match(words[index],@"^([a-z]):?$");if(!m.Success || !driveExists(m.Groups[1].Value[0]))continue;
    drive=m.Groups[1].Value[0];words.RemoveAt(index);
    // "obs no d", "obs na unidade d" → também remove as palavras de ligação antes da letra.
    if(index>0)while(words.Count>1 && DriveWords.Contains(words[^1]))words.RemoveAt(words.Count-1);
    break;
   }
  }
  var text=string.Join(' ',words);return new FileQuery(text,words.ToArray(),drive,folders);
 }
}
// Fatos sobre as pastas, calculados uma vez por índice:
//  • raízes de projetos de código (onde há CMakeLists.txt, package.json, .csproj…): o conteúdo interno raramente é o que se procura;
//  • quantos arquivos pessoais (vídeos, fotos, documentos) cada pasta guarda diretamente.
internal sealed class FolderFacts
{
 private static readonly HashSet<string> Markers=new(StringComparer.OrdinalIgnoreCase){"cmakelists.txt","package.json","pyproject.toml","setup.py","cargo.toml","go.mod","pom.xml","build.gradle","makefile","meson.build","composer.json","gemfile"};
 private static readonly HashSet<string> ProjectExtensions=new(StringComparer.OrdinalIgnoreCase){".sln",".csproj",".vcxproj",".xcodeproj",".uproject"};
 private readonly HashSet<string> projects=new(StringComparer.OrdinalIgnoreCase);private readonly Dictionary<string,int> media=new(StringComparer.OrdinalIgnoreCase);
 internal static readonly FolderFacts Empty=new();
 internal static FolderFacts Build(IEnumerable<FileEntry> entries)
 {
  var facts=new FolderFacts();
  foreach(var e in entries)
  {
   if(e.Folder)continue;var parent=Path.GetDirectoryName(e.Path);if(parent==null)continue;var ext=Path.GetExtension(e.Name);
   if(Markers.Contains(e.Name) || ProjectExtensions.Contains(ext))facts.projects.Add(parent);
   else if(FileRanking.IsPersonal(ext))facts.media[parent]=facts.media.GetValueOrDefault(parent)+1;
  }
  return facts;
 }
 // Está dentro (não é a própria raiz) de um projeto de código?
 internal bool InsideProject(string path){for(var p=Path.GetDirectoryName(path);p!=null && p.Length>3;p=Path.GetDirectoryName(p))if(projects.Contains(p))return true;return false;}
 internal bool IsProject(string folder)=>projects.Contains(folder);
 internal int Media(string folder)=>media.GetValueOrDefault(folder);
}
// Pontuação dos resultados de arquivos. Ideia geral: o nome vale muito mais que o caminho,
// pastas e arquivos do usuário valem mais que pastas internas de programas, itens rasos valem mais
// que profundos, e o que já foi aberto pela barra sobe.
internal static class FileRanking
{
 // Pastas típicas de instalação, código compilado e dados internos: raramente são o que se procura.
 private static readonly HashSet<string> Noise=new(StringComparer.OrdinalIgnoreCase){"program files","program files (x86)","programdata","appdata","windows","steamapps","common","node_modules","site-packages","packages","vendor","build","build_native","cmakefiles","rundir","relwithdebinfo","debug","release","obj","bin","lib","lib64","share","src","include","core","locale","locales","resources","cache","caches","temp","tmp","data","assets","dist","out","x64","x86","runtimes","modules","extensions","__pycache__","venv","dependencies"};
 private static readonly HashSet<string> Junk=new(StringComparer.OrdinalIgnoreCase){".dll",".ini",".json",".dat",".lock",".log",".pdb",".manifest",".mui",".o",".d",".h",".c",".cpp",".hpp",".cmake",".so",".lib",".pak",".bin",".tmp",".cache",".xml",".yml",".yaml",".ts",".map",".pyc",".sys",".cat",".inf",".etl",".db",".sqlite",".idx",".cfg",".vdf",".acf",".qm",".ttf",".otf",".svg",".css",".js"};
 private static readonly HashSet<string> Personal=new(StringComparer.OrdinalIgnoreCase){".mp4",".mkv",".mov",".avi",".webm",".flv",".mp3",".wav",".flac",".m4a",".jpg",".jpeg",".png",".gif",".webp",".heic",".psd",".pdf",".doc",".docx",".xls",".xlsx",".ppt",".pptx",".odt",".txt",".md",".zip",".rar",".7z",".iso",".exe",".msi",".lnk",".prproj",".blend"};
 private static readonly string Profile=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\');
 // Palavras do nome: início, ou depois de qualquer caractere que não seja letra/número ("obs-studio", "OBS Stúdio", "gravacao_obs").
 internal static bool AtWordStart(string text,string token)
 {
  for(var i=text.IndexOf(token,StringComparison.Ordinal);i>=0;i=text.IndexOf(token,i+1,StringComparison.Ordinal))
   if(i==0 || !char.IsLetterOrDigit(text[i-1]) || (char.IsDigit(text[i-1])!=char.IsDigit(text[i])))return true;
  return false;
 }
 internal static bool IsPersonal(string extension)=>Personal.Contains(extension);
 internal static int Score(FileEntry e,FileQuery q,IReadOnlyCollection<string>? favorites=null,FolderFacts? facts=null)
 {
  facts??=FolderFacts.Empty;
  var name=e.SearchName;var stem=e.Folder?name:Commands.Normalize(Path.GetFileNameWithoutExtension(e.Name));var text=q.Text;var score=0;
  if(text.Length>0 && (stem==text || name==text))score=1000;
  else if(text.Length>0 && name.StartsWith(text,StringComparison.Ordinal))score=800;
  else if(q.Tokens.All(t=>AtWordStart(name,t)))score=650;
  else if(q.Tokens.All(t=>name.Contains(t,StringComparison.Ordinal)))score=350;
  else
  {
   // Só o caminho corresponde: bom quando o termo é o nome de uma pasta acima ("obs" → vídeos dentro de "OBS Stúdio").
   score=100;var parents=Path.GetDirectoryName(e.SearchPath)??"";if(q.Tokens.Any(t=>parents.Split('\\').Any(s=>AtWordStart(s,t))))score+=250;
  }
  if(e.Folder && score>=650 && q.FoldersOnly!=false)score+=120;
  if(q.Drive is char drive && e.Path.Length>1 && char.ToLowerInvariant(e.Path[0])==drive && e.Path[1]==':')score+=900;
  // Já aberto pela barra: ajuda a desempatar, mas uma abertura isolada não deve passar na frente de tudo.
  // A lista vem do mais recente para o mais antigo; os primeiros valem mais.
  if(favorites!=null){var rank=0;foreach(var f in favorites){var weight=Math.Max(0.4,1-rank++*0.1);if(e.Path.Equals(f,StringComparison.OrdinalIgnoreCase)){score+=(int)(250*weight);break;}if(e.Path.StartsWith(f.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase)){score+=(int)(80*weight);break;}}}
  // Profundidade conta a partir da pasta do usuário quando o item está nela (Documentos/Downloads não ficam "fundos").
  var relative=e.Path.StartsWith(Profile+"\\",StringComparison.OrdinalIgnoreCase)?e.Path[(Profile.Length+1)..]:e.Path[Math.Min(3,e.Path.Length)..];
  var segments=relative.Split('\\',StringSplitOptions.RemoveEmptyEntries);score-=35*Math.Min(segments.Length-1,12);
  var penalty=0;foreach(var s in segments.Take(segments.Length-1)){if(s.StartsWith('.') || s.StartsWith('$'))penalty+=200;else if(Noise.Contains(s) || s.Contains("plugin",StringComparison.OrdinalIgnoreCase))penalty+=120;}
  if(e.Path.StartsWith(Profile+"\\AppData",StringComparison.OrdinalIgnoreCase))penalty+=200;
  score-=Math.Min(penalty,480);
  // Conteúdo interno de projetos de código perde; a raiz do projeto continua encontrável normalmente.
  if(facts.InsideProject(e.Path))score-=300;
  // Pastas que guardam vídeos, fotos ou documentos são as que a pessoa costuma procurar.
  if(e.Folder){var count=facts.Media(e.Path);score+=count>=20?180:count>=3?120:count>0?40:0;}
  if(!e.Folder){var ext=Path.GetExtension(e.Name);if(Junk.Contains(ext))score-=150;else if(Personal.Contains(ext))score+=60;}
  return score;
 }
 // Desempate pelos mais recentes entre os melhores (lê a data só de poucos itens).
 internal static int Recency(string path,DateTime now)
 {
  try{var changed=Directory.Exists(path)?Directory.GetLastWriteTime(path):File.GetLastWriteTime(path);var days=(now-changed).TotalDays;return days<=7?40:days<=30?20:days<=180?5:0;}catch{return 0;}
 }
}
