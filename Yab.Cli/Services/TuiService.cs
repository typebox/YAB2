using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Rendering;
using Yab.Cli.Models;

namespace Yab.Cli.Services
{
    public class TuiService
    {
        private readonly PipelineContext _context;
        private readonly CancellationTokenSource _cts;
        private readonly Pipeline _pipeline;
        private string _status = "Idle";
        private string _currentPhase = "Ready";
        private double _progress = 0;
        private bool _isRunning = false;
        private bool _shouldExit = false;
        private bool _isApplyingSuggestion = false;

        public TuiService(PipelineContext context, CancellationTokenSource cts)
        {
            _context = context;
            _cts = cts;
            _context.IsTui = true;
            _pipeline = new Pipeline().DiscoverSteps();
        }

        public async Task RunAsync()
        {
            var layout = new Layout("Root")
                .SplitRows(
                    new Layout("Header").Size(3),
                    new Layout("Main").SplitColumns(
                        new Layout("Left").SplitRows(
                            new Layout("Status"),
                            new Layout("Services").Size(6)
                        ),
                        new Layout("Logs")
                    ),
                    new Layout("Footer").Size(8)
                );

            // Set initial sizes
            var width = AnsiConsole.Console.Profile.Width;
            layout["Left"].Size(Math.Min(45, width / 2));

            // Auto-start initial synchronization
            _context.Command = _context.Runtime ? "all" : "docs";
            _ = RunPipelineAsync(_context.Runtime ? "Initial Full Sync (with Coverage)" : "Initial Documentation Sync");

            await AnsiConsole.Live(layout)
                .StartAsync(async ctx =>
                {
                    while (!_shouldExit)
                    {
                        try 
                        {
                            UpdateLayout(layout);
                            ctx.Refresh();
                        }
                        catch (Exception ex)
                        {
                            // If rendering fails (e.g. during resize), try to recover in the next tick
                            _context.Logs.Add($"[red]Render Error:[/] {ex.Message}");
                        }

                        await HandleInputAsync();

                        await Task.Delay(100); // Slightly longer delay for stability
                    }
                });
        }

        private void UpdateLayout(Layout layout)
        {
            var width = AnsiConsole.Console.Profile.Width;
            var height = AnsiConsole.Console.Profile.Height;

            // Safety check for terminal size
            if (width < 60 || height < 18)
            {
                layout["Header"].IsVisible = false;
                layout["Footer"].IsVisible = false;
                layout["Left"].IsVisible = false;
                layout["Logs"].Update(
                    new Panel(
                        Align.Center(
                            new Rows(
                                new Markup("[bold red]TERMINAL TOO SMALL[/]"),
                                new Text(""),
                                new Markup($"[grey]Current: {width}x{height}[/]"),
                                new Markup("[grey]Required: 60x18[/]"),
                                new Text(""),
                                new Markup("[yellow]Please resize the window...[/]")
                            ),
                            VerticalAlignment.Middle
                        )
                    ).BorderColor(Color.Red).Expand()
                );
                return;
            }

            // Restore visibility
            layout["Header"].IsVisible = true;
            layout["Footer"].IsVisible = true;
            layout["Left"].IsVisible = true;

            // Dynamically adjust Left column size based on available width
            layout["Left"].Size(Math.Min(45, width / 2));

            // Header
            layout["Header"].Update(
                new Panel(
                    Align.Center(
                        new Markup("[bold blue]YAB[/] - [italic grey]Living Documentation Shell[/]"),
                        VerticalAlignment.Middle
                    )
                ).BorderColor(Color.Blue).Expand()
            );

            // Status
            var statusTable = new Table().HideHeaders().NoBorder();
            statusTable.AddColumn("Label");
            statusTable.AddColumn("Value");
            statusTable.AddRow("[grey]Project:[/]", $"[white]{_context.RootPath}[/]");
            statusTable.AddRow("[grey]Run ID:[/]", $"[white]{_context.RunId}[/]");
            statusTable.AddRow("[grey]Phase:[/]", $"[bold yellow]{_currentPhase}[/]");
            statusTable.AddRow("[grey]Status:[/]", $"[white]{_status}[/]");

            var statusContent = new Rows(
                new Padder(statusTable, new Padding(1, 1, 0, 0)),
                new Rule().RuleStyle("grey"),
                new Padder(new Markup($"[grey]Suggestions:[/] [bold]{_context.Suggestions.Count}[/]"), new Padding(1, 0)),
                new Padder(new Markup($"[grey]Warnings:[/] [bold red]{_context.DriftWarnings.Count}[/]"), new Padding(1, 0))
            );

            layout["Status"].Update(
                new Panel(statusContent)
                {
                    Header = new PanelHeader("[bold]SYSTEM STATUS[/]"),
                    Border = BoxBorder.Rounded
                }.Expand()
            );

            // Logs
            var logsToTake = Math.Max(5, height - 15); // Adjust logs based on height
            var logLines = _context.Logs.TakeLast(logsToTake).Select(l => 
            {
                try { return new Markup(l); }
                catch { return new Markup(Markup.Escape(l)); }
            });

            // Services
            var servicesPanel = new Panel(
                _context.IsServerRunning 
                    ? new Rows(
                        new Markup("[green]●[/] Docs Server"),
                        new Markup($"  [grey]{_context.ServerUrl}[/]")
                      )
                    : new Markup("[grey]No active services[/]")
            )
            {
                Header = new PanelHeader("[bold]ACTIVE SERVICES[/]"),
                Border = BoxBorder.Rounded
            }.Expand();

            layout["Services"].Update(servicesPanel);

            layout["Logs"].Update(
                new Panel(new Rows(logLines))
                {
                    Header = new PanelHeader("[bold]ACTIVITY LOG[/]"),
                    Border = BoxBorder.Rounded
                }.Expand()
            );

            // Footer
            var menuItems = new List<string> {
                "[bold blue]G[/]enerate",
                "[bold blue]R[/]eview",
                "[bold blue]S[/]erve",
                "[bold blue]Q[/]uit"
            };

            if (_isRunning || _isApplyingSuggestion)
            {
                layout["Footer"].Update(
                    new Panel(
                        Align.Center(
                            new Markup("[yellow]Processing... Press Ctrl+C to cancel[/]"),
                            VerticalAlignment.Middle
                        )
                    ).BorderColor(Color.Yellow).Expand()
                );
            }
            else if (_context.Suggestions.Any())
            {
                var suggestion = _context.Suggestions.First();
                var content = new Rows(
                    Align.Center(new Markup($"[yellow]SUGGESTION:[/] {Markup.Escape(suggestion.Title)}")),
                    new Text(""),
                    Align.Center(new Markup($"[grey]{Markup.Escape(suggestion.Description)}[/]")),
                    new Text(""),
                    Align.Center(new Markup("[bold blue]Y[/]es / [bold blue]N[/]o / [bold blue]A[/]ll skip"))
                );
                layout["Footer"].Update(
                    new Panel(content)
                    .BorderColor(Color.Yellow).Expand()
                );
            }
            else
            {
                layout["Footer"].Update(
                    new Panel(
                        Align.Center(
                            new Markup(string.Join("  |  ", menuItems)),
                            VerticalAlignment.Middle
                        )
                    ).BorderColor(Color.Grey).Expand()
                );
            }
        }

