using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Yab.Cli.Services;
using Yab.Cli.Models;

namespace Yab.Tests
{
    public class IntegrationTests : IDisposable
    {
        private readonly string _testDir;

        public IntegrationTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "YabTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }

        [Fact]
        public void FullFlow_ShouldGeneratePortalAndLedger()
        {
            // Arrange: Create a sample CS and MD file
            var csPath = Path.Combine(_testDir, "TestService.cs");
            File.WriteAllText(csPath, @"
namespace Test {
    public class TestService {
        public void DoSomething() {}
    }
}");
            var mdPath = Path.Combine(_testDir, "TestService.md");
            File.WriteAllText(mdPath, @"---
concept: TestConcept
status: Active
---
# Test Service
[yab-hash:TestService:placeholder]
");

            var scanner = new CodeAttributeScanner();
            var collector = new DocumentationDataCollector(scanner);
            var generator = new DocumentationGenerator();

            // Act: Collect and Generate
            var (data, driftWarnings) = collector.Collect(_testDir);
            var portalPath = Path.Combine(_testDir, "LivingDocumentation.html");
            var ledgerPath = Path.Combine(_testDir, "BUILD_CERTIFICATE.md");
            
            generator.GeneratePortal(data, portalPath);
            generator.GenerateMasterLedger(data, ledgerPath);

            // Assert
            Assert.True(File.Exists(portalPath));
            Assert.True(File.Exists(ledgerPath));
            Assert.Contains("Logic Drift", driftWarnings.First()); // Should have drift due to placeholder
            
            var portalContent = File.ReadAllText(portalPath);
            Assert.Contains("TestService", portalContent);
            Assert.Contains("TestConcept", portalContent);
        }

        [Fact]
        public void DriftDetection_ShouldDetectChanges()
        {
            // Arrange
            var csPath = Path.Combine(_testDir, "Drift.cs");
            File.WriteAllText(csPath, "public class Drift { public void A() {} }");
            
            var scanner = new CodeAttributeScanner();
            var blocks = scanner.ScanFile(csPath);
            var classHash = blocks.First(b => b.Name == "Drift").Hash;
            var methodHash = blocks.First(b => b.Name == "Drift.A").Hash;

            var mdPath = Path.Combine(_testDir, "Drift.md");
            File.WriteAllText(mdPath, $"# Drift\n[yab-hash:Drift:{classHash}]\n[yab-hash:Drift.A:{methodHash}]");

            var collector = new DocumentationDataCollector(scanner);

            // Act 1: Initial check (should pass)
            var (data1, warnings1) = collector.Collect(_testDir);
            Assert.Empty(warnings1);

            // Act 2: Change code (A -> B)
            File.WriteAllText(csPath, "public class Drift { public void B() {} }");
            var (data2, warnings2) = collector.Collect(_testDir);

            // Assert
            Assert.NotEmpty(warnings2);
            Assert.Contains("Logic Drift in Drift", warnings2.First());
        }
        [Fact]
        public async Task AiIntegration_ShouldTriggerMockedReview()
        {
            // Arrange
            var csPath = Path.Combine(_testDir, "MockAi.cs");
            File.WriteAllText(csPath, "public class MockAi { }");
            var mdPath = Path.Combine(_testDir, "MockAi.md");
            File.WriteAllText(mdPath, "# MockAi\n[yab-hash:MockAi:drift]");

            var mockAi = new MockAiAgentService(passed: false, reason: "Grug says no.");

            // Act
            var exitCode = await Yab.Cli.Program.RunAsync(new[] { "dev", "docs", _testDir }, mockAi);

            // Assert
            Assert.Equal(0, exitCode);
            var ledger = File.ReadAllText(Path.Combine(_testDir, "BUILD_CERTIFICATE.md"));
            Assert.Contains("MockAi", ledger);
        }

        [Fact]
        public async Task AiIntegration_ShouldTriggerGeminiReview()
        {
            // Arrange
            var csPath = Path.Combine(_testDir, "AiTest.cs");
            File.WriteAllText(csPath, "public class AiTest { public void UntestedLogic() { } }");
            
            var mdPath = Path.Combine(_testDir, "AiTest.md");
            File.WriteAllText(mdPath, "# AiTest\n[yab-hash:AiTest:wrong-hash]");

            // Act
            var exitCode = await Yab.Cli.Program.RunAsync(new[] { "dev", "docs", _testDir });

            // Assert
            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(Path.Combine(_testDir, "LivingDocumentation.html")));
        }
    }

    public class MockAiAgentService : IAiAgentService
    {
        private readonly bool _passed;
        private readonly string _reason;

        public string? RunId { get; set; }
        public bool Verbose { get; set; }
        public bool PromptOnly { get; set; }

        public MockAiAgentService(bool passed, string reason)
        {
            _passed = passed;
            _reason = reason;
        }

        public Task<AiReviewResult> ReviewChangesAsync(string command, string diff, string conceptDocs, string validationJson, string hash = "", CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AiReviewResult(_passed, _reason));
        }

        public Task<List<(string Name, AiReviewResult Result)>> ReviewBatchAsync(string command, List<AuditBatchRequest> requests, CancellationToken cancellationToken = default)
        {
            var results = requests.Select(r => (r.Name, new AiReviewResult(_passed, _reason))).ToList();
            return Task.FromResult(results);
        }

        public Task<string> BuildPromptAsync(string diff, string conceptDocs, string validationJson)
        {
            return Task.FromResult("Mock Prompt");
        }
    }
}
