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

        [ThreadStatic]
        private static string? _currentTestId;
        [ThreadStatic]
        private static string? _currentTraceId;

        private static readonly System.Diagnostics.ActivitySource _activitySource = new("Yab.Runtime");

        public static void SetCurrentTest(string testId) => _currentTestId = testId;
        public static void ClearCurrentTest() => _currentTestId = null;
        public static void SetTraceId(string traceId) => _currentTraceId = traceId;
        public static string? GetTraceId() => _currentTraceId;

        public static void Hit(string methodId)
        {
            var testId = _currentTestId ?? "__unknown__";
            var tests = _hits.GetOrAdd(methodId, _ => new ConcurrentDictionary<string, byte>());
            tests.TryAdd(testId, 0);

            if (_currentTraceId != null)
            {
                var traces = _traceHits.GetOrAdd(_currentTraceId, _ => new ConcurrentBag<TraceHit>());
                traces.Add(new TraceHit { MethodId = methodId, TestId = testId, Timestamp = DateTimeOffset.UtcNow });
            }

            using var activity = _activitySource.StartActivity(methodId);
            activity?.SetTag("yab.test_id", testId);
            activity?.SetTag("yab.trace_id", _currentTraceId);
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

        public static void Clear() { _hits.Clear(); _traceHits.Clear(); _currentTestId = null; _currentTraceId = null; }
    }

    public class HitsFile { public List<string> Hits { get; set; } = new(); public Dictionary<string, List<string>> Matrix { get; set; } = new(); }
    public class TraceHit { public string MethodId { get; set; } = ""; public string TestId { get; set; } = ""; public DateTimeOffset Timestamp { get; set; } }
}