        private async Task HandleInputAsync()
        {
            if (!Console.KeyAvailable) return;

            var key = Console.ReadKey(true);
            var commandChar = key.KeyChar.ToString().ToUpper();

            if (commandChar == "Q")
            {
                _context.Logs.Add("[grey]INPUT:[/] Q - Quitting...");
                _shouldExit = true;
                _cts.Cancel();
                return;
            }

            if (_isRunning || _isApplyingSuggestion) return;

            if (_context.Suggestions.Any())
            {
                var suggestion = _context.Suggestions.First();
                if (commandChar == "Y")
                {
                    _ = ApplySuggestionAsync(suggestion);
                    return;
                }
                if (commandChar == "N")
                {
                    _context.Suggestions.RemoveAt(0);
                    return;
                }
                if (commandChar == "A")
                {
                    _context.Suggestions.Clear();
                    return;
                }
            }

            switch (commandChar)
            {
                case "G":
                    _context.Logs.Add("[grey]INPUT:[/] G - Synchronizing...");
                    _context.Command = _context.Runtime ? "all" : "docs";
                    _ = RunPipelineAsync(_context.Runtime ? "Full Sync (with Coverage)" : "Documentation Sync");
                    break;
                case "R":
                    _context.Logs.Add("[grey]INPUT:[/] R - Performing Review...");
                    _context.Command = "audit";
                    _ = RunPipelineAsync("Performing Semantic Review");
                    break;
                case "S":
                    _context.Logs.Add("[grey]INPUT:[/] S - Starting Server...");
                    _context.Command = "serve";
                    _ = RunPipelineAsync("Starting Web Server");
                    break;
            }
        }

        private async Task RunPipelineAsync(string phaseName)
        {
            _isRunning = true;
            _currentPhase = phaseName;
            _status = "In Progress";
            _progress = 0;
            _context.Logs.Add($"[blue]INFO[/] Starting {phaseName}...");

            try
            {
                // Run pipeline in background to keep UI thread responsive
                await Task.Run(() => _pipeline.ExecuteAsync(_context));
                
                _status = "Completed Successfully";
                _progress = 100;
                _context.Logs.Add($"[green]SUCCESS[/] {phaseName} completed.");
            }
            catch (Exception ex)
            {
                _status = "Failed";
                _context.Logs.Add($"[red]ERROR[/] {ex.Message}");
            }
            finally
            {
                _isRunning = false;
            }
        }

        private async Task ApplySuggestionAsync(Suggestion suggestion)
        {
            _isApplyingSuggestion = true;
            _status = "Applying Suggestion";
            _context.Logs.Add($"[blue]INFO[/] Applying: {Markup.Escape(suggestion.Title)}...");
            
            try
            {
                await Task.Run(() => suggestion.ApplyAsync());
                _context.Logs.Add($"[green]SUCCESS[/] Applied: {Markup.Escape(suggestion.Title)}");
                _context.Suggestions.RemoveAt(0);
            }
            catch (Exception ex)
            {
                _context.Logs.Add($"[red]ERROR[/] Failed to apply: {ex.Message}");
            }
            finally
            {
                _isApplyingSuggestion = false;
                _status = "Idle";
            }
        }
    }
}
