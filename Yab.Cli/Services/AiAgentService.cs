using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Yab.Cli.Services
{
    public record AiReviewResult(bool Passed, string? Reason);

    public interface IAiAgentService
    {
        string? RunId { get; set; }
        bool Verbose { get; set; }
        bool PromptOnly { get; set; }
        Task<AiReviewResult> ReviewChangesAsync(string command, string diff, string conceptDocs, string validationJson, string hash = "", CancellationToken cancellationToken = default);
        Task<List<(string Name, AiReviewResult Result)>> ReviewBatchAsync(string command, List<AuditBatchRequest> requests, CancellationToken cancellationToken = default);
        Task<string> BuildPromptAsync(string diff, string conceptDocs, string validationJson);
    }

    public record AuditBatchRequest(string Name, string Content, string ConceptDocs, string Intent, string Hash);

    public class AiAgentService : IAiAgentService
    {
        private const string AgentFile = ".gemini/agents/business-integrity-agent.md";
        private const string HardcodedNodePath = @"C:\nvm4w\nodejs\node.exe";
        private const string HardcodedGeminiJsPath = @"C:\nvm4w\nodejs\node_modules\@google\gemini-cli\bundle\gemini.js";

        public string? RunId { get; set; }
        public bool Verbose { get; set; }
        public bool PromptOnly { get; set; }

        public async Task<List<(string Name, AiReviewResult Result)>> ReviewBatchAsync(string command, List<AuditBatchRequest> requests, CancellationToken cancellationToken = default)
        {
            var batchPrompt = new StringBuilder();
            batchPrompt.AppendLine("Please perform a batch review of the following components:");
            foreach (var req in requests)
            {
                batchPrompt.AppendLine($"### COMPONENT: {req.Name}");
                batchPrompt.AppendLine($"CONCEPT DOCS:\n{req.ConceptDocs}");
                batchPrompt.AppendLine($"INTENT: {req.Intent}");
                batchPrompt.AppendLine($"CODE:\n{req.Content}");
                batchPrompt.AppendLine($"STRUCTURAL HASH: {req.Hash}");
                batchPrompt.AppendLine("---");
            }
            batchPrompt.AppendLine("\nFor each component, respond with exactly: 'COMPONENT: [Name] - PASSED' or 'COMPONENT: [Name] - BLOCKED: [Reason]'.");

            var (output, error, exitCode) = await ExecuteAgentInternalAsync(command, batchPrompt.ToString(), "BATCH", cancellationToken);
            
            var results = new List<(string Name, AiReviewResult Result)>();
            if (exitCode != 0)
            {
                var errorResult = new AiReviewResult(false, $"AI CLI Error ({exitCode}): {error}");
                foreach (var req in requests) results.Add((req.Name, errorResult));
                return results;
            }

            foreach (var req in requests)
            {
                results.Add((req.Name, ParseGranularResponse(output, req.Name)));
            }
            return results;
        }

        private static AiReviewResult ParseGranularResponse(string output, string name)
        {
            var lines = output.Split(new[] { '\n', '\r' }, StringSplitOptions.None);
            var resultLines = new List<string>();
            bool capturing = false;
            bool found = false;
            bool passed = false;

            foreach (var line in lines)
            {
                if (line.Contains($"COMPONENT: {name}", StringComparison.OrdinalIgnoreCase))
                {
                    found = true;
                    capturing = true;
                    if (line.Contains("PASSED", StringComparison.OrdinalIgnoreCase))
                    {
                        passed = true;
                        capturing = false; // No more reasoning needed for PASSED
                    }
                    else if (line.Contains("BLOCKED", StringComparison.OrdinalIgnoreCase))
                    {
                        var index = line.IndexOf("BLOCKED", StringComparison.OrdinalIgnoreCase);
                        var firstLineReason = line.Substring(index + 7).Trim(':').Trim();
                        if (!string.IsNullOrEmpty(firstLineReason)) resultLines.Add(firstLineReason);
                    }
                    continue;
                }

                if (capturing)
                {
                    if (line.Trim().StartsWith("COMPONENT:", StringComparison.OrdinalIgnoreCase))
                    {
                        capturing = false;
                    }
                    else if (!string.IsNullOrWhiteSpace(line))
                    {
                        resultLines.Add(line.Trim());
                    }
                }
            }

            if (!found)
            {
                // Fallback for non-batch output
                if (output.Trim().Equals("PASSED", StringComparison.OrdinalIgnoreCase)) return new AiReviewResult(true, null);
                return new AiReviewResult(false, "Could not find component in AI output.");
            }

            if (passed) return new AiReviewResult(true, null);

            var fullReason = string.Join("\n", resultLines);
            return new AiReviewResult(false, string.IsNullOrWhiteSpace(fullReason) ? "AI blocked this component without providing a specific reason." : fullReason);
        }

        public async Task<AiReviewResult> ReviewChangesAsync(string command, string diff, string conceptDocs, string validationJson, string hash = "", CancellationToken cancellationToken = default)
        {
            var prompt = await BuildPromptWithHashAsync(diff, conceptDocs, validationJson, hash);
            
            if (PromptOnly)
            {
                var promptDir = Path.Combine(Directory.GetCurrentDirectory(), ".yab", "prompts", RunId ?? "last_run");
                Directory.CreateDirectory(promptDir);
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                var filename = Path.Combine(promptDir, $"prompt_MANUAL_{timestamp}.md");
                File.WriteAllText(filename, prompt);
                return new AiReviewResult(false, $"MANUAL_REVIEW_REQUIRED: Prompt saved to {Path.GetFileName(filename)}. Please review manually.");
            }

            var (output, error, exitCode) = await ExecuteAgentInternalAsync(command, prompt, "SINGLE", cancellationToken);

            if (exitCode != 0)
            {
                return new AiReviewResult(false, $"AI CLI Error ({exitCode}): {error}");
            }

            return ParseAiResponse(output);
        }

        private async Task<(string Output, string Error, int ExitCode)> ExecuteAgentInternalAsync(string command, string prompt, string label, CancellationToken cancellationToken)
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            var promptDir = Path.Combine(Directory.GetCurrentDirectory(), ".yab", "prompts", RunId ?? "last_run");
            var filename = Path.Combine(promptDir, $"prompt_{label}_{timestamp}.md");
            
            try
            {
                Directory.CreateDirectory(promptDir);
                File.WriteAllText(filename, prompt);
            }
            catch { /* Ignore logging errors */ }

            if (Verbose)
            {
                Spectre.Console.AnsiConsole.MarkupLine($"[grey]DEBUG: AI Request {label} ({timestamp}) starting...[/]");
            }

            try
            {
                var exe = command;
                var argsPrefix = "";

                if (command == "gemini" && File.Exists(HardcodedNodePath) && File.Exists(HardcodedGeminiJsPath))
                {
                    exe = HardcodedNodePath;
                    argsPrefix = $"\"{HardcodedGeminiJsPath}\" ";
                }
                else
                {
                    var cmdParts = command.Split(' ', 2);
                    exe = cmdParts[0];
                    if (cmdParts.Length > 1) argsPrefix = cmdParts[1] + " ";
                }

                var fullArgs = $"{argsPrefix}".Trim();
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = fullArgs, 
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = psi };
                process.Start();

                using (var writer = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false), 1024, leaveOpen: true))
                {
                    await writer.WriteAsync(prompt);
                    await writer.FlushAsync();
                }
                process.StandardInput.Close();

                var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
                var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
                
                try
                {
                    await process.WaitForExitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    throw;
                }

                var output = await outputTask;
                var error = await errorTask;

                try
                {
                    var responseFile = Path.Combine(promptDir, $"response_{label}_{timestamp}.txt");
                    File.WriteAllText(responseFile, $"EXIT CODE: {process.ExitCode}\n\nSTDOUT:\n{output}\n\nSTDERR:\n{error}");
                }
                catch { }

                return (output, error, process.ExitCode);
            }
            catch (Exception ex)
            {
                return ("", ex.Message, -1);
            }
        }

        public async Task<string> BuildPromptWithHashAsync(string diff, string conceptDocs, string validationJson, string hash)
        {
            var basePrompt = await BuildPromptAsync(diff, conceptDocs, validationJson);
            if (!string.IsNullOrEmpty(hash))
            {
                basePrompt = basePrompt.Replace("STRUCTURAL HASH: [Not Provided]", $"STRUCTURAL HASH: {hash}");
            }
            return basePrompt;
        }

        public async Task<string> BuildPromptAsync(string diff, string conceptDocs, string validationJson)
        {
            var rootDir = Directory.GetCurrentDirectory();
            var agentPath = Path.Combine(rootDir, AgentFile.Replace('/', Path.DirectorySeparatorChar));
            var agentInstructions = File.Exists(agentPath) ? await File.ReadAllTextAsync(agentPath) : "";

            // Strip physical anchors from docs sent to AI to focus on logic
            var cleanDocs = System.Text.RegularExpressions.Regex.Replace(conceptDocs, @"\[yab-hash:.*?\]", "");

            return $@"{agentInstructions}

---
Please review these changes for business logic integrity.

CONTEXT:
1. Business Logic Docs:
{cleanDocs}

2. Staged Changes (Diff):
{diff}

3. Structural Validation Results:
{validationJson}

4. Structural Hash:
STRUCTURAL HASH: [Not Provided]

Refer to the instructions above for specific review criteria and response format.
Note: Structural integrity (hashes) and cryptographic anchors are handled by the YAB CLI. Your review should focus exclusively on whether the code's logic correctly implements the business concepts and intent.
";
        }

        private static AiReviewResult ParseAiResponse(string output)
        {
            var trimmed = output.Trim();
            if (trimmed.IndexOf("PASSED", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new AiReviewResult(true, null);
            }

            if (trimmed.IndexOf("BLOCKED", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var index = trimmed.IndexOf("BLOCKED", StringComparison.OrdinalIgnoreCase);
                var reason = trimmed.Substring(index + "BLOCKED".Length).Trim();
                if (reason.StartsWith(":") || reason.StartsWith("-"))
                {
                    reason = reason.Substring(1).Trim();
                }
                return new AiReviewResult(false, reason);
            }

            return new AiReviewResult(false, $"Unexpected AI response format: {output}");
        }
    }
}
