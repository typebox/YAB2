using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Spectre.Console;

namespace Yab.Cli.Services.Steps
{
    public class SignOffStep : IPipelineStep
    {
        public string Name => "Sign-off";
        public int Order => 30; // Runs after auditing

        public async Task ExecuteAsync(PipelineContext context)
        {
            if (context.Command != "sign-off") return;

            AnsiConsole.MarkupLine("[grey]Signing off on current implementation...[/]");
            
            var data = context.Data;
            var updatedCount = 0;

            foreach (var block in data.Blocks)
            {
                var hashTag = $"[yab-hash:{block.Name}:{block.Hash}]";
                bool updated = false;

                // Priority 1: Internal Documentation in .cs file
                var csContent = File.ReadAllText(block.FilePath);
                bool hasInternalDocs = !string.IsNullOrEmpty(block.Documentation) || csContent.Contains("/*yab-docs");

                if (hasInternalDocs)
                {
                    if (!csContent.Contains(hashTag))
                    {
                        var pattern = $@"\[yab-hash:{block.Name}:.*?\]";
                        if (Regex.IsMatch(csContent, pattern))
                        {
                            csContent = Regex.Replace(csContent, pattern, hashTag);
                        }
                        else
                        {
                            // If it's internal docs but missing the tag, we need to find the /*yab-docs block
                            if (csContent.Contains("/*yab-docs"))
                            {
                                csContent = csContent.Replace("/*yab-docs", "/*yab-docs\n    " + hashTag);
                            }
                            else
                            {
                                // Fallback to appending if no /*yab-docs found (shouldn't happen here)
                                csContent += "\n\n/*yab-docs\n" + hashTag + "\n*/";
                            }
                        }
                        File.WriteAllText(block.FilePath, csContent);
                        AnsiConsole.MarkupLine($"[green]Updated[/] internal anchor for {block.Name} in {Path.GetFileName(block.FilePath)}");
                        updatedCount++;
                        updated = true;
                    }
                    else
                    {
                        updated = true; // Already up to date
                    }
                }

                if (updated) continue;

                // Priority 2: External MD files
                var siblingMd = block.FilePath.Replace(".cs", ".md");
                string? mdPath = null;

                if (File.Exists(siblingMd))
                {
                    mdPath = siblingMd;
                }
                else
                {
                    var conceptMd = context.Data.MarkdownFiles.FirstOrDefault(f => 
                        f.Value.Metadata != null && block.Concepts.Contains(f.Value.Metadata.Concept)).Key;
                    mdPath = conceptMd;
                }

                if (mdPath == null) continue;
                if (mdPath.StartsWith("virtual://")) mdPath = mdPath.Substring(10);
                
                var content = File.ReadAllText(mdPath);
                
                // If it's already there, skip
                if (content.Contains(hashTag)) continue;

                // Replace old hash for this block if it exists
                var patternMd = $@"\[yab-hash:{block.Name}:.*?\]";
                if (Regex.IsMatch(content, patternMd))
                {
                    content = Regex.Replace(content, patternMd, hashTag);
                    AnsiConsole.MarkupLine($"[green]Updated[/] anchor for {block.Name} in {Path.GetFileName(mdPath)}");
                }
                else
                {
                    // Append to Physical Anchors section or end of file
                    if (content.Contains("## Physical Anchors"))
                    {
                        content = content.Replace("## Physical Anchors", "## Physical Anchors\n" + hashTag);
                    }
                    else
                    {
                        content += "\n\n## Physical Anchors\n" + hashTag;
                    }
                    AnsiConsole.MarkupLine($"[bold green]Added[/] new anchor for {block.Name} in {Path.GetFileName(mdPath)}");
                }

                File.WriteAllText(mdPath, content);
                updatedCount++;
            }

            AnsiConsole.MarkupLine($"\n[bold green]Success![/] Programmatically signed off on {updatedCount} code blocks.");
        }
    }
}
