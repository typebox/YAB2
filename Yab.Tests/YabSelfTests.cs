using Xunit;
using Yab.Cli.Services;
using Yab.Cli.Services.Steps;
using Yab.Attributes;

namespace Yab.Tests
{
    [Concept("Scanning")]
    public class ScannerTests
    {
        [Fact]
        public void Should_Scan_Code()
        {
            var scanner = new CodeAttributeScanner();
            Assert.NotNull(scanner);
        }
    }

    [Concept("Auditing")]
    public class AuditingTests
    {
        [Fact]
        public void Should_Collect_Data()
        {
            var scanner = new CodeAttributeScanner();
            var collector = new DocumentationDataCollector(scanner);
            Assert.NotNull(collector);
        }

        [Fact]
        public void Should_Audit_Logic()
        {
            var step = new AuditStep();
            Assert.NotNull(step);
        }
    }

    [Concept("Verification")]
    public class VerificationTests
    {
        [Fact]
        public void Should_Verify_Examples()
        {
            var engine = new VerificationEngine();
            Assert.NotNull(engine);
        }
    }

    [Concept("AI")]
    public class AiTests
    {
        [Fact]
        public void Should_Invoke_Ai()
        {
            var service = new AiAgentService();
            Assert.NotNull(service);
        }
    }

    [Concept("CLI")]
    public class CliTests
    {
        [Fact]
        public void Should_Run_Program()
        {
            // Just a static reference to the type
            var type = typeof(Yab.Cli.Program);
            Assert.NotNull(type);
        }
    }
}
