using System;
using System.IO;
using System.Linq;
using Xunit;
using Yab.Cli.Services;
using Yab.Attributes;

namespace Yab.Tests
{
    [Concept("Verification")]
    [Intent("Unit tests for the VerificationEngine, ensuring markdown code snippets are correctly identified.")]
    public class VerificationEngineTests : IDisposable
    {
        private readonly string _testDir;

        public VerificationEngineTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "YabVerificationTests_" + Guid.NewGuid().ToString("N"));
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
        [Intent("Verifies that markdown files with 'yab-run' code blocks are correctly identified by the engine.")]
        public void VerifyExamples_ShouldDetectYabRunBlocks()
        {
            // Arrange
            var engine = new VerificationEngine();
            var mdPath = Path.Combine(_testDir, "sample.md");
            File.WriteAllText(mdPath, @"# Sample
```csharp yab-run
var x = 1;
```
```javascript
console.log('skip me');
```");

            // Act
            var results = engine.VerifyExamples(_testDir);

            // Assert
            Assert.Contains(results, r => r.Contains("Verifying example in") && r.Contains("sample.md"));
            Assert.Contains(results, r => r.Contains("[PASS]") && r.Contains("syntactically valid"));
        }

        [Fact]
        [Intent("Ensures that code blocks without 'yab-run' are ignored by the verification engine.")]
        public void VerifyExamples_ShouldIgnoreNonYabRunBlocks()
        {
            // Arrange
            var engine = new VerificationEngine();
            var mdPath = Path.Combine(_testDir, "sample.md");
            File.WriteAllText(mdPath, @"
# Sample
```csharp
var x = 1;
```");

            // Act
            var results = engine.VerifyExamples(_testDir);

            // Assert
            Assert.Empty(results);
        }
    }
}
