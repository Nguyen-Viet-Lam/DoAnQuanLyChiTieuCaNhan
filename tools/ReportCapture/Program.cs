using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Drawing;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: ReportCapture <url> <outputPath> <width> [height]");
            return 1;
        }

        var url = args[0];
        var outputPath = args[1];
        var width = int.TryParse(args[2], out var parsedWidth) ? parsedWidth : 1600;
        var height = args.Length >= 4 && int.TryParse(args[3], out var parsedHeight) ? parsedHeight : 1800;
        var tcs = new TaskCompletionSource<int>();

        ApplicationConfiguration.Initialize();

        var form = new Form
        {
            Width = width,
            Height = height,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-32000, -32000),
            ShowInTaskbar = false,
            FormBorderStyle = FormBorderStyle.None
        };

        var webView = new WebView2
        {
            Dock = DockStyle.Fill
        };

        form.Controls.Add(webView);

        form.Shown += async (_, __) =>
        {
            try
            {
                var env = await CoreWebView2Environment.CreateAsync();
                await webView.EnsureCoreWebView2Async(env);
                webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                webView.CoreWebView2.NavigationCompleted += async (_, e) =>
                {
                    if (!e.IsSuccess)
                    {
                        Console.Error.WriteLine($"Navigation failed: {e.WebErrorStatus}");
                        tcs.TrySetResult(2);
                        form.Close();
                        return;
                    }

                    try
                    {
                        await Task.Delay(1200);
                        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                        await using var stream = File.Create(outputPath);
                        await webView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
                        tcs.TrySetResult(0);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine(ex);
                        tcs.TrySetResult(3);
                    }
                    finally
                    {
                        form.Close();
                    }
                };

                webView.Source = new Uri(url);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                tcs.TrySetResult(4);
                form.Close();
            }
        };

        form.FormClosed += (_, __) =>
        {
            if (!tcs.Task.IsCompleted)
            {
                tcs.TrySetResult(5);
            }
        };

        form.Show();
        Application.Run(form);

        return tcs.Task.GetAwaiter().GetResult();
    }
}
