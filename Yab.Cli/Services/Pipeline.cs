using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Spectre.Console;
using Yab.Cli.Models;

namespace Yab.Cli.Services
{
    public class PipelineContext
    {
        public string RootPath { get; set; } = string.Empty;
        public string Command { get; set; } = string.Empty;
        public DocumentationData Data { get; set; } = new();
        public List<string> DriftWarnings { get; set; } = new();
        public List<Suggestion> Suggestions { get; set; } = new();
        public List<string> Logs { get; set; } = new();
        public string RunId { get; set; } = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        public bool Verbose { get; set; }
        public bool ManualAudit { get; set; }
        public bool SkipAi { get; set; }
        public bool Runtime { get; set; }
        public IAiAgentService? AiService { get; set; }
        public bool IsTui { get; set; }
        public bool IsServerRunning { get; set; }
        public string? ServerUrl { get; set; }
        public System.Threading.CancellationToken CancellationToken { get; set; } = System.Threading.CancellationToken.None;

        public void Log(string message)
        {
            Logs.Add($"[[{DateTime.Now:HH:mm:ss}]] {message}");
            if (Verbose && !IsTui)
            {
                try
                {
                    AnsiConsole.MarkupLine($"[grey]LOG:[/] {message}");
                }
                catch
                {
                    // Fallback to escaping if markup is invalid
                    AnsiConsole.MarkupLine($"[grey]LOG:[/] {Markup.Escape(message)}");
                }
            }
        }
    }

    public interface IPipelineStep
    {
        string Name { get; }
        int Order { get; }
        Task ExecuteAsync(PipelineContext context);
    }

    public class Pipeline
    {
        private readonly List<IPipelineStep> _steps = new();

        public Pipeline DiscoverSteps()
        {
            var interfaceType = typeof(IPipelineStep);
            var assembly = interfaceType.Assembly;
            
            var types = assembly.GetTypes()
                .Where(p => interfaceType.IsAssignableFrom(p) && !p.IsInterface && !p.IsAbstract);

            foreach (var type in types)
            {
                if (Activator.CreateInstance(type) is IPipelineStep step)
                {
                    _steps.Add(step);
                }
            }
            
            _steps.Sort((a, b) => a.Order.CompareTo(b.Order));
            return this;
        }

        public async Task ExecuteAsync(PipelineContext context)
        {
            foreach (var step in _steps)
            {
                await step.ExecuteAsync(context);
            }
        }
    }
}
