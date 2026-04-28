using System.Threading.Tasks;
using Spectre.Console;

namespace Yab.Cli.Services.Steps
{
    public class VerificationStep : IPipelineStep
    {
        public string Name => "Verifying";
        public int Order => 30;

        public async Task ExecuteAsync(PipelineContext context)
        {
            AnsiConsole.MarkupLine("[grey]Verifying runnable examples...[/]");
            var verifier = new VerificationEngine();
            var verificationResults = verifier.VerifyExamples(context.RootPath);
            context.Data.VerificationResults.AddRange(verificationResults);
            
            foreach (var result in verificationResults)
            {
                AnsiConsole.MarkupLine($"[blue]{Markup.Escape(result)}[/]");
            }
        }
    }
}
