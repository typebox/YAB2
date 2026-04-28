using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Yab.Cli.Services;

namespace Yab.Cli
{
    public class Program
    {
        public static async Task<int> Main(string[] args)
        {
            return await RunAsync(args, null);
        }

        public static async Task<int> RunAsync(string[] args, IAiAgentService? aiServiceOverride = null)
        {
            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            var verbose = args.Contains("--verbose") || args.Contains("-v");
            var manual = args.Contains("--manual") || args.Contains("-m");
            var skipAi = args.Contains("--skip-ai");
            var runtime = args.Contains("--runtime") || args.Contains("-r");
            var filteredArgs = args.Where(a => a != "--verbose" && a != "-v" && a != "--manual" && a != "-m" && a != "--skip-ai" && a != "--runtime" && a != "-r").ToArray();

            var rootPath = Directory.GetCurrentDirectory();
            var command = "shell";

            if (filteredArgs.Length > 0)
            {
                if (Directory.Exists(filteredArgs[0]))
                {
                    rootPath = Path.GetFullPath(filteredArgs[0]);
                    if (filteredArgs.Length > 1) command = filteredArgs[1];
                }
                else if (filteredArgs[0] == "dev")
                {
                    if (filteredArgs.Length > 1)
                    {
                        if (Directory.Exists(filteredArgs[1]))
                        {
                            rootPath = Path.GetFullPath(filteredArgs[1]);
                        }
                        else
                        {
                            command = filteredArgs[1];
                            if (filteredArgs.Length > 2) rootPath = Path.GetFullPath(filteredArgs[2]);
                        }
                    }
                }
                else
                {
                    command = filteredArgs[0];
                    if (filteredArgs.Length > 1) rootPath = Path.GetFullPath(filteredArgs[1]);
                }
            }

            if (!Directory.Exists(rootPath))
            {
                AnsiConsole.MarkupLine($"[red]Directory not found: {rootPath}[/]");
                return 1;
            }

            var context = new PipelineContext
            {
                RootPath = rootPath,
                Command = command,
                AiService = aiServiceOverride,
                Verbose = verbose,
                ManualAudit = manual,
                SkipAi = skipAi,
                Runtime = runtime,
                CancellationToken = cts.Token
            };

            if (command == "shell" || command == "tui")
            {
                var tui = new TuiService(context, cts);
                await tui.RunAsync();
                return 0;
            }

            var pipeline = new Pipeline()
                .DiscoverSteps();

            try
            {
                await pipeline.ExecuteAsync(context);
            }
            catch (OperationCanceledException)
            {
                AnsiConsole.MarkupLine("[yellow]Operation cancelled by user.[/]");
                return 130; // Standard exit code for Ctrl+C
            }

            return 0;
        }
    }
}
