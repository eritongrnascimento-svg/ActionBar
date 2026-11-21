using System;
using System.Collections.Generic;
using System.Linq;
namespace BarraInteligente;
internal static class ResultRanking
{
 // Os três itens recentes de cada categoria sobem para o topo. Exceção: arquivos com texto digitado,
 // em que a ordem vem da pontuação do índice (que já considera o histórico); lá o rótulo RECENTE só identifica.
 internal static List<Result> Apply(IEnumerable<Result> results,Settings settings,string category,bool hasQuery=false)
 {
  var recent=UsageHistory.Recent(settings,category).ToList();var items=results.DistinctBy(r=>r.Category+"|"+r.Id,StringComparer.OrdinalIgnoreCase).ToList();
  foreach(var item in items)item.IsRecent=recent.Any(e=>e.Category==item.Category && e.Id.Equals(item.Id,StringComparison.OrdinalIgnoreCase));
  return items.OrderBy(r=>{if(hasQuery && r.Category=="files")return int.MaxValue;var rank=recent.FindIndex(e=>e.Category==r.Category&&e.Id.Equals(r.Id,StringComparison.OrdinalIgnoreCase));return rank<0?int.MaxValue:rank;}).ToList();
 }
}
