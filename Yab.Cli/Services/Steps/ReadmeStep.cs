using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Yab.Cli.Services.Steps
{
    public class ReadmeStep : IPipelineStep
    {
        public string Name => "README Preview";
        public int Order => 45;

        public async Task ExecuteAsync(PipelineContext context)
        {
            if (context.Command == "readme")
            {
                var readmePath = Path.Combine(context.RootPath, "README.md");
                
                // If not in current dir, check parent (common if running from project folder)
                if (!File.Exists(readmePath))
                {
                    var parentDir = Directory.GetParent(context.RootPath)?.FullName;
                    if (parentDir != null)
                    {
                        var parentReadme = Path.Combine(parentDir, "README.md");
                        if (File.Exists(parentReadme)) readmePath = parentReadme;
                    }
                }

                if (!File.Exists(readmePath))
                {
                    Spectre.Console.AnsiConsole.MarkupLine("[red]Error: README.md not found in the target path or its parent.[/]");
                    return;
                }

                var cts = new CancellationTokenSource();
                Console.CancelKeyPress += (s, e) =>
                {
                    e.Cancel = true;
                    cts.Cancel();
                };

                var server = new DocsServer(readmePath);
                await server.StartAsync(cts.Token);
            }
        }
    }
}
