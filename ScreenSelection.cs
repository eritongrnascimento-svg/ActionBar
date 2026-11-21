using System;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace BarraInteligente;

internal static class ScreenSelection
{
    internal static System.Drawing.Rectangle? Select(System.Drawing.Bitmap screenshot, System.Drawing.Rectangle screen)
    {
        using var memory = new MemoryStream();
        screenshot.Save(memory, ImageFormat.Bmp);
        memory.Position = 0;
        var frame = BitmapFrame.Create(memory, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var canvas = new Canvas { Background = Brushes.Transparent };
        var outline = new Rectangle { Stroke = Brushes.White, StrokeThickness = 2,
            Fill = new SolidColorBrush(Color.FromArgb(75, 27, 139, 223)), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        canvas.Children.Add(outline);
        var help = new TextBlock { Text = "Arraste para selecionar a área · Esc para cancelar", Background = Brushes.Black,
            Foreground = Brushes.White, Padding = new Thickness(12), IsHitTestVisible = false };
        Canvas.SetLeft(help, 16);
        Canvas.SetTop(help, 16);
        canvas.Children.Add(help);
        var root = new Grid { Background = new ImageBrush(frame) { Stretch = Stretch.Fill } };
        root.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(75, 0, 0, 0)), IsHitTestVisible = false });
        root.Children.Add(canvas);
        var overlay = new Window { WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
            Topmost = true, Cursor = Cursors.Cross, WindowStartupLocation = WindowStartupLocation.Manual, Content = root };
        Point? origin = null;
        System.Drawing.Rectangle? selected = null;
        overlay.Loaded += (_, _) => SetWindowPos(new WindowInteropHelper(overlay).Handle, new IntPtr(-1),
            screen.Left, screen.Top, screen.Width, screen.Height, 0);
        overlay.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) overlay.Close(); };
        overlay.Deactivated += (_, _) => { if (overlay.IsVisible) overlay.Close(); };
        canvas.MouseLeftButtonDown += (_, e) =>
        {
            origin = e.GetPosition(canvas);
            help.Visibility = Visibility.Collapsed;
            canvas.CaptureMouse();
        };
        canvas.MouseMove += (_, e) =>
        {
            if (origin is not Point first || e.LeftButton != MouseButtonState.Pressed) return;
            var area = new Rect(first, Bound(e.GetPosition(canvas)));
            Canvas.SetLeft(outline, area.Left);
            Canvas.SetTop(outline, area.Top);
            outline.Width = area.Width;
            outline.Height = area.Height;
            outline.Visibility = Visibility.Visible;
        };
        canvas.MouseLeftButtonUp += (_, e) =>
        {
            if (origin is not Point first) return;
            canvas.ReleaseMouseCapture();
            var area = new Rect(first, Bound(e.GetPosition(canvas)));
            if (area.Width < 3 || area.Height < 3) { origin = null; outline.Visibility = Visibility.Collapsed; return; }
            double scaleX = screen.Width / canvas.ActualWidth, scaleY = screen.Height / canvas.ActualHeight;
            selected = System.Drawing.Rectangle.FromLTRB((int)Math.Floor(area.Left * scaleX), (int)Math.Floor(area.Top * scaleY),
                Math.Min(screen.Width, (int)Math.Ceiling(area.Right * scaleX)), Math.Min(screen.Height, (int)Math.Ceiling(area.Bottom * scaleY)));
            overlay.Close();
        };
        overlay.ShowDialog();
        return selected;

        Point Bound(Point point) => new(Math.Clamp(point.X, 0, canvas.ActualWidth), Math.Clamp(point.Y, 0, canvas.ActualHeight));
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}

