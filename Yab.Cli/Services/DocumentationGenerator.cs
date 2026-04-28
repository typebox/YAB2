using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Yab.Cli.Models;

namespace Yab.Cli.Services
{
    public class DocumentationGenerator
    {
        public void GeneratePortal(DocumentationData data, string outputPath)
        {
            var jsonData = JsonSerializer.Serialize(data);
            jsonData = jsonData.Replace("</script>", "<\\/script>");
            
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "Yab.Cli.Resources.PortalTemplate.html";

            using (Stream stream = assembly.GetManifestResourceStream(resourceName)!)
            using (StreamReader reader = new StreamReader(stream))
            {
                string template = reader.ReadToEnd();
                string html = template.Replace("{{JSON_DATA}}", jsonData);
                File.WriteAllText(outputPath, html);
            }
        }

        public void GenerateMasterLedger(DocumentationData data, string outputPath)
        {
            using (var writer = new StreamWriter(outputPath))
            {
                writer.WriteLine("# BUILD_CERTIFICATE.md");
                writer.WriteLine($"Generated: {DateTime.Now}");
                writer.WriteLine($"Commit: {data.GitCommit}");
                writer.WriteLine();
                writer.WriteLine("## Concept Sign-offs");
                foreach (var mdFile in data.MarkdownFiles.Values)
                {
                    if (mdFile.Metadata != null)
                    {
                        writer.WriteLine($"### {mdFile.Metadata.Concept}");
                        writer.WriteLine($"- **Status**: {mdFile.Metadata.Status}");
                        writer.WriteLine("- **Business Rules**:");
                        foreach (var rule in mdFile.Metadata.Rules) { writer.WriteLine($"  - [{rule.Id}] {rule.Description}"); }
                        writer.WriteLine();
                    }
                }
                writer.WriteLine("## Code Implementation Hashes");
                writer.WriteLine("| Component | Hash | Confidence | Status |");
                writer.WriteLine("| --- | --- | --- | --- |");
                foreach (var block in data.Blocks) { writer.WriteLine($"| {block.Name} | `{(block.Hash != null ? block.Hash.Substring(0, 8) : "N/A")}` | {block.ConfidenceScore}% | {block.VerificationStatus} |"); }
            }
        }
    }
}
