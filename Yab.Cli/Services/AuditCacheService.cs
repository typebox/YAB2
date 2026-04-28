using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Yab.Cli.Services
{
    public class AuditCacheEntry
    {
        public required string CodeHash { get; set; }
        public required string DocsHash { get; set; }
        public bool Success { get; set; }
        public string? Message { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /*yab-docs
    description: Prevents redundant AI audits by caching results based on code and documentation hashes.
    ---
    # AuditCacheService

    Stores and retrieves previous AI audit results to speed up the development loop and reduce API costs.

    ## Physical Anchors
    [yab-hash:AuditCacheService:A8rd5+5f8lNRw49TvKwumKiuqhnyIluqCkTNMJPZUtA=]
    [yab-hash:Get:Bgp636TgRvhUpKj7opqYVmve1lcj+IzrGs0syGxya8E=]
    [yab-hash:Update:kaJIvNGgeVh/dP0vGh2L6iAi8s3/cyV+ZIIQav6gGSU=]
    [yab-hash:Save:awuZhFOjiPYa43Wef8GXg4nz/hTLCdXpqeM16FibIOc=]
    [yab-hash:Load:8FsanXz8NwVZ4wG9Gk8M8+NTheba9m4VlxiDBtrckas=]
    */
    public class AuditCacheService
    {
        private readonly string _cachePath;
        private Dictionary<string, AuditCacheEntry> _cache = new();

        public AuditCacheService(string rootPath)
        {
            _cachePath = Path.Combine(rootPath, ".yab", "audit-cache.json");
            Load();
        }

        private void Load()
        {
            if (File.Exists(_cachePath))
            {
                try {
                    var json = File.ReadAllText(_cachePath);
                    _cache = JsonSerializer.Deserialize<Dictionary<string, AuditCacheEntry>>(json) ?? new();
                } catch { _cache = new(); }
            }
        }

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(_cachePath);
                if (dir != null) Directory.CreateDirectory(dir);
                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(_cachePath, JsonSerializer.Serialize(_cache, options));
            }
            catch { /* Ignore cache save errors */ }
        }

        public AuditCacheEntry? Get(string blockName, string codeHash, string docsContent)
        {
            var docsHash = ComputeHash(docsContent);
            if (_cache.TryGetValue(blockName, out var entry))
            {
                if (entry.CodeHash == codeHash && entry.DocsHash == docsHash)
                {
                    return entry;
                }
            }
            return null;
        }

        public void Update(string blockName, string codeHash, string docsContent, bool success, string? message)
        {
            _cache[blockName] = new AuditCacheEntry
            {
                CodeHash = codeHash,
                DocsHash = ComputeHash(docsContent),
                Success = success,
                Message = message,
                Timestamp = DateTime.UtcNow
            };
        }

        public void UpdateManual(string blockName, bool success, string? message)
        {
            if (_cache.TryGetValue(blockName, out var entry))
            {
                entry.Success = success;
                entry.Message = message;
                entry.Timestamp = DateTime.UtcNow;
            }
            else
            {
                // If not in cache, we can't update hashes reliably here, 
                // but we can store it with empty hashes which will be updated next run
                _cache[blockName] = new AuditCacheEntry
                {
                    CodeHash = "",
                    DocsHash = "",
                    Success = success,
                    Message = message,
                    Timestamp = DateTime.UtcNow
                };
            }
        }

        private static string ComputeHash(string input)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToBase64String(bytes);
        }
    }
}
