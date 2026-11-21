using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BarraInteligente;

internal sealed partial class BarPanels
{
    private readonly TextBox folderAddress = Field();
    private readonly ListBox folderEntries = new();
    private readonly Image folderPreview = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock folderStatus = Label();
    private readonly Button wallpaperApply = new() { Content = "Aplicar wallpaper", Padding = new Thickness(8, 4, 8, 4), IsEnabled = false };
    private readonly WrapPanel browseDrives = new();
    private DirectoryInfo? currentFolder;
    private int folderRequest;
    private bool gallery;
    private sealed record FileItem(string Path, bool Directory)
    {
        public override string ToString() => (Directory ? "▸ " : "") + System.IO.Path.GetFileName(Path.TrimEnd('\\'));
    }

    private void BuildFolders()
    {
        var panel = Page("pastas");
        var go = Button("Ir", () => Run(folderStatus, () => Browse(folderAddress.Text)));
        OnEnter(folderAddress, () => Run(folderStatus, () => Browse(folderAddress.Text)));
        folderAddress.Width = 350;
        Top(panel, Row(Button("Subir", () => Run(folderStatus, () => Browse(currentFolder?.Parent?.FullName ?? InitialFolder()))), folderAddress, go));
        Top(panel, Row(Button("Início", () => Run(folderStatus, () => Browse(InitialFolder()))),
            Button("Imagens", () => Run(folderStatus, () => Browse(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)))),
            Button("Galeria de wallpapers", () => Run(folderStatus, ShowWallpapers)), wallpaperApply));
        Top(panel, browseDrives);
        Top(panel, folderStatus);
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition());
        body.ColumnDefinitions.Add(new ColumnDefinition());
        body.Children.Add(folderEntries);
        Grid.SetColumn(folderPreview, 1);
        body.Children.Add(folderPreview);
        folderEntries.MouseDoubleClick += (_, _) => Run(folderStatus, OpenEntry);
        folderEntries.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            Run(folderStatus, OpenEntry);
        };
        folderEntries.SelectionChanged += (_, _) => PreviewEntry();
        wallpaperApply.Click += (_, _) => Run(folderStatus, ApplyWallpaper);
        panel.Children.Add(body);
    }

    private static string HomeFolder() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private string InitialFolder()
    {
        var configured = settings().DefaultBrowseFolder;
        return Directory.Exists(configured) ? configured : HomeFolder();
    }

    private void RefreshBrowseDrives()
    {
        browseDrives.Children.Clear();
        var options = settings();
        foreach (var drive in DriveInfo.GetDrives())
        {
            var root = drive.Name;
            if (options.ShowAllBrowseDrives || options.BrowseDriveRoots.Contains(root, StringComparer.OrdinalIgnoreCase))
                browseDrives.Children.Add(Button(root, () => Run(folderStatus, () => Browse(root))));
        }
    }

    private async Task Browse(string path)
    {
        var version = ++folderRequest;
        if (string.IsNullOrWhiteSpace(path)) path = InitialFolder();
        var directory = new DirectoryInfo(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')));
        folderStatus.Text = "Carregando pasta…";
        var items = await Task.Run(() => directory.EnumerateFileSystemInfos()
            .Select(f => new FileItem(f.FullName, (f.Attributes & FileAttributes.Directory) != 0))
            .OrderByDescending(f => f.Directory).ThenBy(f => f.ToString(), StringComparer.CurrentCultureIgnoreCase)
            .ToArray());
        if (disposed || version != folderRequest) return;
        gallery = false;
        currentFolder = directory;
        folderAddress.Text = directory.FullName;
        ShowEntries(items);
    }

    private static bool IsImage(string path) => Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".bmp";

    private async Task ShowWallpapers()
    {
        var version = ++folderRequest;
        folderStatus.Text = "Procurando imagens…";
        var images = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        var pictures = await Task.Run(() =>
        {
            var result = new List<FileItem>();
            foreach (var root in new[] { images, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Web", "Wallpaper") }.Distinct())
            {
                if (!Directory.Exists(root)) continue;
                var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 4, IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System };
                result.AddRange(Directory.EnumerateFiles(root, "*", options).Where(IsImage).Take(60).Select(path => new FileItem(path, false)));
            }
            return result;
        });
        if (disposed || version != folderRequest) return;
        gallery = true;
        currentFolder = null;
        folderAddress.Clear();
        ShowEntries(pictures);
        if (pictures.Count > 0) folderEntries.SelectedIndex = 0;
    }

    private void ShowEntries(IReadOnlyList<FileItem> items)
    {
        folderEntries.ItemsSource = items;
        folderStatus.Text = items.Count == 0 ? "Nenhum item encontrado." : $"{items.Count} itens · Enter para abrir";
        PreviewEntry();
    }

    private void PreviewEntry()
    {
        folderPreview.Source = null;
        wallpaperApply.IsEnabled = false;
        if (folderEntries.SelectedItem is not FileItem { Directory: false } item || !IsImage(item.Path)) return;
        try
        {
            var bitmap = new BitmapImage();
            using var stream = File.OpenRead(item.Path);
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 440;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            folderPreview.Source = bitmap;
            wallpaperApply.IsEnabled = true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or FormatException) { }
    }

    private Task OpenEntry()
    {
        if (folderEntries.SelectedItem is not FileItem item) return Task.CompletedTask;
        if (item.Directory) return Browse(item.Path);
        if (!gallery) Commands.Open(item.Path);
        return Task.CompletedTask;
    }

    private Task ApplyWallpaper()
    {
        if (folderEntries.SelectedItem is not FileItem item || folderPreview.Source == null) return Task.CompletedTask;
        var desktop = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD"), throwOnError: true)!)!;
        try { ((IDesktopWallpaper)desktop).SetWallpaper(null, item.Path); }
        finally { Release(desktop); }
        folderStatus.Text = "Papel de parede aplicado.";
        return Task.CompletedTask;
    }

    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitor,
            [MarshalAs(UnmanagedType.LPWStr)] string path);
    }

    private void BuildUpdates()
    {
        var panel = Page("atualizacoes");
        var status = Label("Consulte as atualizações disponíveis para este computador.");
        var progress = new ProgressBar { Height = 6, Margin = new Thickness(4, 8, 4, 8), Visibility = Visibility.Collapsed };
        var list = new ListBox();
        var check = new Button { Content = "Verificar atualizações", Padding = new Thickness(9, 4, 9, 4), Margin = new Thickness(4) };
        check.Click += (_, _) => Run(status, async () =>
        {
            check.IsEnabled = false;
            progress.Visibility = Visibility.Visible;
            progress.Value = 0;
            progress.IsIndeterminate = true;
            status.Text = "Consultando o Windows Update…";
            list.ItemsSource = null;
            try
            {
                var (updates, partial) = await Task.Run(SearchUpdates);
                if (disposed) return;
                list.ItemsSource = updates;
                status.Text = partial ? $"Consulta parcial · {updates.Count} atualizações encontradas."
                    : updates.Count == 0 ? "Nenhuma atualização pendente." : $"{updates.Count} atualizações disponíveis.";
                progress.Value = 100;
            }
            catch { progress.Visibility = Visibility.Collapsed; throw; }
            finally
            {
                progress.IsIndeterminate = false;
                check.IsEnabled = true;
            }
        });
        Top(panel, Row(check, Button("Configurações do Windows Update", () => Commands.Open("ms-settings:windowsupdate"))));
        Top(panel, status);
        Top(panel, progress);
        panel.Children.Add(list);
    }

    private static (List<string> Updates, bool Partial) SearchUpdates()
    {
        object? session = null, searcher = null, result = null, collection = null;
        try
        {
            session = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.Session", throwOnError: true)!)!;
            searcher = ((dynamic)session).CreateUpdateSearcher();
            result = ((dynamic)searcher).Search("IsInstalled=0 and IsHidden=0");
            int code = ((dynamic)result).ResultCode;
            if (code is not (2 or 3)) throw new InvalidOperationException($"O Windows Update não concluiu a consulta (código {code}).");
            collection = ((dynamic)result).Updates;
            int count = ((dynamic)collection).Count;
            var titles = new List<string>(count);
            for (int index = 0; index < count; index++)
            {
                object update = ((dynamic)collection).Item(index);
                try { titles.Add((string)((dynamic)update).Title); }
                finally { Release(update); }
            }
            return (titles, code == 3);
        }
        finally
        {
            Release(collection);
            Release(result);
            Release(searcher);
            Release(session);
        }
    }

    private static void Release(object? instance)
    {
        if (instance != null && Marshal.IsComObject(instance)) Marshal.ReleaseComObject(instance);
    }
}

