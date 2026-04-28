using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;

namespace Yab.Cli.Services
{

    public class DocsServer
    {
        private readonly string _filePath;
        private readonly int _port;
        public Action<string>? Logger { get; set; }

        public DocsServer(string filePath, int port = 5006)
        {
            _filePath = filePath;
            _port = port;
        }

        public async Task StartAsync(CancellationToken ct)
        {
            if (!File.Exists(_filePath))
            {
                Log($"[red]Error: Documentation file not found at {_filePath}[/]");
                return;
            }

            var url = $"http://localhost:{_port}/";
            using var listener = new HttpListener();
            listener.Prefixes.Add(url);

            try
            {
                listener.Start();
            }
            catch (HttpListenerException ex)
            {
                Log($"[red]Failed to start server on {url}: {ex.Message}[/]");
                Log("[yellow]Try running as administrator or choosing a different port.[/]");
                return;
            }

            Log($"[bold green]Server started![/]");
            Log($"[blue]Serving:[/] [underline]{_filePath}[/]");
            Log($"[blue]URL:[/] [underline]{url}[/]");
            Log("[grey]Press Ctrl+C to stop the server.[/]");

            OpenBrowser(url);

            using var registration = ct.Register(() => listener.Stop());

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var context = await listener.GetContextAsync();
                    _ = Task.Run(() => HandleRequest(context), ct);
                }
                catch (HttpListenerException) // This will happen when listener.Stop() is called
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!ct.IsCancellationRequested)
                    {
                        AnsiConsole.MarkupLine($"[red]Error handling request: {ex.Message}[/]");
                    }
                }
            }

            listener.Stop();
            Log("[yellow]Server stopped.[/]");
        }

        private async Task HandleRequest(HttpListenerContext context)
        {
            try
            {
                var response = context.Response;

                if (context.Request.HttpMethod == "POST" && context.Request.Url?.AbsolutePath == "/api/audit-results")
                {
                    using var reader = new StreamReader(context.Request.InputStream);
                    var body = await reader.ReadToEndAsync();
                    UpdateCacheFromManualInput(body);
                    response.StatusCode = 200;
                    response.Close();
                    return;
                }

                byte[] content;
                if (_filePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                {
                    var md = await File.ReadAllTextAsync(_filePath);
                    var htmlContent = Markdig.Markdown.ToHtml(md);
                    var styledHtml = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
    <title>YAB | {Path.GetFileName(_filePath)}</title>
    <link href='https://fonts.googleapis.com/css2?family=Inter:wght@400;600;800&family=JetBrains+Mono&display=swap' rel='stylesheet'>
    <style>
        body {{ font-family: 'Inter', sans-serif; line-height: 1.6; color: #1e293b; max-width: 800px; margin: 4rem auto; padding: 0 2rem; background: #f8fafc; }}
        h1, h2, h3 {{ color: #0f172a; margin-top: 2rem; }}
        h1 {{ font-size: 2.5rem; font-weight: 800; border-bottom: 2px solid #e2e8f0; padding-bottom: 1rem; }}
        code {{ font-family: 'JetBrains Mono', monospace; background: #e2e8f0; padding: 0.2rem 0.4rem; border-radius: 0.25rem; font-size: 0.9em; }}
        pre {{ background: #0f172a; color: #e2e8f0; padding: 1.5rem; border-radius: 0.75rem; overflow-x: auto; }}
        pre code {{ background: none; padding: 0; color: inherit; }}
        blockquote {{ border-left: 4px solid #6366f1; padding-left: 1.5rem; color: #64748b; font-style: italic; margin: 2rem 0; }}
        a {{ color: #6366f1; text-decoration: none; font-weight: 600; }}
        a:hover {{ text-decoration: underline; }}
        table {{ width: 100%; border-collapse: collapse; margin: 2rem 0; }}
        th, td {{ text-align: left; padding: 0.75rem; border-bottom: 1px solid #e2e8f0; }}
        th {{ background: #f1f5f9; font-weight: 600; }}
    </style>
</head>
<body>
    {htmlContent}
</body>
</html>";
                    content = Encoding.UTF8.GetBytes(styledHtml);
                }
                else
                {
                    content = await File.ReadAllBytesAsync(_filePath);
                }

                response.ContentType = "text/html";
                response.ContentEncoding = Encoding.UTF8;
                response.ContentLength64 = content.Length;

                await response.OutputStream.WriteAsync(content, 0, content.Length);
                response.Close();
            }
            catch (Exception ex)
            {
                Log($"[grey]Request Error: {ex.Message}[/]");
            }
        }

        private void OpenBrowser(string url)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Process.Start("xdg-open", url);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Process.Start("open", url);
                }
            }
            catch (Exception ex)
            {
                Log($"[yellow]Could not open browser automatically: {ex.Message}[/]");
            }
        }

        private void UpdateCacheFromManualInput(string input)
        {
            var rootPath = Path.GetDirectoryName(_filePath);
            if (rootPath == null) return;
            
            var cache = new AuditCacheService(rootPath);
            var lines = input.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var line in lines)
            {
                if (line.Contains("COMPONENT:") && (line.Contains("PASSED") || line.Contains("BLOCKED")))
                {
                    var componentPart = line.Substring(line.IndexOf("COMPONENT:") + 10);
                    var parts = componentPart.Split(new[] { '-' }, 2);
                    if (parts.Length < 2) continue;
                    
                    var name = parts[0].Trim();
                    var statusPart = parts[1].Trim();
                    
                    bool passed = statusPart.StartsWith("PASSED", StringComparison.OrdinalIgnoreCase);
                    string? reason = null;
                    if (!passed && statusPart.Contains("BLOCKED:"))
                    {
                        reason = statusPart.Substring(statusPart.IndexOf("BLOCKED:") + 8).Trim();
                    }
                    else if (!passed)
                    {
                        reason = statusPart;
                    }

                    cache.UpdateManual(name, passed, reason);
                }
            }
            cache.Save();
            Log("[bold green]Audit cache updated from manual input![/]");
        }

        private void Log(string message)
        {
            if (Logger != null) Logger(message);
            else AnsiConsole.MarkupLine(message);
        }
    }
}
