using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Yab.Cli.Models;

namespace Yab.Cli.Services
{
    public class FeatureFileScanner
    {
        public List<CodeBlock> ScanFile(string filePath)
        {
            var blocks = new List<CodeBlock>();
            var lines = File.ReadAllLines(filePath);
            
            string? currentScenario = null;
            List<string> currentSteps = new List<string>();
            List<string> currentConcepts = new List<string>();
            int startLine = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                
                // Extract concepts from tags
                if (line.StartsWith("@yab-concept:"))
                {
                    var concept = line.Substring("@yab-concept:".Length).Trim();
                    if (!currentConcepts.Contains(concept)) currentConcepts.Add(concept);
                }
                else if (line.StartsWith("Scenario:"))
                {
                    // Save previous scenario if any
                    if (currentScenario != null)
                    {
                        blocks.Add(new CodeBlock
                        {
                            Name = currentScenario,
                            FilePath = filePath,
                            StartLine = startLine,
                            EndLine = i,
                            Content = string.Join("\n", currentSteps),
                            Concepts = new List<string>(currentConcepts),
                            IsTest = true
                        });
                    }

                    currentScenario = line.Substring("Scenario:".Length).Trim();
                    currentSteps = new List<string> { lines[i] };
                    startLine = i + 1;
                    // Keep concepts for the new scenario, but we might want to clear them if tags are per-scenario
                    // Gherkin tags are per-scenario or per-feature. For now, assume they apply to the next scenario.
                }
                else if (currentScenario != null)
                {
                    if (string.IsNullOrWhiteSpace(line) && !IsStep(line))
                    {
                        // End of scenario? (simplified Gherkin parsing)
                        // In real Gherkin, a new Scenario: or Feature: or Tag starts a new block.
                    }
                    else
                    {
                        currentSteps.Add(lines[i]);
                    }
                }

                // If we hit a new Scenario or Feature, we might need to reset concepts if they were per-scenario
                if (line.StartsWith("Scenario:") || line.StartsWith("Feature:"))
                {
                    // In this simple scanner, tags immediately preceding Scenario apply to it.
                }
            }

            // Add last scenario
            if (currentScenario != null)
            {
                blocks.Add(new CodeBlock
                {
                    Name = currentScenario,
                    FilePath = filePath,
                    StartLine = startLine,
                    EndLine = lines.Length,
                    Content = string.Join("\n", currentSteps),
                    Concepts = new List<string>(currentConcepts),
                    IsTest = true
                });
            }

            return blocks;
        }

        private bool IsStep(string line)
        {
            var l = line.Trim();
            return l.StartsWith("Given ") || l.StartsWith("When ") || l.StartsWith("Then ") || l.StartsWith("And ") || l.StartsWith("But ");
        }
    }
}
