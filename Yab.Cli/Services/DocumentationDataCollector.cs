using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Yab.Cli.Models;
using Yab.Cli.Services;

namespace Yab.Cli.Services
{
    public class DocumentationDataCollector
    {
        private readonly CodeAttributeScanner _scanner;
        private DocumentationData? _data;
        private List<string>? _driftWarnings;
        private List<Suggestion>? _suggestions;
        private SignOffService? _signOffService;
        private string? _rootPath;

        public DocumentationDataCollector(CodeAttributeScanner scanner)
        {
            _scanner = scanner;
        }

        public (DocumentationData Data, List<string> DriftWarnings) Collect(string rootPath)
        {
            var context = new PipelineContext { RootPath = rootPath };
            Collect(context);
            return (context.Data, context.DriftWarnings);
        }

        public void Collect(PipelineContext context)
        {
            _rootPath = context.RootPath;
            var discovery = new FileDiscoveryService(_rootPath);
            _data = context.Data;
            _driftWarnings = context.DriftWarnings;
            _suggestions = context.Suggestions;
            _signOffService = new SignOffService(_data);

            var codeBlocks = _scanner.ScanDirectory(_rootPath, discovery);
            
            // Also scan .feature files for original Gherkin syntax
            var featureScanner = new FeatureFileScanner();
            var featureFiles = discovery.EnumerateFiles(_rootPath, "*.feature");
            foreach (var featureFile in featureFiles)
            {
                codeBlocks.AddRange(featureScanner.ScanFile(featureFile));
            }

            _data.Blocks = codeBlocks;

            var mdFiles = discovery.EnumerateFiles(_rootPath!, "*.md");
            var deserializer = new YamlDotNet.Serialization.DeserializerBuilder()
                .IgnoreUnmatchedProperties()
                .Build();

            foreach (var mdFile in mdFiles)
            {
                var fullContent = File.ReadAllText(mdFile);
                var mdFileData = new MarkdownFile { Content = fullContent };

                // Parse YAML Front Matter
                if (fullContent.StartsWith("---"))
                {
                    var parts = fullContent.Split(new[] { "---" }, 3, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        try
                        {
                            mdFileData.Metadata = deserializer.Deserialize<ConceptMetadata>(parts[0]);
                            mdFileData.Content = parts[1].Trim();
                        }
                        catch (Exception ex)
                        {
                            _driftWarnings!.Add($"Failed to parse YAML front matter in {mdFile}: {ex.Message}");
                        }
                    }
                }

                _data!.MarkdownFiles[mdFile] = mdFileData;
            }

            // Step 2: Detect tests and infer concepts
            foreach (var block in _data.Blocks)
            {
                if (block.FilePath?.Contains("Tests", StringComparison.OrdinalIgnoreCase) == true || 
                    block.FilePath?.Contains("Steps", StringComparison.OrdinalIgnoreCase) == true ||
                    block.Name?.Contains("Steps", StringComparison.OrdinalIgnoreCase) == true ||
                    block.Content?.Contains("Fact") == true || 
                    block.Content?.Contains("Theory") == true ||
                    block.Content?.Contains("Test") == true ||
                    block.Content?.Contains("SkippableFact") == true ||
                    block.Content?.Contains("[Binding]") == true ||
                    block.Content?.Contains("[Given") == true ||
                    block.Content?.Contains("[When") == true ||
                    block.Content?.Contains("[Then") == true ||
                    block.FilePath?.EndsWith(".feature", StringComparison.OrdinalIgnoreCase) == true)
                {
                    block.IsTest = true;
                }
            }
            InferConceptsFromTests(_data);

            // Propagate concepts from Classes to Methods
            foreach (var fileBlocks in _data.Blocks.GroupBy(b => b.FilePath))
            {
                var classBlocks = fileBlocks.Where(b => !b.IsTest && !b.Name.Contains(".")).ToList();
                var methodBlocks = fileBlocks.Where(b => b.Name.Contains(".")).ToList();

                foreach (var cb in classBlocks)
                {
                    foreach (var mb in methodBlocks)
                    {
                        if (mb.Name.StartsWith(cb.Name + "."))
                        {
                            foreach (var concept in cb.Concepts)
                            {
                                if (!mb.Concepts.Contains(concept)) mb.Concepts.Add(concept);
                            }
                        }
                    }
                }
            }

            // Step 3: Associate blocks with MD files or internal documentation
            foreach (var block in _data.Blocks)
            {
                // If block has internal documentation, use it
                if (!string.IsNullOrEmpty(block.Documentation))
                {
                    ProcessInternalDocumentation(block, _data, _driftWarnings, deserializer);
                }
                else
                {
                    // Fallback to sibling MD or Concept match
                    AssociateWithExternalDocumentation(block, _data, _driftWarnings!);
                }
            }

            // Step 3: Enforce concept-only linking for production code
            foreach (var block in _data.Blocks.Where(b => !b.IsTest && b.Concepts.Count == 0))
            {
                if (!string.IsNullOrEmpty(block.Intent) || !string.IsNullOrEmpty(block.Documentation))
                {
                    _driftWarnings.Add($"Traceability Violation: {block.Name} is not referenced by any test and has no documentation concepts.");
                }
            }

            // Step 4: Final Pass - Extract all audit feedback from all associated documentation
            foreach (var block in _data.Blocks)
            {
                string? docContent = null;
                if (!string.IsNullOrEmpty(block.Documentation)) docContent = block.Documentation;
                else
                {
                    // Find associated MD file content
                    var md = _data.MarkdownFiles.Values.FirstOrDefault(m => m.Metadata != null && block.Concepts.Contains(m.Metadata.Concept));
                    if (md != null) docContent = md.Content;
                }

                if (!string.IsNullOrEmpty(docContent))
                {
                    // Use a more global search for this block's audit tag within the documentation
                    var auditPattern = $@"\[yab-audit:{Regex.Escape(block.Name)}:(.*?)\]";
                    var auditMatch = Regex.Match(docContent, auditPattern);
                    if (auditMatch.Success)
                    {
                        block.SemanticReviewMessage = auditMatch.Groups[1].Value.Trim();
                    }
                    else if (block.Name.Contains("."))
                    {
                        // If it's a method, also check its parent class's documentation
                        var className = block.Name.Split('.')[0];
                        var classBlock = _data.Blocks.FirstOrDefault(b => b.Name == className && b.FilePath == block.FilePath);
                        if (classBlock != null && !string.IsNullOrEmpty(classBlock.Documentation))
                        {
                            var classAuditMatch = Regex.Match(classBlock.Documentation, auditPattern);
                            if (classAuditMatch.Success)
                            {
                                block.SemanticReviewMessage = classAuditMatch.Groups[1].Value.Trim();
                            }
                        }
                    }
                }
            }
        }

