using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Yab.Cli.Services
{
    public class InstrumentationService
    {
        public void InjectReqnrollHooks(string targetPath)
        {
            var hooksCode = @"
using Reqnroll;
namespace Yab.Generated
{
    [Binding]
    public class YabReqnrollHooks
    {
        [BeforeScenario]
        public void BeforeScenario(ScenarioContext scenarioContext)
        {
            Yab.Runtime.YabTracker.SetCurrentTest(scenarioContext.ScenarioInfo.Title);
        }

        [AfterScenario]
        public void AfterScenario()
        {
            Yab.Runtime.YabTracker.ClearCurrentTest();
        }
    }
}";
            File.WriteAllText(Path.Combine(targetPath, "YabReqnrollHooks.cs"), hooksCode);
        }

        public void Instrument(string sourcePath, string targetPath)
        {
            var discovery = new FileDiscoveryService(sourcePath);
            var csFiles = discovery.EnumerateFiles(sourcePath, "*.cs");

            foreach (var file in csFiles)
            {
                var relativePath = Path.GetRelativePath(sourcePath, file);
                var targetFile = Path.Combine(targetPath, relativePath);
                var targetDir = Path.GetDirectoryName(targetFile);
                
                if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir!);

                var code = File.ReadAllText(file);
                var tree = CSharpSyntaxTree.ParseText(code);
                var root = tree.GetRoot();

                var rewriter = new YabRewriter();
                var newRoot = rewriter.Visit(root);

                if (newRoot == root) Console.WriteLine($"[YAB] WARNING: No changes made to {relativePath}");

                File.WriteAllText(targetFile, newRoot.ToFullString());
            }

            // Inject Bootstrapper
            var bootstrapperCode = @"
using System;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace Yab.Runtime {
    public static class YabBootstrapper {
        [ModuleInitializer]
        public static void Init() {
            Action save = () => {
                var path = Environment.GetEnvironmentVariable(""YAB_HITS_PATH"") ?? ""yab-hits.json"";
                YabTracker.Save(path);
            };
            AppDomain.CurrentDomain.ProcessExit += (s, e) => save();
            AssemblyLoadContext.Default.Unloading += (ctx) => save();
        }
    }
}";
            File.WriteAllText(Path.Combine(targetPath, "YabBootstrapper.cs"), bootstrapperCode);
        }

        private class YabRewriter : CSharpSyntaxRewriter
        {
            public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
            {
                var methodName = node.Identifier.Text;
                if (node.Body == null) {
                    return base.VisitMethodDeclaration(node);
                }
                if (node.ExplicitInterfaceSpecifier != null) {
                    return base.VisitMethodDeclaration(node);
                }

                var className = GetParentClassName(node);
                var methodId = $"{className}.{methodName}";

                // Check if this is a test method or a BDD step
                var testAttributes = new[] { "Fact", "Theory", "Test", "Given", "When", "Then", "StepDefinition", "Scenario" };
                
                var attributes = node.AttributeLists.SelectMany(al => al.Attributes).ToList();
                var isTest = attributes.Any(a => {
                        var name = a.Name.ToString();
                        return testAttributes.Any(attr => name.IndexOf(attr, StringComparison.OrdinalIgnoreCase) >= 0);
                    });

                if (isTest)
                {
                    var setTest = SyntaxFactory.ParseStatement($"Yab.Runtime.YabTracker.SetCurrentTest(\"{methodId}\");\r\n");
                    var clearTest = SyntaxFactory.ParseStatement($"Yab.Runtime.YabTracker.ClearCurrentTest();\r\n");
                    
                    var tryBlock = SyntaxFactory.Block(node.Body.Statements);
                    var finallyClause = SyntaxFactory.FinallyClause(SyntaxFactory.Block(clearTest));
                    var tryFinally = SyntaxFactory.TryStatement(tryBlock, SyntaxFactory.List<CatchClauseSyntax>(), finallyClause);

                    var newBody = SyntaxFactory.Block(setTest, tryFinally);
                    return node.WithBody(newBody);
                }
                else
                {
                    // Just inject method-level hit
                    var methodHit = SyntaxFactory.ParseStatement($"Yab.Runtime.YabTracker.Hit(\"{methodId}\");\r\n");
                    var newStatements = node.Body.Statements.Insert(0, methodHit);
                    var newBody = node.Body.WithStatements(newStatements);
                    
                    return node.WithBody(newBody);
                }
            }


            public override SyntaxNode? VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
            {
                if (node.Body == null) return base.VisitConstructorDeclaration(node);

                var className = GetParentClassName(node);
                var methodName = className; // Constructor name is class name
                var methodId = $"{className}.{methodName}";

                var hitStatement = SyntaxFactory.ParseStatement($"Yab.Runtime.YabTracker.Hit(\"{methodId}\");\r\n");
                var newBody = node.Body.WithStatements(node.Body.Statements.Insert(0, hitStatement));

                return node.WithBody(newBody);
            }

            private string GetParentClassName(SyntaxNode node)
            {
                var parent = node.Parent;
                while (parent != null && !(parent is ClassDeclarationSyntax))
                {
                    parent = parent.Parent;
                }

                if (parent is ClassDeclarationSyntax cds)
                {
                    return cds.Identifier.Text;
                }

                return "Global";
            }
        }
    }
}
