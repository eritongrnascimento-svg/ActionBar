using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
namespace BarraInteligente;
internal sealed class Hotkeys : IDisposable
{
 internal int PrimaryId {get;private set;}
 private readonly IntPtr hwnd;private readonly Dictionary<int,Action> actions=new(); private int id=100;
 internal Hotkeys(IntPtr hwnd){this.hwnd=hwnd;}
 internal static bool Parse(string chord,out uint modifiers,out uint key)
 {
  modifiers=0;key=0;var parts=chord.Split('+',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);if(parts.Length<2)return false;
  foreach(var part in parts[..^1])switch(part.ToLowerInvariant()){case "alt":modifiers|=1;break;case "ctrl":case "control":modifiers|=2;break;case "shift":modifiers|=4;break;default:return false;}
  try{var token=parts[^1];if(token.Equals("Espaço",StringComparison.OrdinalIgnoreCase))token="Space";if(!Enum.TryParse<Key>(token,true,out var k))return false;key=(uint)KeyInterop.VirtualKeyFromKey(k);return key!=0 && k is not Key.LeftAlt and not Key.RightAlt and not Key.LeftCtrl and not Key.RightCtrl and not Key.LeftShift and not Key.RightShift;}catch{return false;}
 }
 internal string? Configure(Settings settings,Action toggle,Action<string> run)
 {
  Clear(); PrimaryId=0; var chords=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  foreach(var binding in new[]{new ShortcutBinding{Name="Abrir barra",Chord=settings.Hotkey}}.Concat(settings.Shortcuts))
  {
   if(!Parse(binding.Chord,out var mods,out var key)){Clear();return $"Atalho inválido: {binding.Name}. Use Ctrl, Alt ou Shift + uma tecla.";}
   if(!chords.Add(mods+":"+key)){Clear();return "Atalho repetido: "+binding.Chord;}
   var number=++id;if(!Native.RegisterHotKey(hwnd,number,mods|0x4000,key)){Clear();return "Atalho ocupado ou reservado: "+binding.Chord;}
   if(PrimaryId==0){PrimaryId=number;actions[number]=toggle;}else actions[number]=()=>run(binding.Target);
  }
  return null;
 }
 internal bool Dispatch(int number){if(!actions.TryGetValue(number,out var action))return false;action();return true;}
 internal string Probe(string chord)
 {
  if(!Parse(chord,out var modifiers,out var key))return "Combinação inválida ou reservada ao Windows.";
  var probe=++id;if(!Native.RegisterHotKey(hwnd,probe,modifiers|0x4000,key))return "Ocupado, já usado pela barra ou reservado pelo sistema.";Native.UnregisterHotKey(hwnd,probe);return "Disponível para registro neste momento.";
 }
 private void Clear(){foreach(var number in actions.Keys)Native.UnregisterHotKey(hwnd,number);actions.Clear();}
 public void Dispose()=>Clear();
}

