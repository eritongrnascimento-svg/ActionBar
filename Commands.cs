using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace BarraInteligente;
internal static class Commands
{
    internal static string Normalize(string value) => new string(value.ToLowerInvariant().Normalize(NormalizationForm.FormD).Where(c=>CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark).ToArray()).Normalize(NormalizationForm.FormC).Trim();
    internal static void Open(string target) => Process.Start(new ProcessStartInfo(target){UseShellExecute=true});
    internal static void Search(string query) => Open("https://www.google.com/search?q="+Uri.EscapeDataString(query));
    internal static void Theme(bool dark)
    {
        using var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        key.SetValue("AppsUseLightTheme",dark?0:1,RegistryValueKind.DWord); key.SetValue("SystemUsesLightTheme",dark?0:1,RegistryValueKind.DWord);
        BroadcastTheme();
    }
    internal static void Transparency(bool enabled)
    {
        using var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");key.SetValue("EnableTransparency",enabled?1:0,RegistryValueKind.DWord);BroadcastTheme();
    }
    private static void BroadcastTheme()
    {
        var pointer=Marshal.StringToHGlobalUni("ImmersiveColorSet");
        try { SendMessageTimeout(new IntPtr(0xffff),0x1a,IntPtr.Zero,pointer,2,1000,out _); } finally {Marshal.FreeHGlobal(pointer);}
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr SendMessageTimeout(IntPtr h,uint msg,IntPtr w,IntPtr l,uint flags,uint timeout,out IntPtr result);
    internal static int? VolumeValue(string text) { var match=Regex.Match(Normalize(text),@"\bvolume\s*(?:para\s*|em\s*|a\s*)?(\d{1,3})\s*%?$"); return match.Success && int.TryParse(match.Groups[1].Value,out var n) && n<=100?n:null; }
    internal static void Volume(int value) => Audio.SetVolume(value);
    internal static List<(string Name,string Path)> IndexApps()
    {
        var list=new List<(string,string)>();
        foreach(var root in new[]{Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)})
        { try { foreach(var file in Directory.EnumerateFiles(root,"*.lnk",SearchOption.AllDirectories)) list.Add((Path.GetFileNameWithoutExtension(file),file)); } catch(UnauthorizedAccessException) {} catch(IOException) {} }
        return list.DistinctBy(a=>a.Item2,StringComparer.OrdinalIgnoreCase).OrderBy(a=>a.Item1).ToList();
    }
}

