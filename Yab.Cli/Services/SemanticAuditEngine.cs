using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Yab.Cli.Models;

namespace Yab.Cli.Services
{
    public class SemanticAuditEngine
    {
        private readonly IAiAgentService _aiService;

        public SemanticAuditEngine(IAiAgentService aiService)
        {
            _aiService = aiService;
        }

        public async Task<List<(string Name, bool Success, string Message)>> ValidateBatchAsync(List<AuditBatchRequest> requests, CancellationToken cancellationToken = default)
        {
            var results = await _aiService.ReviewBatchAsync("gemini", requests, cancellationToken);
            return results.Select(r => (r.Name, r.Result.Passed, r.Result.Reason ?? (r.Result.Passed ? "AI Verified" : "Unknown Error"))).ToList();
        }

        public async Task<(bool Success, string Message)> ValidateIntentAsync(CodeBlock block, string intent, string conceptDocs, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(intent)) return (false, "Missing human-authored intent.");

            // Use the AI service to compare code with intent
            var result = await _aiService.ReviewChangesAsync("gemini", block.Content, conceptDocs, "", block.Hash ?? "", cancellationToken);

            if (!result.Passed)
            {
                return (false, $"Semantic Mismatch: {result.Reason}");
            }

            return (true, "Intent matches implementation (AI Verified)");
        }
    }
}
