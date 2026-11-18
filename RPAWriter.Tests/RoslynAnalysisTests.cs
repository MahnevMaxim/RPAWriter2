using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;
using RPAWriter.Roslyn;
using Agent.Rpa.AgentTools;
using Agent.Rpa.Contracts;
using Agent.Rpa.Models;
using Agent.Rpa.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class RoslynAnalysisTests
    {
        private static Project CreateProject(AdhocWorkspace workspace)
        {
            var project = workspace.AddProject("TestProject", LanguageNames.CSharp);
            project = project.WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            return project.AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
        }

        private static Document AddDocument(AdhocWorkspace workspace, Project project, string name, string code)
        {
            return workspace.AddDocument(project.Id, name, SourceText.From(code));
        }

        [Test]
        public async Task BuildOutline_ReturnsMembersWithoutBodies()
        {
            var workspace = new AdhocWorkspace();
            var project = CreateProject(workspace);
            var document = AddDocument(workspace, project, "OrderService.cs", @"
namespace RPA.Crm
{
    public class OrderService
    {
        private int _id;

        public OrderService(int id)
        {
            _id = id;
        }

        public int GetId(int x)
        {
            return _id + x;
        }

        public string Name { get; set; }
    }
}
");

            var outline = await RoslynAnalysis.BuildOutlineAsync(document, CancellationToken.None);
            var text = RoslynOutputFormatter.FormatOutline(outline, "OrderService.cs");

            Assert.That(text, Does.Contain("namespace RPA.Crm"));
            Assert.That(text, Does.Contain("class OrderService"));
            Assert.That(text, Does.Contain("GetId"));
            Assert.That(text, Does.Contain("Name"));
            Assert.That(text, Does.Not.Contain("_id = id"), "method bodies must be stripped from the outline");
            Assert.That(text, Does.Not.Contain("return _id + x"));
        }

        [Test]
        public async Task GetDiagnostics_ReturnsCompilerErrors()
        {
            var workspace = new AdhocWorkspace();
            var project = CreateProject(workspace);
            var document = AddDocument(workspace, project, "Broken.cs", "class C { void M() { int x = \"oops\"; } }");

            var diagnostics = await RoslynAnalysis.GetDiagnosticsAsync(document, CancellationToken.None);

            Assert.That(diagnostics.Any(d => d.Severity == "Error"), Is.True);
        }

        [Test]
        public async Task FindSymbolUsages_ReturnsCompilerVerifiedReferences()
        {
            var workspace = new AdhocWorkspace();
            var project = CreateProject(workspace);
            workspace.AddDocument(project.Id, "A.cs", SourceText.From("public class A { public void Go() { } }"));
            workspace.AddDocument(project.Id, "B.cs", SourceText.From("public class B { void Run(A a) { a.Go(); } }"));

            var solution = workspace.CurrentSolution;
            var scope = solution.Projects.First().Documents.First(d => d.Name == "A.cs");

            var result = await RoslynAnalysis.FindSymbolUsagesAsync(solution, "Go", scope, 100, CancellationToken.None);

            Assert.That(result.Usages.Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(result.Usages.Any(u => (u.FilePath ?? string.Empty).EndsWith("B.cs")), Is.True);
        }

        [Test]
        public async Task GetInheritance_ReturnsBaseAndInterfaces()
        {
            var workspace = new AdhocWorkspace();
            var project = CreateProject(workspace);
            var document = AddDocument(workspace, project, "Types.cs", @"
public interface IThing { }
public class Base { }
public class Derived : Base, IThing { }
");

            var info = await RoslynAnalysis.GetInheritanceAsync(workspace.CurrentSolution, "Derived", document, CancellationToken.None);

            Assert.That(info.BaseTypes, Does.Contain("Base"));
            Assert.That(info.Interfaces, Does.Contain("IThing"));
        }

        [Test]
        public void FormatUsages_RendersCompactText()
        {
            var result = new SymbolUsageResult
            {
                SymbolDisplay = "A.Go",
                Usages = new[]
                {
                    new SymbolUsage { FilePath = "B.cs", Line = 1, Column = 30, CodeLine = "a.Go();" }
                },
                Truncated = false,
                MaxResults = 100
            };

            var text = RoslynOutputFormatter.FormatUsages(result);

            Assert.That(text, Does.Contain("Symbol: A.Go"));
            Assert.That(text, Does.Contain("B.cs:1:30"));
            Assert.That(text, Does.Contain("a.Go();"));
        }
    }
}
