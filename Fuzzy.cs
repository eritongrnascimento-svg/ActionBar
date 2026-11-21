using System;
using System.Linq;
namespace BarraInteligente;
// Correspondência tolerante a erros de digitação ("blutooth" → "bluetooth").
// Só é usada quando a busca exata não encontra nada, para não poluir resultados bons.
internal static class Fuzzy
{
 // Distância de Damerau-Levenshtein restrita (inclui troca de letras vizinhas).
 internal static int Distance(string a,string b)
 {
  var d=new int[a.Length+1,b.Length+1];
  for(var i=0;i<=a.Length;i++)d[i,0]=i;for(var j=0;j<=b.Length;j++)d[0,j]=j;
  for(var i=1;i<=a.Length;i++)for(var j=1;j<=b.Length;j++)
  {
   var cost=a[i-1]==b[j-1]?0:1;d[i,j]=Math.Min(Math.Min(d[i-1,j]+1,d[i,j-1]+1),d[i-1,j-1]+cost);
   if(i>1 && j>1 && a[i-1]==b[j-2] && a[i-2]==b[j-1])d[i,j]=Math.Min(d[i,j],d[i-2,j-2]+1);
  }
  return d[a.Length,b.Length];
 }
 // Palavras curtas exigem grafia exata; médias toleram 1 erro; longas, 2.
 internal static int Tolerance(int length)=>length<4?0:length<7?1:2;
 // Cada termo precisa aparecer no texto ou ficar perto de uma palavra (inteira ou do mesmo tamanho do termo, para quem ainda está digitando).
 internal static bool Matches(string normalizedText,string[] tokens,out int cost)
 {
  cost=0;var words=normalizedText.Split(' ',StringSplitOptions.RemoveEmptyEntries);
  foreach(var token in tokens)
  {
   if(normalizedText.Contains(token))continue;var tolerance=Tolerance(token.Length);if(tolerance==0 || words.Length==0)return false;
   var best=words.Min(w=>Math.Min(Distance(token,w),w.Length>token.Length?Distance(token,w[..token.Length]):int.MaxValue));
   if(best>tolerance)return false;cost+=best;
  }
  return true;
 }
}