        private void ProcessInternalDocumentation(CodeBlock block, DocumentationData data, List<string> driftWarnings, YamlDotNet.Serialization.IDeserializer deserializer)
        {
            var content = block.Documentation!;
            ConceptMetadata? metadata = null;
            
            // Parse YAML Front Matter if present in comment
            if (content.Contains("---"))
            {
                var parts = content.Split(new[] { "---" }, 2, StringSplitOptions.None);
                if (parts.Length == 2)
                {
                    var metadataPart = parts[0];
                    // Strip tags from metadata part before parsing YAML
                    metadataPart = Regex.Replace(metadataPart, @"\[yab-hash:.*?\]", "");
                    metadataPart = Regex.Replace(metadataPart, @"\[yab-audit:.*?\]", "");
                    
                    try
                    {
                        metadata = deserializer.Deserialize<ConceptMetadata>(metadataPart);
                    }
                    catch { /* Ignore parse errors */ }
                    
                    content = parts[1].Trim();
                }
            }

            // Register as a virtual markdown file if it has a concept and we don't have one yet
            var concept = metadata?.Concept ?? block.Concepts.FirstOrDefault();
            if (!string.IsNullOrEmpty(concept))
            {
                if (metadata == null) metadata = new ConceptMetadata { Concept = concept };
                if (string.IsNullOrEmpty(metadata.Concept)) metadata.Concept = concept;

                if (!data.MarkdownFiles.Any(f => f.Value.Metadata?.Concept == concept))
                {
                    data.MarkdownFiles[$"virtual://{block.FilePath}"] = new MarkdownFile 
                    { 
                        Content = content,
                        Metadata = metadata 
                    };
                }

                // Ensure block is associated with this concept
                if (!block.Concepts.Contains(concept))
                {
                    block.Concepts.Add(concept);
                }
            }

            // Check for drift
            VerifyDrift(block, content, "Internal Comment");
        }

        private void AssociateWithExternalDocumentation(CodeBlock block, DocumentationData data, List<string> driftWarnings)
        {
            var csFile = Path.GetFullPath(block.FilePath);
            var siblingMd = csFile.Replace(".cs", ".md");

            foreach (var mdPair in data.MarkdownFiles)
            {
                var mdFile = Path.GetFullPath(mdPair.Key);
                var mdData = mdPair.Value;
                var metadata = mdData.Metadata;

                bool isSibling = string.Equals(mdFile, siblingMd, StringComparison.OrdinalIgnoreCase);
                bool isConceptMatch = (metadata != null && block.Concepts.Contains(metadata.Concept));
                bool sourceHasSibling = File.Exists(siblingMd);

                bool belongsToThisMd = isSibling || (!sourceHasSibling && isConceptMatch);

                if (belongsToThisMd)
                {
                    // Inherit concept if missing (mostly for tests)
                    if (metadata != null && !block.Concepts.Contains(metadata.Concept))
                    {
                        block.Concepts.Add(metadata.Concept);
                    }

                    VerifyDrift(block, mdData.Content, Path.GetFileName(mdFile));
                }
            }
        }

