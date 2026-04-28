using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Spectre.Console;
using Yab.Cli.Models;

namespace Yab.Cli.Services.Steps
{
    public class RuntimeVerificationStep : IPipelineStep
    {
        public string Name => "Runtime Verification";
        public int Order => 35; // After Inference, before Portal Generation

        public async Task ExecuteAsync(PipelineContext context)
        {
            if (!context.Runtime) return;

            async Task RunVerification(StatusContext? ctx)
            {
                var tempPath = Path.Combine(context.RootPath, ".yab", "instrumented");
                if (Directory.Exists(tempPath)) Directory.Delete(tempPath, true);
                Directory.CreateDirectory(tempPath);

                try
                {
                    void SetStatus(string status)
                    {
                        if (ctx != null) ctx.Status(status);
                        else context.Log($"[grey]Runtime:[/] {status}");
                    }

                    SetStatus("Discovering related projects...");
                    var projectsToInstrument = DiscoverRelatedProjects(context.RootPath);
                    var slnRoot = GetSolutionRoot(context.RootPath);
                    
                    // Copy solution files ONLY if the project is part of them
                    var slnFiles = Directory.GetFiles(slnRoot, "*.sln").Concat(Directory.GetFiles(slnRoot, "*.slnx")).ToList();
                    string? slnToUse = null;
                    foreach (var sln in slnFiles)
                    {
                        var slnContent = File.ReadAllText(sln);
                        var projRelPath = Path.GetRelativePath(slnRoot, Directory.GetFiles(context.RootPath, "*.csproj").First());
                        
                        // Inject project if missing from SLN (to ensure it builds in shadow)
                        if (!slnContent.Contains(Path.GetFileName(projRelPath)))
                        {
                            if (sln.EndsWith(".slnx"))
                            {
                                slnContent = slnContent.Replace("</Solution>", $"  <Project Path=\"{projRelPath}\" />\r\n</Solution>");
                            }
                        }

                        slnToUse = Path.Combine(tempPath, Path.GetFileName(sln));
                        File.WriteAllText(slnToUse, slnContent);
                    }

                    SetStatus("Instrumenting code...");
                    var instrumenter = new InstrumentationService();
                    foreach (var projectPath in projectsToInstrument)
                    {
                        var targetProjDir = Path.Combine(tempPath, Path.GetRelativePath(slnRoot, projectPath));
                        CopyProjectFiles(projectPath, targetProjDir, context);
                        instrumenter.Instrument(projectPath, targetProjDir);
                        AddRuntimeReference(targetProjDir, projectPath, context);
                    }

                    SetStatus("Executing instrumented tests...");
                    // Run tests on the SLN if part of one, otherwise find the target project file
                    var testTarget = slnToUse ?? Directory.GetFiles(Path.Combine(tempPath, Path.GetRelativePath(slnRoot, context.RootPath)), "*.csproj").First();
                    await RunTestsAsync(tempPath, testTarget, context);

                    SetStatus("Collecting execution hits...");
                    var hitsPath = Path.Combine(tempPath, "yab-hits.json");
                    if (File.Exists(hitsPath))
                    {
                        var hitsJson = File.ReadAllText(hitsPath);
                        List<string> hits;
                        Dictionary<string, List<string>>? matrix = null;
                        
                        try
                        {
                            // Try new format first
                            var hitsFile = JsonSerializer.Deserialize<Yab.Runtime.HitsFile>(hitsJson);
                            hits = hitsFile?.Hits ?? new List<string>();
                            matrix = hitsFile?.Matrix;
                        }
                        catch
                        {
                            // Fallback to old flat list format
                            hits = JsonSerializer.Deserialize<List<string>>(hitsJson) ?? new List<string>();
                        }
                        
                        var count = 0;
                        foreach (var block in context.Data.Blocks)
                        {
                            if (hits.Contains(block.Name))
                            {
                                block.RuntimeVerified = true;
                                count++;
                                context.Log($"DEBUG: Matched hit for {block.Name}");
                            }
                            else
                            {
                                // context.Log($"DEBUG: No hit for {block.Name}");
                            }
                            
                            // Store which tests exercise this block (for coverage overlap)
                            if (matrix != null && matrix.TryGetValue(block.Name, out var testList))
                            {
                                block.VerifyingTests = testList;
                                context.Log($"DEBUG: Matched matrix for {block.Name} with {testList.Count} tests");
                            }

                            // Count statement-level coverage
                            if (matrix != null)
                            {
                                var stmtHits = matrix.Keys.Where(k => k.StartsWith(block.Name + "#")).ToList();
                                block.StatementsCovered = stmtHits.Count;
                                // Total statements = count of unique statement indices
                                block.StatementsTotal = stmtHits.Count; // Approximate; exact requires parsing
                            }
                        }

                        // Re-calculate overlap now that we have runtime data
                        var collector = new DocumentationDataCollector(new CodeAttributeScanner());
                        collector.BuildCoverageOverlap(context.Data);

                        context.Log($"[bold green]Runtime Verification Complete:[/] {count} execution points verified.");
                    }
                    else
                    {
                        context.Log("[yellow]Runtime Verification Warning: No execution hits recorded. Ensure tests were executed.[/]");
                    }
                }
                catch (Exception ex)
                {
                    context.Log($"[red]Runtime Verification Failed: {ex.Message}[/]");
                }
                finally
                {
                    if (Directory.Exists(tempPath))
                    {
                        // Directory.Delete(tempPath, true); // Keep for debugging if needed
                    }
                }
            }

            if (context.IsTui)
            {
                await RunVerification(null);
            }
            else
            {
                await AnsiConsole.Status()
                    .StartAsync("Performing Runtime Execution Tracking...", RunVerification);
            }
        }

