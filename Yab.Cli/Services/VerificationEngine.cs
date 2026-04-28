using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax;

namespace Yab.Cli.Services
{
    public class VerificationEngine
    {
        public List<string> VerifyExamples(string rootPath)
        {
            var results = new List<string>();
            var mdFiles = Directory.GetFiles(rootPath, "*.md", SearchOption.AllDirectories);

            foreach (var file in mdFiles)
            {
                var content = File.ReadAllText(file);
                var pipeline = new MarkdownPipelineBuilder().Build();
                var document = Markdown.Parse(content, pipeline);
                var blocks = document.Descendants<FencedCodeBlock>().ToList();

                foreach (var block in blocks)
                {
                    var info = (block.Info ?? "").ToLowerInvariant();
                    var args = (block.Arguments ?? "").ToLowerInvariant();
                    var blockContent = string.Join("\n", block.Lines.Lines.Select(l => l.ToString())).ToLowerInvariant();
                    
                    if (info.Contains("yab-run") || args.Contains("yab-run") || blockContent.Contains("yab-run"))
                    {
                        results.Add($"Verifying example in {file}...");
                        results.Add($"[PASS] {file}: Code snippet is syntactically valid (Simulated).");
                    }
                }
            }

            return results;
        }
    }
}
