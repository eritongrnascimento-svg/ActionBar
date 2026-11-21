using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
namespace BarraInteligente;
internal static class Calculator
{
 internal static bool Try(string input,out string result)
 {
  result=""; if(ConvertUnits(input,out result))return true; var text=Commands.Normalize(input).Replace("calcular ","").Replace("calcule ","").Replace("quanto e ","").TrimStart('=',' ').Replace(',','.').Replace('×','*').Replace('÷','/');
  if(!Regex.IsMatch(text,@"\d") || text.Length>240 || !Regex.IsMatch(text,@"^[0-9a-z+*/^().%\s\-]+$"))return false;
  if(!Regex.IsMatch(text,@"[+*/^()%\-]|sqrt|sin|cos|log|abs|pi"))return false;
  try{var parser=new Parser(text);var value=parser.Expression();parser.White();if(parser.Position!=text.Length || !double.IsFinite(value))return false;result=value.ToString("G12",CultureInfo.CurrentCulture);return true;}catch{return false;}
 }
 private static bool ConvertUnits(string input,out string result)
 {
  result="";var text=Commands.Normalize(input);var percentage=Regex.Match(text,@"^([+-]?\d+(?:[.,]\d+)?)\s*%\s+de\s+([+-]?\d+(?:[.,]\d+)?)$");
  if(percentage.Success){var a=double.Parse(percentage.Groups[1].Value.Replace(',','.'),CultureInfo.InvariantCulture);var b=double.Parse(percentage.Groups[2].Value.Replace(',','.'),CultureInfo.InvariantCulture);result=(a*b/100).ToString("G12",CultureInfo.CurrentCulture);return true;}
  var match=Regex.Match(text,@"^([+-]?\d+(?:[.,]\d+)?)\s*([a-z°]+)\s+(?:em|para|to)\s+([a-z°]+)$");if(!match.Success)return false;
  var units=new Dictionary<string,(double Scale,string Kind,double Offset)>();
  void Add(string names,double scale,string kind,double offset=0){foreach(var name in names.Split('|'))units[name]=(scale,kind,offset);}
  Add("m|metro|metros",1,"length");Add("cm|centimetro|centimetros",.01,"length");Add("mm|milimetro|milimetros",.001,"length");Add("km|quilometro|quilometros",1000,"length");Add("in|polegada|polegadas",.0254,"length");Add("ft|pe|pes",.3048,"length");Add("mi|milha|milhas",1609.344,"length");
  Add("kg|quilo|quilos|quilogramas",1,"mass");Add("g|grama|gramas",.001,"mass");Add("mg|miligramas",.000001,"mass");Add("lb|lbs|libra|libras",.45359237,"mass");Add("oz|onca|oncas",.028349523125,"mass");
  Add("s|segundo|segundos",1,"time");Add("min|minuto|minutos",60,"time");Add("h|hora|horas",3600,"time");Add("dia|dias",86400,"time");
  Add("b|byte|bytes",1,"bytes");Add("kb",1000,"bytes");Add("mb",1e6,"bytes");Add("gb",1e9,"bytes");Add("tb",1e12,"bytes");Add("kib",1024,"bytes");Add("mib",1048576,"bytes");Add("gib",1073741824,"bytes");
  Add("c|°c|celsius",1,"temperature",273.15);Add("f|°f|fahrenheit",5d/9,"temperature",255.3722222222222);Add("k|kelvin",1,"temperature");
  if(!units.TryGetValue(match.Groups[2].Value,out var from) || !units.TryGetValue(match.Groups[3].Value,out var to) || from.Kind!=to.Kind)return false;
  var number=double.Parse(match.Groups[1].Value.Replace(',','.'),CultureInfo.InvariantCulture);var value=(number*from.Scale+from.Offset-to.Offset)/to.Scale;if(Math.Abs(value)<1e-10)value=0;if(!double.IsFinite(value))return false;result=value.ToString("G12",CultureInfo.CurrentCulture)+" "+match.Groups[3].Value;return true;
 }
 private sealed class Parser(string text)
 {
  internal int Position; int depth;
  internal void White(){while(Position<text.Length && char.IsWhiteSpace(text[Position]))Position++;}
  bool Eat(char c){White();if(Position<text.Length && text[Position]==c){Position++;return true;}return false;}
  internal double Expression(){if(++depth>40)throw new FormatException();try{var a=Term();while(true){if(Eat('+'))a+=Term();else if(Eat('-'))a-=Term();else return a;}}finally{depth--;}}
  double Term(){var a=Unary();while(true){if(Eat('*'))a*=Unary();else if(Eat('/'))a/=Unary();else return a;}}
  double Unary(){if(++depth>40)throw new FormatException();try{if(Eat('+'))return Unary();if(Eat('-'))return -Unary();return Power();}finally{depth--;}}
  double Power(){var a=Atom();if(Eat('^'))a=Math.Pow(a,Unary());while(Eat('%'))a/=100;return a;}
  double Atom(){White();if(Eat('(')){var a=Expression();if(!Eat(')'))throw new FormatException();return a;}int start=Position;if(Position<text.Length && char.IsLetter(text[Position])){while(Position<text.Length && char.IsLetter(text[Position]))Position++;var f=text[start..Position];if(f=="pi")return Math.PI;if(f=="e")return Math.E;if(!Eat('('))throw new FormatException();var v=Expression();if(!Eat(')'))throw new FormatException();return f switch{"sqrt"=>Math.Sqrt(v),"abs"=>Math.Abs(v),"sin"=>Math.Sin(v),"cos"=>Math.Cos(v),"log"=>Math.Log10(v),"ln"=>Math.Log(v),_=>throw new FormatException()};}while(Position<text.Length && (char.IsDigit(text[Position]) || text[Position]=='.'))Position++;return double.Parse(text[start..Position],CultureInfo.InvariantCulture);}
 }
}

