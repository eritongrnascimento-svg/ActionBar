using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
namespace BarraInteligente;
internal static class Layouts
{
 internal static readonly string[] Names={"Duas colunas","Principal + duas laterais","Três colunas","Grade 2 × 2","Duas linhas","Seis painéis"};
 internal static List<Rectangle> Cells(int preset,Rectangle area)
 {
  var fractions=preset switch
  {
   0=>new[]{(0d,0d,.5,1d),(.5,0d,.5,1d)},
   1=>new[]{(0d,0d,.65,1d),(.65,0d,.35,.5),(.65,.5,.35,.5)},
   2=>new[]{(0d,0d,1d/3,1d),(1d/3,0d,1d/3,1d),(2d/3,0d,1d/3,1d)},
   3=>new[]{(0d,0d,.5,.5),(.5,0d,.5,.5),(0d,.5,.5,.5),(.5,.5,.5,.5)},
   4=>new[]{(0d,0d,1d,.5),(0d,.5,1d,.5)},
   _=>new[]{(0d,0d,1d/3,.5),(1d/3,0d,1d/3,.5),(2d/3,0d,1d/3,.5),(0d,.5,1d/3,.5),(1d/3,.5,1d/3,.5),(2d/3,.5,1d/3,.5)}
  };
  return fractions.Select(f=>new Rectangle(area.Left+(int)Math.Round(f.Item1*area.Width),area.Top+(int)Math.Round(f.Item2*area.Height),(int)Math.Round((f.Item1+f.Item3)*area.Width)-(int)Math.Round(f.Item1*area.Width),(int)Math.Round((f.Item2+f.Item4)*area.Height)-(int)Math.Round(f.Item2*area.Height))).ToList();
 }
}