        private void VerifyDrift(CodeBlock block, string documentationContent, string sourceName)
        {
            var hashTag = $"[yab-hash:{block.Name}:{block.Hash}]";
            if (!documentationContent.Contains(hashTag))
            {
                if (documentationContent.Contains($"[yab-hash:{block.Name}:"))
                {
                    block.ConfidenceScore = 50.0;
                    block.VerificationStatus = "DRIFTED";
                    var msg = $"Logic Drift in {block.Name} ({Path.GetFileName(block.FilePath)}). Code changed but {sourceName} not re-signed.";
                    _driftWarnings!.Add(msg);
                    
                    _suggestions!.Add(new Suggestion
                    {
                        Title = "Logic Drift Detected!",
                        Description = msg,
                        ActionText = "Update Physical Anchor",
                        ApplyAsync = () => _signOffService!.SignOffBlockAsync(block, _rootPath)
                    });
                }
                else
                {
                    block.ConfidenceScore = 0.0;
                    block.VerificationStatus = "UNVERIFIED";
                    var msg = $"Missing physical anchor for {block.Name} in {sourceName}. Add {hashTag} to sign off.";
                    _driftWarnings!.Add(msg);

                    _suggestions!.Add(new Suggestion
                    {
                        Title = "Missing Physical Anchor",
                        Description = msg,
                        ActionText = "Sign Off Now",
                        ApplyAsync = () => _signOffService!.SignOffBlockAsync(block, _rootPath)
                    });
                }
            }
            else
            {
                block.ConfidenceScore = 100.0;
                block.VerificationStatus = "VERIFIED";
            }

            // Extract audit feedback from documentation if present
            var auditPattern = $@"\[yab-audit:{Regex.Escape(block.Name)}:(.*?)\]";
            var auditMatch = Regex.Match(documentationContent, auditPattern);
            if (auditMatch.Success)
            {
                block.SemanticReviewMessage = auditMatch.Groups[1].Value.Trim();
            }
        }

        private void InferConceptsFromTests(DocumentationData data)
        {
            var productionBlocks = data.Blocks.Where(b => !b.IsTest).ToList();
            var testBlocks = data.Blocks.Where(b => b.IsTest).ToList();

            foreach (var test in testBlocks)
            {
                if (test.Concepts.Count == 0) continue;

                foreach (var reference in test.References)
                {
                    // If a test references something with the same name as a production block
                    // or if the reference is a method in a class (e.g. ValidateFunds matches TransferService.ValidateFunds)
                    var targets = productionBlocks.Where(pb => pb.Name == reference || pb.Name.EndsWith("." + reference)).ToList();
                    foreach (var target in targets)
                    {
                        foreach (var concept in test.Concepts)
                        {
                            if (!target.Concepts.Contains(concept))
                            {
                                target.Concepts.Add(concept);
                            }
                        }
                    }
                }
            }

            // Also propagate from classes to methods (all blocks)
            foreach (var fileBlocks in data.Blocks.GroupBy(b => b.FilePath))
            {
                var classBlocks = fileBlocks.Where(b => !b.Name.Contains(".")).ToList();
                var methodBlocks = fileBlocks.Where(b => b.Name.Contains(".")).ToList();

                foreach (var cb in classBlocks)
                {
                    foreach (var mb in methodBlocks)
                    {
                        if (mb.Name.StartsWith(cb.Name + "."))
                        {
                            foreach (var concept in cb.Concepts)
                            {
                                if (!mb.Concepts.Contains(concept)) mb.Concepts.Add(concept);
                            }
                        }
                    }
                }
            }
            BuildCoverageOverlap(_data);
        }

        public void BuildCoverageOverlap(DocumentationData data)
        {
            // For each production block that has verifying tests...
            foreach (var block in data.Blocks.Where(b => !b.IsTest && b.VerifyingTests.Count > 0))
            {
                // Find which test blocks correspond to the verifying test IDs
                var bddTests = new List<string>();
                var unitTests = new List<string>();
                
                foreach (var testId in block.VerifyingTests)
                {
                    var testBlock = data.Blocks.FirstOrDefault(b => b.IsTest && b.Name == testId);
                    if (testBlock != null)
                    {
                        bool isBdd = testBlock.FilePath.EndsWith(".feature.cs") 
                            || testBlock.Name.Contains("Steps") 
                            || testBlock.Content.Contains("[Binding]");
                        if (isBdd) bddTests.Add(testId);
                        else unitTests.Add(testId);
                    }
                    else
                    {
                        // Test not found as a block, classify by name pattern
                        if (testId.Contains("Steps")) bddTests.Add(testId);
                        else unitTests.Add(testId);
                    }
                }
                
                // If both BDD and unit tests hit this code, they're related via coverage overlap
                if (bddTests.Count > 0 && unitTests.Count > 0)
                {
                    block.CoverageOverlap = new CoverageOverlapInfo
                    {
                        BddTests = bddTests,
                        UnitTests = unitTests
                    };
                }
            }
        }
    }
}
