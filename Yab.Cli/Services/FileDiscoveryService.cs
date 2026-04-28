using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Yab.Cli.Services
{
    public class FileDiscoveryService
    {
        private static readonly string[] DefaultExclusions = { 
            ".git", ".gemini", "bin", "obj", ".vs", "node_modules", 
            "LivingDocumentation.html", "BUILD_CERTIFICATE.md", ".yab"
        };
        private readonly List<string> _gitIgnoredPatterns = new List<string>();
        private readonly string _rootPath;

        public FileDiscoveryService(string rootPath)
        {
            _rootPath = rootPath;
            var gitIgnorePath = Path.Combine(rootPath, ".gitignore");
            if (File.Exists(gitIgnorePath))
            {
                _gitIgnoredPatterns = File.ReadAllLines(gitIgnorePath)
                    .Select(line => line.Trim())
                    .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("#"))
                    .ToList();
            }
        }

        public List<string> EnumerateFiles(string directory, string searchPattern)
        {
            var allFiles = Directory.EnumerateFiles(directory, searchPattern, SearchOption.AllDirectories);
            return allFiles.Where(file => !IsIgnored(file)).ToList();
        }

        private bool IsIgnored(string filePath)
        {
            var relativePath = Path.GetRelativePath(_rootPath, filePath).Replace('\\', '/');
            var pathParts = relativePath.Split('/');

            // Check default exclusions in any part of the path
            foreach (var part in pathParts)
            {
                if (DefaultExclusions.Any(e => e.Equals(part, StringComparison.OrdinalIgnoreCase))) 
                    return true;
            }

            // Check .gitignore patterns
            foreach (var pattern in _gitIgnoredPatterns)
            {
                var cleanPattern = pattern.TrimEnd('/').Replace('\\', '/');
                
                // Absolute from root if starts with /
                if (cleanPattern.StartsWith("/"))
                {
                    var absolutePattern = cleanPattern.Substring(1);
                    if (relativePath.Equals(absolutePattern, StringComparison.OrdinalIgnoreCase) || 
                        relativePath.StartsWith(absolutePattern + "/", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                else
                {
                    // Match anywhere in path
                    if (relativePath.IndexOf(cleanPattern, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }

                // Handle basic extension patterns like *.log
                if (pattern.StartsWith("*."))
                {
                    var ext = pattern.Substring(1);
                    if (filePath.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) 
                        return true;
                }
            }

            return false;
        }
    }
}
