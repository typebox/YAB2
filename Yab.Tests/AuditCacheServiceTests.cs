using System;
using System.IO;
using Xunit;
using Yab.Cli.Services;
using Yab.Attributes;

namespace Yab.Tests
{
    [Concept("Auditing")]
    [Intent("Unit tests for AuditCacheService, ensuring expensive AI audit results are cached correctly.")]
    public class AuditCacheServiceTests : IDisposable
    {
        private readonly string _testDir;

        public AuditCacheServiceTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "YabAuditCacheTests_" + Guid.NewGuid().ToString("N"));
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
        [Intent("Verifies that getting a non-existent entry returns null.")]
        public void Get_ShouldReturnNull_WhenEntryDoesNotExist()
        {
            // Arrange
            var cache = new AuditCacheService(_testDir);

            // Act
            var entry = cache.Get("Missing", "hash1", "docs");

            // Assert
            Assert.Null(entry);
        }

        [Fact]
        [Intent("Verifies that an entry can be stored and retrieved if hashes match.")]
        public void UpdateAndGet_ShouldReturnEntry_WhenHashesMatch()
        {
            // Arrange
            var cache = new AuditCacheService(_testDir);
            var blockName = "MyBlock";
            var codeHash = "code123";
            var docs = "some documentation content";

            // Act
            cache.Update(blockName, codeHash, docs, true, "All good");
            var entry = cache.Get(blockName, codeHash, docs);

            // Assert
            Assert.NotNull(entry);
            Assert.True(entry.Success);
            Assert.Equal("All good", entry.Message);
        }

        [Fact]
        [Intent("Verifies that the cache returns null if the code hash changes.")]
        public void Get_ShouldReturnNull_WhenCodeHashDrifts()
        {
            // Arrange
            var cache = new AuditCacheService(_testDir);
            var blockName = "MyBlock";
            var docs = "docs";
            cache.Update(blockName, "old-hash", docs, true, "ok");

            // Act
            var entry = cache.Get(blockName, "new-hash", docs);

            // Assert
            Assert.Null(entry);
        }

        [Fact]
        [Intent("Verifies that the cache persists to disk and can be reloaded.")]
        public void SaveAndLoad_ShouldPersistCache()
        {
            // Arrange
            var cache1 = new AuditCacheService(_testDir);
            cache1.Update("Persisted", "hash", "docs", true, "saved");
            cache1.Save();

            // Act
            var cache2 = new AuditCacheService(_testDir);
            var entry = cache2.Get("Persisted", "hash", "docs");

            // Assert
            Assert.NotNull(entry);
            Assert.Equal("saved", entry.Message);
        }
    }
}
