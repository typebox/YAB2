using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Yab.Cli.Services.Steps
{
    public class ServeStep : IPipelineStep
    {
        public string Name => "Serving";
        public int Order => 50;

        public async Task ExecuteAsync(PipelineContext context)
        {
            if (context.Command == "serve")
            {
                var server = new DocsServer(Path.Combine(context.RootPath, "LivingDocumentation.html"));
                if (context.IsTui)
                {
                    server.Logger = msg => context.Log(msg);
                    context.IsServerRunning = true;
                    context.ServerUrl = "http://localhost:5006/";
                    _ = Task.Run(async () => 
                    {
                        try 
                        {
                            await server.StartAsync(context.CancellationToken);
                        }
                        finally
                        {
                            context.IsServerRunning = false;
                        }
                    });
                    context.Log("[bold green]Background Server Started![/]");
                    context.Log("[grey]Docs are being served at http://localhost:5006/[/]");
                }
                else
                {
                    await server.StartAsync(context.CancellationToken);
                }
            }
        }
    }
}
