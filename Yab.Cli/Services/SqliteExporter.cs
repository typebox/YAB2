using System;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using Yab.Cli.Models;

namespace Yab.Cli.Services
{
    public class SqliteExporter
    {
        public string Export(DocumentationData data)
        {
            var tempFile = Path.GetTempFileName();
            try
            {
                using (var conn = new SqliteConnection("Data Source=:memory:"))
                {
                    conn.Open();
                    CreateSchema(conn);
                    PopulateData(conn, data);
                    Compact(conn);

                    // Backup in-memory DB to temp file
                    using (var fileConn = new SqliteConnection($"Data Source={tempFile}"))
                    {
                        fileConn.Open();
                        conn.BackupDatabase(fileConn);
                    }
                }

                SqliteConnection.ClearAllPools();
                byte[] bytes = File.ReadAllBytes(tempFile);
                return Convert.ToBase64String(bytes);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        private void CreateSchema(SqliteConnection conn)
        {
            var sql = @"
CREATE TABLE Blocks (
    Name TEXT,
    FilePath TEXT NOT NULL,
    StartLine INTEGER,
    EndLine INTEGER,
    Content TEXT,
    Hash TEXT,
    Intent TEXT,
    ConfidenceScore REAL DEFAULT 100.0,
    Status TEXT DEFAULT 'VERIFIED',
    SemanticMessage TEXT,
    IsTest INTEGER DEFAULT 0,
    RuntimeVerified INTEGER DEFAULT 0,
    Documentation TEXT,
    StatementsCovered INTEGER DEFAULT 0,
    StatementsTotal INTEGER DEFAULT 0
);
CREATE INDEX idx_blocks_name ON Blocks(Name);

CREATE TABLE Concepts (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    BlockName TEXT NOT NULL,
    ConceptName TEXT NOT NULL
);
CREATE INDEX idx_concepts_block ON Concepts(BlockName);
CREATE INDEX idx_concepts_name ON Concepts(ConceptName);

CREATE TABLE BlockReferences (
    BlockName TEXT NOT NULL,
    RefName TEXT NOT NULL
);

CREATE TABLE VerifyingTests (
    BlockName TEXT NOT NULL,
    TestId TEXT NOT NULL
);

CREATE TABLE CoverageOverlap (
    BlockName TEXT NOT NULL,
    TestId TEXT NOT NULL,
    TestType TEXT NOT NULL
);

CREATE TABLE MarkdownFiles (
    Path TEXT PRIMARY KEY,
    Content TEXT,
    Concept TEXT,
    Description TEXT,
    Status TEXT,
    Audience TEXT,
    Type TEXT
);

CREATE TABLE BusinessRules (
    MdPath TEXT NOT NULL,
    RuleId TEXT NOT NULL,
    Description TEXT,
    Risk TEXT
);

CREATE TABLE OwnerHistory (
    MdPath TEXT NOT NULL,
    Name TEXT NOT NULL,
    FromDate TEXT
);

CREATE TABLE Metadata (
    Key TEXT PRIMARY KEY,
    Value TEXT
);

CREATE VIRTUAL TABLE BlockSearch USING fts5(
    Name, Content, Intent, ConceptNames
);
";
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        private void PopulateData(SqliteConnection conn, DocumentationData data)
        {
            using var tx = conn.BeginTransaction();

            // Insert Metadata
            InsertMetadata(conn, "GitCommit", data.GitCommit);

            // Insert Blocks
            foreach (var block in data.Blocks)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"INSERT INTO Blocks 
                    (Name, FilePath, StartLine, EndLine, Content, Hash, Intent, 
                     ConfidenceScore, Status, SemanticMessage, IsTest, RuntimeVerified, 
                     Documentation, StatementsCovered, StatementsTotal)
                    VALUES (@n, @fp, @sl, @el, @c, @h, @i, @cs, @st, @sm, @it, @rv, @doc, @sc, @stot)";
                cmd.Parameters.AddWithValue("@n", block.Name);
                cmd.Parameters.AddWithValue("@fp", block.FilePath ?? "");
                cmd.Parameters.AddWithValue("@sl", block.StartLine);
                cmd.Parameters.AddWithValue("@el", block.EndLine);
                cmd.Parameters.AddWithValue("@c", (object?)block.Content ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@h", (object?)block.Hash ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@i", (object?)block.Intent ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@cs", block.ConfidenceScore);
                cmd.Parameters.AddWithValue("@st", block.VerificationStatus ?? "VERIFIED");
                cmd.Parameters.AddWithValue("@sm", (object?)block.SemanticReviewMessage ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@it", block.IsTest ? 1 : 0);
                cmd.Parameters.AddWithValue("@rv", block.RuntimeVerified ? 1 : 0);
                cmd.Parameters.AddWithValue("@doc", (object?)block.Documentation ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@sc", block.StatementsCovered);
                cmd.Parameters.AddWithValue("@stot", block.StatementsTotal);
                cmd.ExecuteNonQuery();

                // Insert Concepts
                foreach (var concept in block.Concepts)
                {
                    using var cc = conn.CreateCommand();
                    cc.CommandText = "INSERT INTO Concepts (BlockName, ConceptName) VALUES (@bn, @cn)";
                    cc.Parameters.AddWithValue("@bn", block.Name);
                    cc.Parameters.AddWithValue("@cn", concept);
                    cc.ExecuteNonQuery();
                }

                // Insert References
                foreach (var refName in block.References)
                {
                    using var rc = conn.CreateCommand();
                    rc.CommandText = "INSERT INTO BlockReferences (BlockName, RefName) VALUES (@bn, @rn)";
                    rc.Parameters.AddWithValue("@bn", block.Name);
                    rc.Parameters.AddWithValue("@rn", refName);
                    rc.ExecuteNonQuery();
                }

                // Insert VerifyingTests
                foreach (var testId in block.VerifyingTests)
                {
                    using var tc = conn.CreateCommand();
                    tc.CommandText = "INSERT INTO VerifyingTests (BlockName, TestId) VALUES (@bn, @tid)";
                    tc.Parameters.AddWithValue("@bn", block.Name);
                    tc.Parameters.AddWithValue("@tid", testId);
                    tc.ExecuteNonQuery();
                }

                // Insert CoverageOverlap
                if (block.CoverageOverlap != null)
                {
                    foreach (var bdd in block.CoverageOverlap.BddTests)
                    {
                        using var oc = conn.CreateCommand();
                        oc.CommandText = "INSERT INTO CoverageOverlap (BlockName, TestId, TestType) VALUES (@bn, @tid, 'bdd')";
                        oc.Parameters.AddWithValue("@bn", block.Name);
                        oc.Parameters.AddWithValue("@tid", bdd);
                        oc.ExecuteNonQuery();
                    }
                    foreach (var unit in block.CoverageOverlap.UnitTests)
                    {
                        using var oc = conn.CreateCommand();
                        oc.CommandText = "INSERT INTO CoverageOverlap (BlockName, TestId, TestType) VALUES (@bn, @tid, 'unit')";
                        oc.Parameters.AddWithValue("@bn", block.Name);
                        oc.Parameters.AddWithValue("@tid", unit);
                        oc.ExecuteNonQuery();
                    }
                }

                // Insert FTS5 row
                var conceptNames = string.Join(", ", block.Concepts);
                using var fc = conn.CreateCommand();
                fc.CommandText = "INSERT INTO BlockSearch (Name, Content, Intent, ConceptNames) VALUES (@n, @c, @i, @cn)";
                fc.Parameters.AddWithValue("@n", block.Name);
                fc.Parameters.AddWithValue("@c", (object?)block.Content ?? DBNull.Value);
                fc.Parameters.AddWithValue("@i", (object?)block.Intent ?? DBNull.Value);
                fc.Parameters.AddWithValue("@cn", conceptNames);
                fc.ExecuteNonQuery();
            }

            // Insert MarkdownFiles
            foreach (var (path, mdFile) in data.MarkdownFiles)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"INSERT INTO MarkdownFiles 
                    (Path, Content, Concept, Description, Status, Audience, Type) 
                    VALUES (@p, @c, @con, @d, @s, @a, @t)";
                cmd.Parameters.AddWithValue("@p", path);
                cmd.Parameters.AddWithValue("@c", (object?)mdFile.Content ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@con", (object?)mdFile.Metadata?.Concept ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@d", (object?)mdFile.Metadata?.Description ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@s", (object?)mdFile.Metadata?.Status ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@a", (object?)mdFile.Metadata?.Audience ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@t", (object?)mdFile.Metadata?.Type ?? DBNull.Value);
                cmd.ExecuteNonQuery();

                // Insert BusinessRules
                if (mdFile.Metadata?.Rules != null)
                {
                    foreach (var rule in mdFile.Metadata.Rules)
                    {
                        using var rc = conn.CreateCommand();
                        rc.CommandText = "INSERT INTO BusinessRules (MdPath, RuleId, Description, Risk) VALUES (@mp, @ri, @d, @r)";
                        rc.Parameters.AddWithValue("@mp", path);
                        rc.Parameters.AddWithValue("@ri", rule.Id);
                        rc.Parameters.AddWithValue("@d", rule.Description);
                        rc.Parameters.AddWithValue("@r", (object?)rule.Risk ?? DBNull.Value);
                        rc.ExecuteNonQuery();
                    }
                }

                // Insert OwnerHistory
                if (mdFile.Metadata?.OwnerHistory != null)
                {
                    foreach (var owner in mdFile.Metadata.OwnerHistory)
                    {
                        using var oc = conn.CreateCommand();
                        oc.CommandText = "INSERT INTO OwnerHistory (MdPath, Name, FromDate) VALUES (@mp, @n, @f)";
                        oc.Parameters.AddWithValue("@mp", path);
                        oc.Parameters.AddWithValue("@n", owner.Name);
                        oc.Parameters.AddWithValue("@f", owner.From);
                        oc.ExecuteNonQuery();
                    }
                }
            }

            tx.Commit();
        }

        private void InsertMetadata(SqliteConnection conn, string key, string value)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Metadata (Key, Value) VALUES (@k, @v)";
            cmd.Parameters.AddWithValue("@k", key);
            cmd.Parameters.AddWithValue("@v", value);
            cmd.ExecuteNonQuery();
        }

        private void Compact(SqliteConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode = DELETE; VACUUM;";
            cmd.ExecuteNonQuery();
        }
    }
}
