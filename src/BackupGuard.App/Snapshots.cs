using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BackupGuard.App.ViewModels;

namespace BackupGuard.App;

/// <summary>Renderiza cada tela em PNG, sem capturar a tela do computador.</summary>
internal static class Snapshots
{
    public static async Task CaptureAllAsync(Window window, MainViewModel viewModel, string folder)
    {
        Directory.CreateDirectory(folder);
        for (var i = 0; i < viewModel.Pages.Count; i++)
        {
            viewModel.Current = viewModel.Pages[i];
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await Task.Delay(300);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);

            var content = (FrameworkElement)window.Content;
            var dpi = VisualTreeHelper.GetDpi(window);
            var bitmap = new RenderTargetBitmap(
                (int)(content.ActualWidth * dpi.DpiScaleX), (int)(content.ActualHeight * dpi.DpiScaleY),
                dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            bitmap.Render(content);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            await using var file = File.Create(Path.Combine(folder, $"{i + 1}-{viewModel.Pages[i].Title}.png"));
            encoder.Save(file);
        }
    }
}
