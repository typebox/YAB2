using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Yab.Cli.Models;

namespace Yab.Cli.Services
{
    public class CodeAttributeScanner
    {
        public List<CodeBlock> ScanDirectory(string directory, FileDiscoveryService? discovery = null)
        {
            var blocks = new List<CodeBlock>();
            var files = discovery != null 
                ? discovery.EnumerateFiles(directory, "*.cs")
                : Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories)
                    .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && 
                                !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                    .ToList();

            foreach (var file in files)
            {
                blocks.AddRange(ScanFile(file));
            }

            return blocks;
        }

        public List<CodeBlock> ScanFile(string filePath)
        {
            var code = File.ReadAllText(filePath);
            var tree = CSharpSyntaxTree.ParseText(code);
            var root = tree.GetCompilationUnitRoot();
            var blocks = new List<CodeBlock>();

            var members = root.DescendantNodes().OfType<MemberDeclarationSyntax>();

            foreach (var member in members)
            {
                // Skip interface methods to avoid naming collisions with implementations
                if (member.Parent is InterfaceDeclarationSyntax) continue;
                // We now collect everything, not just things with attributes
                var attributes = member.AttributeLists.SelectMany(al => al.Attributes).ToList();
                var conceptAttrs = attributes.Where(a => a.Name.ToString().Contains("Concept")).ToList();
                var intentAttr = attributes.FirstOrDefault(a => a.Name.ToString().Contains("Intent"));

                var block = new CodeBlock
                {
                    FilePath = filePath,
                    StartLine = tree.GetLineSpan(member.Span).StartLinePosition.Line + 1,
                    EndLine = tree.GetLineSpan(member.Span).EndLinePosition.Line + 1,
                    Content = NormalizeIndentation(member.ToString()),
                    Name = GetMemberName(member),
                    Concepts = conceptAttrs.Select(a => GetAttributeValue(a)).Where(v => v != null).Cast<string>().ToList(),
                    Intent = intentAttr != null ? GetAttributeValue(intentAttr) : null,
                    References = ExtractReferences(member)
                };

                var docContent = ExtractDocumentation(member, out var docConcepts);
                block.Documentation = docContent;
                foreach (var c in docConcepts) if (!block.Concepts.Contains(c)) block.Concepts.Add(c);

                // Only add if it's a "significant" member (Class or Method)
                if (member is ClassDeclarationSyntax || member is MethodDeclarationSyntax)
                {
                    block.Hash = CalculateHash(block.Content);
                    blocks.Add(block);
                }
            }

            return blocks;
        }

        private string GetMemberName(MemberDeclarationSyntax member)
        {
            if (member is ClassDeclarationSyntax cds) return cds.Identifier.Text;
            
            if (member is MethodDeclarationSyntax mds)
            {
                var parent = mds.Parent;
                while (parent != null && !(parent is ClassDeclarationSyntax)) parent = parent.Parent;
                if (parent is ClassDeclarationSyntax pcds)
                {
                    return $"{pcds.Identifier.Text}.{mds.Identifier.Text}";
                }
                return mds.Identifier.Text;
            }
            
            if (member is PropertyDeclarationSyntax pds) return pds.Identifier.Text;
            return "Unknown";
        }

        private List<string> ExtractReferences(MemberDeclarationSyntax member)
        {
            var refs = new HashSet<string>();
            
            // Look for type names in object creations
            var creations = member.DescendantNodes().OfType<ObjectCreationExpressionSyntax>();
            foreach (var creation in creations)
            {
                refs.Add(creation.Type.ToString());
            }

            // Look for method calls
            var invocations = member.DescendantNodes().OfType<InvocationExpressionSyntax>();
            foreach (var inv in invocations)
            {
                if (inv.Expression is IdentifierNameSyntax ins)
                {
                    refs.Add(ins.Identifier.Text);
                }
                else if (inv.Expression is MemberAccessExpressionSyntax maes)
                {
                    refs.Add(maes.Name.Identifier.Text);
                }
            }

            // Also just look for any identifier that might be a class reference
            var identifiers = member.DescendantNodes().OfType<IdentifierNameSyntax>();
            foreach (var id in identifiers)
            {
                // Filter out common language keywords or things that are likely local variables
                // In a true Grug-style scanner, we just take them all and filter against 
                // known CodeBlock names later.
                refs.Add(id.Identifier.Text);
            }

            return refs.ToList();
        }

        private string? ExtractDocumentation(MemberDeclarationSyntax member, out List<string> concepts)
        {
            concepts = new List<string>();
            var trivia = member.GetLeadingTrivia();
            foreach (var t in trivia)
            {
                if (t.IsKind(SyntaxKind.MultiLineCommentTrivia))
                {
                    var comment = t.ToString().Trim();
                    if (comment.StartsWith("/*yab-docs"))
                    {
                        var fullContent = comment.Substring(10);
                        if (fullContent.EndsWith("*/")) fullContent = fullContent.Substring(0, fullContent.Length - 2);
                        fullContent = fullContent.Trim();

                        // Still extract concepts for the CodeBlock model
                        if (fullContent.Contains("---"))
                        {
                            var parts = fullContent.Split("---", 2);
                            var lines = parts[0].Split('\n');
                            foreach (var line in lines)
                            {
                                if (line.Contains(":"))
                                {
                                    var kv = line.Split(':', 2);
                                    var key = kv[0].Trim().ToLower();
                                    var val = kv[1].Trim();
                                    if (key == "concept") concepts.Add(val);
                                }
                            }
                        }
                        return fullContent;
                    }
                }
            }
            return null;
        }

        private string NormalizeIndentation(string content)
        {
            if (string.IsNullOrEmpty(content)) return content;

            var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            if (lines.Length <= 1) return content.Trim();

            // Find minimum indentation of non-empty lines
            var minIndent = int.MaxValue;
            bool foundIndentedLine = false;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                
                int indent = 0;
                while (indent < line.Length && char.IsWhiteSpace(line[indent])) indent++;
                
                if (indent < minIndent) minIndent = indent;
                foundIndentedLine = true;
            }

            if (!foundIndentedLine) return content.Trim();

            var normalizedLines = lines.Select(line => 
                line.Length >= minIndent ? line.Substring(minIndent) : ""
            );

            return string.Join("\n", normalizedLines).Trim();
        }

        private string? GetAttributeValue(AttributeSyntax attribute)
        {
            var arg = attribute.ArgumentList?.Arguments.FirstOrDefault();
            if (arg == null) return null;
            return arg.Expression.ToString().Trim('\"');
        }

        private string CalculateHash(string content)
        {
            using (var sha256 = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(content);
                var hash = sha256.ComputeHash(bytes);
                return Convert.ToBase64String(hash);
            }
        }
    }
}
