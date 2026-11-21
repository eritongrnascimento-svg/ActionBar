using System;
using System.Collections.Generic;
using System.IO;
namespace BarraInteligente;
public sealed class Appearance
{
 public string Theme {get;set;}="Escuro"; public string Font {get;set;}="Segoe UI Variable Text";
 public double Width {get;set;}=780; public double Height {get;set;}=550; public double FontSize {get;set;}=23;
 public bool Animations {get;set;}=true; public int AnimationMs {get;set;}=180; public bool Glass {get;set;}=true;
 public string SearchIcon {get;set;}="⌕"; public string BackgroundImage {get;set;}=""; public double ImageOpacity {get;set;}=.18;
}
public sealed class ShortcutBinding { public string Name {get;set;}=""; public string Chord {get;set;}=""; public string Target {get;set;}=""; }
public sealed class Settings
{
 public bool CenterWindows {get;set;}=true; public List<Workspace> Workspaces {get;set;}=new();
 public List<string> IndexRoots {get;set;}=new(){Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads")};
 public bool IndexFiles {get;set;}=true; public bool AutoIndex {get;set;}=true; public int IndexLimit {get;set;}=500000;
 public string Browser {get;set;}=""; public string SearchEngine {get;set;}="Google";
 public Appearance Appearance {get;set;}=new(); public bool Calculator {get;set;}=true;
 public bool Notifications {get;set;}=true; public bool NotificationAnimation {get;set;}=true; public int NotificationSeconds {get;set;}=4; public string NotificationPosition {get;set;}="Inferior direita";
 public string Hotkey {get;set;}="Alt+Space"; public List<ShortcutBinding> Shortcuts {get;set;}=new();
 public bool StartWithWindows {get;set;}=false; public string StartupMode {get;set;}="Bandeja";
 public string Priority {get;set;}="Normal"; public bool HideOnBlur {get;set;}=true; public bool ClearOnOpen {get;set;}=false;
 public bool HistoryEnabled {get;set;}
 public List<UsageEntry> History {get;set;}=new();public bool WeatherEnabled {get;set;}=true;
 public bool AlwaysOnTop {get;set;}=true;
}


