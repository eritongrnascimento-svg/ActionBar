using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace BarraInteligente;
internal static class ShellIcons
{
 private static readonly ConcurrentDictionary<string,Task<ImageSource?>> cache=new(StringComparer.OrdinalIgnoreCase);
 private static readonly System.Threading.SemaphoreSlim workers=new(3);
 internal static Task<ImageSource?> Get(string target){Trim();return cache.GetOrAdd(target,Load);}
 // Miniatura real (imagens, vídeos, PDFs…) quando o Windows tem uma; senão, o ícone do tipo de arquivo.
 internal static Task<ImageSource?> Thumbnail(string path){Trim();return cache.GetOrAdd("thumb|"+path,async _=>{if(string.IsNullOrWhiteSpace(path))return null;await workers.WaitAsync();try{return await Task.Run(()=>ExtractShell(path,96,0x0)??Extract(path));}finally{workers.Release();}});}
 // Evita crescer sem limite durante sessões longas.
 private static void Trim(){if(cache.Count>800)cache.Clear();}
 private static async Task<ImageSource?> Load(string target)
 {
  if(string.IsNullOrWhiteSpace(target))return null;await workers.WaitAsync();try{return await Task.Run(()=>Extract(target));}finally{workers.Release();}
 }
 private static ImageSource? Extract(string target)
{
 var image=ExtractShell(target,40,0x4|0x1);if(image!=null)return image;
 try{if(System.IO.File.Exists(target)){using var icon=System.Drawing.Icon.ExtractAssociatedIcon(target);if(icon!=null){var bitmap=Imaging.CreateBitmapSourceFromHIcon(icon.Handle,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());bitmap.Freeze();return bitmap;}}}catch{}return null;
}
 private static ImageSource? ExtractShell(string target,int pixels,uint flags)
 {
  IShellItemImageFactory? factory=null;IntPtr bitmap=IntPtr.Zero;
  try{var iid=typeof(IShellItemImageFactory).GUID;if(SHCreateItemFromParsingName(target,IntPtr.Zero,ref iid,out factory)!=0 || factory==null)return null;var size=new Size{X=pixels,Y=pixels};if(factory.GetImage(size,flags,out bitmap)!=0 || bitmap==IntPtr.Zero)return null;var image=Imaging.CreateBitmapSourceFromHBitmap(bitmap,IntPtr.Zero,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());image.Freeze();return image;}catch{return null;}finally{if(bitmap!=IntPtr.Zero)DeleteObject(bitmap);if(factory!=null)Marshal.ReleaseComObject(factory);}
 }
 [StructLayout(LayoutKind.Sequential)] private struct Size{internal int X,Y;}
 [ComImport,Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IShellItemImageFactory{[PreserveSig]int GetImage(Size size,uint flags,out IntPtr image);}
 [DllImport("shell32.dll",CharSet=CharSet.Unicode)] private static extern int SHCreateItemFromParsingName(string path,IntPtr bind,ref Guid iid,out IShellItemImageFactory factory);
 [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);
}

