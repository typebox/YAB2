using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Yab.Cli.Models;

namespace Yab.Cli.Services
{
    public class SignOffService
    {
        private readonly DocumentationData _data;

        public SignOffService(DocumentationData data)
        {
            _data = data;
        }

        public async Task SignOffBlockAsync(CodeBlock block, string? rootPath = null)
        {
            var hashTag = $"[yab-hash:{block.Name}:{block.Hash}]";
            bool updated = false;

            var siblingMd = block.FilePath.Replace(".cs", ".md");

            // Priority 1: Internal Documentation in .cs file
            if (File.Exists(block.FilePath))
            {
                var csContent = await File.ReadAllTextAsync(block.FilePath);
                bool hasInternalDocs = !string.IsNullOrEmpty(block.Documentation) || csContent.Contains("/*yab-docs");
                bool hasSiblingMd = File.Exists(siblingMd);

                // If it already has internal docs, or it DOESN'T have a sibling MD file, use the .cs file
                if (hasInternalDocs || !hasSiblingMd)
                {
                    if (!csContent.Contains(hashTag))
                    {
                        var pattern = $@"\[yab-hash:{block.Name}:.*?\]";
                        if (Regex.IsMatch(csContent, pattern))
                        {
                            csContent = Regex.Replace(csContent, pattern, hashTag);
                        }
                        else
                        {
                            if (csContent.Contains("/*yab-docs"))
                            {
                                csContent = csContent.Replace("/*yab-docs", "/*yab-docs\n    " + hashTag);
                            }
                            else
                            {
                                // Find the end of the method/class if possible, or just add at the end
                                // For now, adding at the end of the file is safest
                                csContent += "\n\n/*yab-docs\n" + hashTag + "\n*/";
                            }
                        }
                        await File.WriteAllTextAsync(block.FilePath, csContent);
                        updated = true;
                    }
                    else
                    {
                        updated = true; // Already up to date
                    }
                }
            }

            if (updated) return;

            // Priority 2: External MD files
            string? mdPath = null;

            if (File.Exists(siblingMd))
            {
                mdPath = siblingMd;
            }
            else
            {
                var conceptMd = _data.MarkdownFiles.FirstOrDefault(f => 
                    f.Value.Metadata != null && block.Concepts.Contains(f.Value.Metadata.Concept)).Key;
                mdPath = conceptMd;
            }

            if (mdPath == null) return;
            if (mdPath.StartsWith("virtual://")) mdPath = mdPath.Substring(10);
            if (!Path.IsPathRooted(mdPath) && rootPath != null) mdPath = Path.Combine(rootPath, mdPath);
            
            if (!File.Exists(mdPath)) return;

            var content = await File.ReadAllTextAsync(mdPath);
            
            if (content.Contains(hashTag)) return;

            var patternMd = $@"\[yab-hash:{block.Name}:.*?\]";
            if (Regex.IsMatch(content, patternMd))
            {
                content = Regex.Replace(content, patternMd, hashTag);
            }
            else
            {
                if (content.Contains("## Physical Anchors"))
                {
                    content = content.Replace("## Physical Anchors", "## Physical Anchors\n" + hashTag);
                }
                else
                {
                    content += "\n\n## Physical Anchors\n" + hashTag;
                }
            }

            await File.WriteAllTextAsync(mdPath, content);
        }
        public async Task UpdateAuditFeedbackAsync(CodeBlock block, string message, string? rootPath = null)
        {
            var status = message.Contains("PASSED", StringComparison.OrdinalIgnoreCase) ? "PASSED" : "BLOCKED";
            // Clean message for single-line tag if needed, but we'll try to keep it readable
            var cleanMessage = message.Replace("\n", " ").Replace("\r", "").Trim();
            var auditTag = $"[yab-audit:{block.Name}:{status} - {cleanMessage}]";
            bool updated = false;

            var siblingMd = block.FilePath.Replace(".cs", ".md");

            if (File.Exists(block.FilePath))
            {
                var csContent = await File.ReadAllTextAsync(block.FilePath);
                bool hasInternalDocs = csContent.Contains("/*yab-docs");

                if (hasInternalDocs)
                {
                    var pattern = $@"\[yab-audit:{block.Name}:.*?\]";
                    if (Regex.IsMatch(csContent, pattern))
                    {
                        csContent = Regex.Replace(csContent, pattern, auditTag);
                    }
                    else
                    {
                        // Add after the hash tag for this block if it exists
                        var hashPattern = $@"\[yab-hash:{block.Name}:.*?\]";
                        if (Regex.IsMatch(csContent, hashPattern))
                        {
                            var match = Regex.Match(csContent, hashPattern);
                            csContent = csContent.Insert(match.Index + match.Length, "\n    " + auditTag);
                        }
                        else
                        {
                            csContent = csContent.Replace("/*yab-docs", "/*yab-docs\n    " + auditTag);
                        }
                    }
                    await File.WriteAllTextAsync(block.FilePath, csContent);
                    updated = true;
                }
            }

            if (updated) return;

            // Update MD file if no internal docs found
            string? mdPath = File.Exists(siblingMd) ? siblingMd : _data.MarkdownFiles.FirstOrDefault(f => f.Value.Metadata != null && block.Concepts.Contains(f.Value.Metadata.Concept)).Key;
            
            if (mdPath == null) return;
            if (mdPath.StartsWith("virtual://")) mdPath = mdPath.Substring(10);
            if (!Path.IsPathRooted(mdPath) && rootPath != null) mdPath = Path.Combine(rootPath, mdPath);
            if (!File.Exists(mdPath)) return;

            var content = await File.ReadAllTextAsync(mdPath);
            var patternMd = $@"\[yab-audit:{block.Name}:.*?\]";
            if (Regex.IsMatch(content, patternMd))
            {
                content = Regex.Replace(content, patternMd, auditTag);
            }
            else
            {
                var hashPattern = $@"\[yab-hash:{block.Name}:.*?\]";
                if (Regex.IsMatch(content, hashPattern))
                {
                    var match = Regex.Match(content, hashPattern);
                    content = content.Insert(match.Index + match.Length, "\n" + auditTag);
                }
                else
                {
                    content += "\n" + auditTag;
                }
            }

            await File.WriteAllTextAsync(mdPath, content);
        }
    }
}
