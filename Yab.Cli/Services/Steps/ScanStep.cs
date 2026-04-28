using System.Threading.Tasks;
using Spectre.Console;

namespace Yab.Cli.Services.Steps
{
    public class ScanStep : IPipelineStep
    {
        public string Name => "Scanning";
        public int Order => 10;

        public async Task ExecuteAsync(PipelineContext context)
        {
            context.Log("[grey]Scanning code and documentation...[/]");

            var scanner = new CodeAttributeScanner();
            var collector = new DocumentationDataCollector(scanner);
            collector.Collect(context);
        }
    }
}
