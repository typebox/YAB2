using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Spectre.Console;
using Yab.Cli.Models;

namespace Yab.Cli.Services.Steps
{
    public class AuditStep : IPipelineStep
    {
        public string Name => "Auditing";
        public int Order => 20;

        public async Task ExecuteAsync(PipelineContext context)
        {
            if (context.DriftWarnings.Any())
            {
                context.Log("[bold red]Audit Failed: Logic Drift Detected![/]");
                foreach (var warning in context.DriftWarnings)
                {
                    context.Log($"[red]- {Markup.Escape(warning)}[/]");
                }

                bool performAiAudit = false;
                if (context.Command == "audit")
                {
                    performAiAudit = true;
                }
                else if (context.SkipAi)
                {
                    context.Log("[yellow]AI audit skipped via --skip-ai flag.[/]");
                }
                else if (context.IsTui)
                {
                    context.Suggestions.Add(new Suggestion
                    {
                        Title = "Semantic Audit Recommended",
                        Description = "Logic drift detected. AI can perform a semantic audit to verify the intent.",
                        ActionText = "Run AI Audit",
                        ApplyAsync = async () => 
                        {
                            await PerformAiAudit(context);
                        }
                    });
                }
                else if (AnsiConsole.Profile.Capabilities.Interactive)
                {
                    performAiAudit = AnsiConsole.Confirm("[yellow]Logic drift detected. Would you like to perform a Semantic Audit using AI?[/]");
                }
                else
                {
                    context.Log("[yellow]Non-interactive environment detected. Skipping optional AI audit.[/]");
                }

                if (performAiAudit)
                {
                    await PerformAiAudit(context);
                }
            }
            else
            {
                context.Log("[green]Audit Passed: No logic drift detected.[/]");
            }
        }

        private async Task PerformAiAudit(PipelineContext context)
        {
            async Task RunAudit(StatusContext? ctx)
            {
                var aiService = context.AiService ?? new AiAgentService();
                aiService.RunId = context.RunId;
                aiService.Verbose = context.Verbose;
                aiService.PromptOnly = context.ManualAudit;
                context.Log($"[grey]Note: AI prompts for this run are being saved to .yab/prompts/{context.RunId}/ for inspection.[/]");
                var auditor = new SemanticAuditEngine(aiService);
                var cacheService = new AuditCacheService(context.RootPath);
                
                var driftedBlocks = context.Data.Blocks.Where(b => b.VerificationStatus == "DRIFTED").ToList();
                
                if (!driftedBlocks.Any())
                {
                    context.Log("[grey]No drifted components found for AI review.[/]");
                    return;
                }

                // Pre-filter with cache
                var blocksNeedingAudit = new List<CodeBlock>();
                foreach (var block in driftedBlocks)
                {
                    var md = context.Data.MarkdownFiles.Values.FirstOrDefault(m => m.Metadata != null && block.Concepts.Contains(m.Metadata.Concept));
                    var conceptDocs = md?.Content ?? "";
                    
                    var cached = cacheService.Get(block.Name, block.Hash ?? "", conceptDocs);
                    if (cached != null)
                    {
                        block.SemanticReviewMessage = cached.Message;
                        if (cached.Success)
                            context.Log($"[grey]Semantic Audit (Cached) Passed for {block.Name}.[/]");
                        else
                            context.Log($"[bold red]Semantic Audit (Cached) Failed for {block.Name}: {Markup.Escape(cached.Message ?? "")}[/]");
                    }
                    else
                    {
                        blocksNeedingAudit.Add(block);
                    }
                }

                if (!blocksNeedingAudit.Any())
                {
                    context.Log("[green]All drifted components have already been verified in current state.[/]");
                    return;
                }

                context.Log($"[grey]Found {blocksNeedingAudit.Count} components needing new AI review. Reviewing in chunks...[/]");

                // Chunk by 5
                for (int i = 0; i < blocksNeedingAudit.Count; i += 5)
                {
                    var chunk = blocksNeedingAudit.Skip(i).Take(5).ToList();
                    var auditRequests = new List<AuditBatchRequest>();
                    
                    var chunkContexts = new List<(CodeBlock Block, string ConceptDocs)>();

                    foreach (var block in chunk)
                    {
                        var md = context.Data.MarkdownFiles.Values.FirstOrDefault(m => m.Metadata != null && block.Concepts.Contains(m.Metadata.Concept));
                        var conceptDocs = md?.Content ?? "";
                        chunkContexts.Add((block, conceptDocs));
                        auditRequests.Add(new AuditBatchRequest(block.Name, block.Content, conceptDocs, block.Intent ?? "No explicit intent", block.Hash ?? ""));
                    }

                    if (ctx != null) ctx.Status($"Sending batch {i / 5 + 1} to AI ({chunk.Count} items)...");
                    else context.Log($"[grey]AI Audit:[/] Sending batch {i / 5 + 1} to AI ({chunk.Count} items)...");

                    var batchResults = await auditor.ValidateBatchAsync(auditRequests, context.CancellationToken);
                    
                    foreach (var res in batchResults)
                    {
                        var ctxBlock = chunkContexts.FirstOrDefault(c => c.Block.Name == res.Name);
                        ctxBlock.Block.SemanticReviewMessage = res.Message;
                        cacheService.Update(res.Name, ctxBlock.Block.Hash ?? "", ctxBlock.ConceptDocs, res.Success, res.Message);

                        // Persist feedback to source/md
                        var signOff = new SignOffService(context.Data);
                        await signOff.UpdateAuditFeedbackAsync(ctxBlock.Block, res.Message, context.RootPath);

                        if (!res.Success)
                        {
                            context.Log($"[bold red]Semantic Audit Failed for {res.Name}: {Markup.Escape(res.Message)}[/]");
                        }
                        else
                        {
                            context.Log($"[green]Semantic Audit Passed for {res.Name}.[/]");
                        }
                    }
                }

                cacheService.Save();

                // Re-generate documentation so the user can see the AI feedback immediately
                var generator = new DocumentationGenerator();
                generator.GeneratePortal(context.Data, System.IO.Path.Combine(context.RootPath, "LivingDocumentation.html"));
                context.Log("[grey]Living Documentation updated with semantic audit results.[/]");
            }

            if (context.IsTui)
            {
                await RunAudit(null);
            }
            else
            {
                await AnsiConsole.Status()
                    .StartAsync("Performing Semantic Audit with AI...", RunAudit);
            }
        }
    }
}
