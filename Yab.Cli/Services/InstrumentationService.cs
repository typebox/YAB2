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
                if (node.Body == null) return base.VisitMethodDeclaration(node);
                if (node.ExplicitInterfaceSpecifier != null) return base.VisitMethodDeclaration(node);

                var className = GetParentClassName(node);
                var methodName = node.Identifier.Text;
                var methodId = $"{className}.{methodName}";

                // Check if this is a test method or a BDD step
                var testAttributes = new[] { "Fact", "Theory", "Test", "Given", "When", "Then", "StepDefinition", "Scenario" };
                var isTest = node.AttributeLists
                    .SelectMany(al => al.Attributes)
                    .Any(a => {
                        var name = a.Name.ToString();
                        return testAttributes.Any(attr => name.Contains(attr, StringComparison.OrdinalIgnoreCase));
                    });

                if (isTest)
                {
                    // Wrap body: SetCurrentTest → original body → ClearCurrentTest
                    var setTest = SyntaxFactory.ParseStatement($"Yab.Runtime.YabTracker.SetCurrentTest(\"{methodId}\");\r\n");
                    var clearTest = SyntaxFactory.ParseStatement($"Yab.Runtime.YabTracker.ClearCurrentTest();\r\n");
                    var newStatements = node.Body.Statements.Insert(0, setTest).Add(clearTest);
                    var newBody = node.Body.WithStatements(newStatements);
                    return node.WithBody(newBody);
                }
                else
                {
                    int stmtIndex = 0;
                    var newBody = InstrumentBlock(node.Body, methodId, ref stmtIndex);
                    
                    // Also add method-level hit for backward compat at the very beginning
                    var methodHit = SyntaxFactory.ParseStatement($"Yab.Runtime.YabTracker.Hit(\"{methodId}\");\r\n");
                    newBody = newBody.WithStatements(newBody.Statements.Insert(0, methodHit));
                    
                    return node.WithBody(newBody);
                }
            }

            private BlockSyntax InstrumentBlock(BlockSyntax block, string methodId, ref int stmtIndex)
            {
                var newStatements = new List<StatementSyntax>();
                foreach (var stmt in block.Statements)
                {
                    var hitStmt = SyntaxFactory.ParseStatement(
                        $"Yab.Runtime.YabTracker.Hit(\"{methodId}#{stmtIndex++}\");\r\n");
                    newStatements.Add(hitStmt);

                    if (stmt is BlockSyntax subBlock)
                    {
                        newStatements.Add(InstrumentBlock(subBlock, methodId, ref stmtIndex));
                    }
                    else if (stmt is IfStatementSyntax ifStmt)
                    {
                        var newIf = ifStmt;
                        if (ifStmt.Statement is BlockSyntax ifBlock)
                            newIf = newIf.WithStatement(InstrumentBlock(ifBlock, methodId, ref stmtIndex));
                        else if (ifStmt.Statement is StatementSyntax singleStmt)
                            newIf = newIf.WithStatement(InstrumentBlock(SyntaxFactory.Block(singleStmt), methodId, ref stmtIndex));

                        if (ifStmt.Else != null)
                        {
                            if (ifStmt.Else.Statement is BlockSyntax elseBlock)
                                newIf = newIf.WithElse(ifStmt.Else.WithStatement(InstrumentBlock(elseBlock, methodId, ref stmtIndex)));
                            else if (ifStmt.Else.Statement is StatementSyntax singleElse)
                                newIf = newIf.WithElse(ifStmt.Else.WithStatement(InstrumentBlock(SyntaxFactory.Block(singleElse), methodId, ref stmtIndex)));
                        }
                        newStatements.Add(newIf);
                    }
                    else
                    {
                        newStatements.Add(stmt);
                    }
                }
                return SyntaxFactory.Block(newStatements);
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
