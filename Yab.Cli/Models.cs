using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Yab.Cli.Models
{
    public class CodeBlock
    {
        public required string Name { get; set; }
        public required string FilePath { get; set; }
        public int StartLine { get; set; }
        public int EndLine { get; set; }
        public required string Content { get; set; }
        public string? Hash { get; set; }
        public List<string> Concepts { get; set; } = new List<string>();
        public string? Intent { get; set; }
        public double ConfidenceScore { get; set; } = 100.0;
        public string VerificationStatus { get; set; } = "VERIFIED";
        public string? SemanticReviewMessage { get; set; }
        public bool IsTest { get; set; } = false;
        public List<string> References { get; set; } = new List<string>();
        public string? Documentation { get; set; }
        public bool RuntimeVerified { get; set; } = false;
        public List<string> VerifyingTests { get; set; } = new List<string>();
        public int StatementsCovered { get; set; }
        public int StatementsTotal { get; set; }
        public CoverageOverlapInfo? CoverageOverlap { get; set; }
    }

    public class CoverageOverlapInfo
    {
        public List<string> BddTests { get; set; } = new();
        public List<string> UnitTests { get; set; } = new();
    }

    public class ConceptMetadata
    {
        [YamlDotNet.Serialization.YamlMember(Alias = "concept")]
        public required string Concept { get; set; }
        [YamlDotNet.Serialization.YamlMember(Alias = "type")]
        public string? Type { get; set; }
        [YamlDotNet.Serialization.YamlMember(Alias = "description")]
        public string? Description { get; set; }
        [YamlDotNet.Serialization.YamlMember(Alias = "owner-history")]
        public List<OwnerHistory> OwnerHistory { get; set; } = new List<OwnerHistory>();
        [YamlDotNet.Serialization.YamlMember(Alias = "status")]
        public string? Status { get; set; }
        [YamlDotNet.Serialization.YamlMember(Alias = "audience")]
        public string? Audience { get; set; }
        [YamlDotNet.Serialization.YamlMember(Alias = "rules")]
        public List<BusinessRule> Rules { get; set; } = new List<BusinessRule>();
    }

    public class OwnerHistory
    {
        [YamlDotNet.Serialization.YamlMember(Alias = "name")]
        public required string Name { get; set; }
        [YamlDotNet.Serialization.YamlMember(Alias = "from")]
        public required string From { get; set; }
    }

    public class BusinessRule
    {
        [YamlDotNet.Serialization.YamlMember(Alias = "id")]
        public required string Id { get; set; }
        [YamlDotNet.Serialization.YamlMember(Alias = "description")]
        public required string Description { get; set; }
        [YamlDotNet.Serialization.YamlMember(Alias = "risk")]
        public string? Risk { get; set; }
    }

    public class MarkdownFile
    {
        public required string Content { get; set; }
        public ConceptMetadata? Metadata { get; set; }
    }

    public class DocumentationData
    {
        public string GitCommit { get; set; } = "HEAD";
        public List<CodeBlock> Blocks { get; set; } = new List<CodeBlock>();
        public Dictionary<string, MarkdownFile> MarkdownFiles { get; set; } = new Dictionary<string, MarkdownFile>();
        public List<string> VerificationResults { get; set; } = new List<string>();
    }

    public class Suggestion
    {
        public required string Title { get; set; }
        public required string Description { get; set; }
        public required string ActionText { get; set; }
        public required Func<Task> ApplyAsync { get; set; }
    }
}
