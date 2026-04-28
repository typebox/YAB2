using System.IO;
using System.Threading.Tasks;
using Spectre.Console;

namespace Yab.Cli.Services.Steps
{
    public class GenerationStep : IPipelineStep
    {
        public string Name => "Generating";
        public int Order => 40;

        public async Task ExecuteAsync(PipelineContext context)
        {
            async Task RunGeneration(StatusContext? ctx)
            {
                var generator = new DocumentationGenerator();
                generator.GeneratePortal(context.Data, Path.Combine(context.RootPath, "LivingDocumentation.html"));
                generator.GenerateMasterLedger(context.Data, Path.Combine(context.RootPath, "BUILD_CERTIFICATE.md"));

                context.Log("[bold green]Success![/]");
                context.Log($"- Generated: {Path.Combine(context.RootPath, "LivingDocumentation.html")}");
                context.Log($"- Ledger: {Path.Combine(context.RootPath, "BUILD_CERTIFICATE.md")}");
            }

            if (context.IsTui)
            {
                await RunGeneration(null);
            }
            else
            {
                await AnsiConsole.Status()
                    .StartAsync("Generating Portal...", RunGeneration);
            }
        }
    }
}
