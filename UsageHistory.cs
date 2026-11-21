using System;
using System.Collections.Generic;
using System.Linq;
namespace BarraInteligente;
public sealed class UsageEntry
{
 public string Category {get;set;}="";public string Id {get;set;}="";public string Title {get;set;}="";public string Target {get;set;}="";public DateTimeOffset UsedAt {get;set;}
}
internal static class UsageHistory
{
 internal static void Record(Settings settings,string category,string id,string title,string target)
 {
  if(!settings.HistoryEnabled)return;
  settings.History.RemoveAll(e=>e.Category==category && e.Id.Equals(id,StringComparison.OrdinalIgnoreCase));settings.History.Insert(0,new UsageEntry{Category=category,Id=id,Title=title,Target=target,UsedAt=DateTimeOffset.UtcNow});if(settings.History.Count>200)settings.History.RemoveRange(200,settings.History.Count-200);
 }
 internal static IReadOnlyList<UsageEntry> Recent(Settings settings,string category="")=>!settings.HistoryEnabled?Array.Empty<UsageEntry>():settings.History.Where(e=>category.Length==0||e.Category==category).OrderByDescending(e=>e.UsedAt).Take(3).ToArray();
}