        private List<string> DiscoverRelatedProjects(string rootPath)
        {
            var projects = new List<string> { rootPath };
            var slnRoot = GetSolutionRoot(rootPath);
            
            // If we are in a solution, include ALL projects in that solution
            // to ensure the shadow build is complete and buildable.
            var discovery = new FileDiscoveryService(slnRoot);
            var allProjs = discovery.EnumerateFiles(slnRoot, "*.csproj");
            
            foreach (var proj in allProjs)
            {
                projects.Add(Path.GetDirectoryName(proj)!);
            }
            
            return projects.Distinct().ToList();
        }

        private string GetSolutionRoot(string rootPath)
        {
            var current = rootPath;
            while (current != null)
            {
                var slnFiles = Directory.GetFiles(current, "*.sln").Concat(Directory.GetFiles(current, "*.slnx")).ToList();
                foreach (var sln in slnFiles)
                {
                    // Verify if our target project is actually part of this solution
                    var content = File.ReadAllText(sln);
                    var projName = Path.GetFileNameWithoutExtension(rootPath);
                    if (content.Contains(projName + ".csproj"))
                    {
                        return current;
                    }
                }

                // Don't climb above the workspace root if possible
                if (current == rootPath && Directory.Exists(Path.Combine(current, ".git"))) break;

                current = Path.GetDirectoryName(current);
            }
            return rootPath;
        }

        private void CopyProjectFiles(string source, string target, PipelineContext context)
        {
            var discovery = new FileDiscoveryService(source);
            // Copy everything except what's already ignored or in .yab
            var files = discovery.EnumerateFiles(source, "*.*");
            foreach (var file in files)
            {
                var relative = Path.GetRelativePath(source, file);
                var dest = Path.Combine(target, relative);
                var dir = Path.GetDirectoryName(dest);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);
                File.Copy(file, dest, true);
            }
        }

        private void AddRuntimeReference(string target, string originalRoot, PipelineContext context)
        {
            var discovery = new FileDiscoveryService(target);
            var projFiles = discovery.EnumerateFiles(target, "*.csproj");
            
            // Find Yab.Runtime.csproj relative to the current assembly
            var assemblyPath = typeof(RuntimeVerificationStep).Assembly.Location;
            var current = Path.GetDirectoryName(assemblyPath);
            while (current != null && !File.Exists(Path.Combine(current, "Yab.slnx")))
            {
                current = Path.GetDirectoryName(current);
            }

            if (current == null) throw new Exception("Could not find YAB solution root.");

            var runtimePath = Path.GetFullPath(Path.Combine(current, "Yab.Runtime/Yab.Runtime.csproj"));
            
            context.Log($"[grey]DEBUG: Using Yab.Runtime path: {runtimePath}[/]");

            foreach (var file in projFiles)
            {
                var content = File.ReadAllText(file);
                
                // Fix relative references to point to instrumented versions if they exist
                var originalDir = Path.GetDirectoryName(Path.Combine(originalRoot, Path.GetRelativePath(target, file)))!;
                var matches = System.Text.RegularExpressions.Regex.Matches(content, "<ProjectReference Include=\"(.*?)\" />");
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    var relativePath = match.Groups[1].Value;
                    if (!Path.IsPathRooted(relativePath))
                    {
                        var absoluteOriginalPath = Path.GetFullPath(Path.Combine(originalDir, relativePath));
                        var slnRoot = GetSolutionRoot(originalRoot);
                        
                        if (absoluteOriginalPath.StartsWith(slnRoot))
                        {
                            // It's part of the solution, point to the instrumented version in tempPath
                            var relToSln = Path.GetRelativePath(slnRoot, absoluteOriginalPath);
                            var instrumentedPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(target)!, relToSln));
                            
                            if (File.Exists(instrumentedPath))
                            {
                                content = content.Replace(match.Value, $"<ProjectReference Include=\"{instrumentedPath}\" />");
                                continue;
                            }
                        }

                        // Fallback to absolute original path
                        content = content.Replace(match.Value, $"<ProjectReference Include=\"{absoluteOriginalPath}\" />");
                    }
                }

                if (!content.Contains("Yab.Runtime.csproj"))
                {
                    var reference = $"\r\n  <ItemGroup>\r\n    <ProjectReference Include=\"{runtimePath}\" />\r\n  </ItemGroup>\r\n</Project>";
                    content = content.Replace("</Project>", reference);
                }
                File.WriteAllText(file, content);
            }
        }

        private async Task RunTestsAsync(string workingDir, string targetPath, PipelineContext context)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            async Task RunCommand(string args) {
                startInfo.Arguments = $"{args} \"{targetPath}\"";
                startInfo.EnvironmentVariables["YAB_HITS_PATH"] = Path.Combine(workingDir, "yab-hits.json");
                using var proc = Process.Start(startInfo);
                if (proc == null) return;

                var output = await proc.StandardOutput.ReadToEndAsync();
                var error = await proc.StandardError.ReadToEndAsync();
                await proc.WaitForExitAsync(context.CancellationToken);

                if (context.Verbose || proc.ExitCode != 0) {
                    context.Log($"[grey]DEBUG:[/] dotnet {args} \"{targetPath}\" (Exit Code: {proc.ExitCode})");
                    if (!string.IsNullOrEmpty(output)) context.Log(Markup.Escape(output));
                    if (!string.IsNullOrEmpty(error)) context.Log($"[red]{Markup.Escape(error)}[/]");
                }
            }

            await RunCommand("build");
            await RunCommand("test");
        }
    }
}
