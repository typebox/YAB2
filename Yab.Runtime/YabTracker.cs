using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Yab.Runtime
{
    public static class YabTracker
    {
        private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _hits = new();
        private static readonly ConcurrentDictionary<string, ConcurrentBag<TraceHit>> _traceHits = new();

        private static readonly System.Diagnostics.ActivitySource _activitySource = new("Yab.Runtime");

        public static void SetCurrentTest(string testId) => YabContext.TestIdStack = YabContext.TestIdStack.Push(testId);
        public static void ClearCurrentTest()
        {
            if (!YabContext.TestIdStack.IsEmpty)
                YabContext.TestIdStack = YabContext.TestIdStack.Pop();
        }
        public static void SetTraceId(string traceId) => YabContext.CurrentTraceId = traceId;
        public static string? GetTraceId() => YabContext.CurrentTraceId;

        public static void Hit(string methodId)
        {
            var testIds = YabContext.AllCurrentTestIds.ToList();
            if (testIds.Count == 0) testIds.Add("__unknown__");

            foreach (var testId in testIds)
            {
                var tests = _hits.GetOrAdd(methodId, _ => new ConcurrentDictionary<string, byte>());
                tests.TryAdd(testId, 0);
            }

            var traceId = YabContext.CurrentTraceId;
            if (traceId != null)
            {
                var traces = _traceHits.GetOrAdd(traceId, _ => new ConcurrentBag<TraceHit>());
                // Use the top-most test ID for the trace hit (most specific)
                var topTestId = YabContext.CurrentTestId ?? "__unknown__";
                traces.Add(new TraceHit { MethodId = methodId, TestId = topTestId, Timestamp = DateTimeOffset.UtcNow });
            }

            using var activity = _activitySource.StartActivity(methodId);
            activity?.SetTag("yab.test_id", YabContext.CurrentTestId ?? "__unknown__");
            activity?.SetTag("yab.trace_id", traceId);
        }

        private static readonly object _saveLock = new object();

        public static void Save(string path)
        {
            lock (_saveLock)
            {
                try
                {
                    var directory = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                        Directory.CreateDirectory(directory);

                    var matrix = new Dictionary<string, List<string>>();
                    foreach (var kvp in _hits) matrix[kvp.Key] = kvp.Value.Keys.ToList();
                    var flat = _hits.Keys.ToList();

                    if (File.Exists(path))
                    {
                        try
                        {
                            var existing = JsonSerializer.Deserialize<HitsFile>(File.ReadAllText(path));
                            if (existing?.Hits != null) foreach (var h in existing.Hits) if (!flat.Contains(h)) flat.Add(h);
                            if (existing?.Matrix != null)
                                foreach (var kvp in existing.Matrix)
                                {
                                    if (!matrix.ContainsKey(kvp.Key)) matrix[kvp.Key] = new List<string>();
                                    foreach (var t in kvp.Value) if (!matrix[kvp.Key].Contains(t)) matrix[kvp.Key].Add(t);
                                }
                        }
                        catch { }
                    }

                    var file = new HitsFile { Hits = flat, Matrix = matrix };
                    File.WriteAllText(path, JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true }));

                    if (_traceHits.Count > 0)
                    {
                        var tracePath = Path.ChangeExtension(path, ".traces.json");
                        var traceData = _traceHits.ToDictionary(k => k.Key, v => v.Value.ToList());
                        File.WriteAllText(tracePath, JsonSerializer.Serialize(traceData, new JsonSerializerOptions { WriteIndented = true }));
                    }
                }
                catch (Exception ex) { Console.WriteLine($"[YAB] Failed to save execution hits: {ex.Message}"); }
            }
        }

        public static void Clear() { _hits.Clear(); _traceHits.Clear(); YabContext.Clear(); }
    }

    public class HitsFile { public List<string> Hits { get; set; } = new(); public Dictionary<string, List<string>> Matrix { get; set; } = new(); }
    public class TraceHit { public string MethodId { get; set; } = ""; public string TestId { get; set; } = ""; public DateTimeOffset Timestamp { get; set; } }
}
