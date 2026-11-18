using NUnit.Framework;
using Agent.Models;
using Agent.Mvvm;
using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using Agent.Rpa.AgentTools;
using Agent.Rpa.Contracts;
using Agent.Rpa.Models;
using Agent.Rpa.Services;

namespace RPAWriter.Tests
{
    /// <summary>
    /// Guards the boundary described in documentation/13-model-layer.md:
    /// <list type="bullet">
    /// <item><c>Agent.Models</c> and <c>Agent.Mvvm</c> must be <c>netstandard2.0</c>.</item>
    /// <item>They must not reference WPF, WinForms, Material.Icons, SQLite, EnvDTE or the VSIX assembly.</item>
    /// <item>No public model type may live in a <c>System.Windows.*</c> namespace.</item>
    /// </list>
    /// A violation fails the build instead of being caught at review time.
    /// </summary>
    [TestFixture]
    public class ModelArchitectureTests
    {
        private static readonly string[] ForbiddenAssemblies =
        {
            "PresentationFramework",
            "PresentationCore",
            "WindowsBase",
            "System.Xaml",
            "System.Windows.Forms",
            "System.Drawing",
            "Material.Icons",
            "Material.Icons.WPF",
            "System.Data.SQLite",
            "EnvDTE",
            "EnvDTE80",
            "EnvDTE90",
            "Microsoft.VisualStudio.Shell.15.0",
            "Microsoft.VisualStudio.Shell.Immutable.15.0",
            "Newtonsoft.Json.Schema"
        };

        private static Assembly ModelsAssembly => typeof(ModelsAssemblyMarker).Assembly;

        private static Assembly MvvmAssembly => typeof(BaseModel).Assembly;

        /// <summary>The Core assembly hosts the UI-free domain types the models are built on.</summary>
        private static Assembly CoreAssembly => typeof(Agent.Core.Models.ChatMessage).Assembly;

        /// <summary>The RPA layer hosts the RPA-domain models and services (robot generation, spec analysis, log analysis).</summary>
        private static Assembly RpaAssembly => typeof(Agent.Rpa.Models.SpecTabModel).Assembly;

        [Test]
        public void ModelsAssembly_TargetsNetStandard()
        {
            AssertNetStandard(ModelsAssembly);
        }

        [Test]
        public void MvvmAssembly_TargetsNetStandard()
        {
            AssertNetStandard(MvvmAssembly);
        }

        [Test]
        public void ModelsAssembly_HasNoUiDependencies()
        {
            AssertNoForbiddenReferences(ModelsAssembly);
        }

        [Test]
        public void MvvmAssembly_HasNoUiDependencies()
        {
            AssertNoForbiddenReferences(MvvmAssembly);
        }

        [Test]
        public void ModelsAssembly_DoesNotReferenceTheVsix()
        {
            var references = ModelsAssembly.GetReferencedAssemblies().Select(r => r.Name).ToArray();

            Assert.That(references, Does.Not.Contain("RPAWriter2"),
                "Agent.Models must never depend on the VSIX assembly: that would break headless tests.");
        }

        [Test]
        public void ModelsAssembly_ExposesNoSystemWindowsTypes()
        {
            var violations = ModelsAssembly.GetExportedTypes()
                .Where(t => t.Namespace != null && t.Namespace.StartsWith("System.Windows", StringComparison.Ordinal))
                .Select(t => t.FullName)
                .ToArray();

            Assert.That(violations, Is.Empty,
                "Models must not declare types in System.Windows.*: " + string.Join(", ", violations));
        }

        [Test]
        public void ModelsAssembly_ReferencesTheMvvmFramework()
        {
            var references = ModelsAssembly.GetReferencedAssemblies().Select(r => r.Name).ToArray();

            Assert.That(references, Does.Contain("Agent.Mvvm"));
        }

        [Test]
        public void CoreAssembly_TargetsNetStandard()
        {
            AssertNetStandard(CoreAssembly);
        }

        [Test]
        public void CoreAssembly_HasNoUiDependencies()
        {
            AssertNoForbiddenReferences(CoreAssembly);
        }

        [Test]
        public void CoreAssembly_DoesNotReferenceTheVsix()
        {
            var references = CoreAssembly.GetReferencedAssemblies().Select(r => r.Name).ToArray();

            Assert.That(references, Does.Not.Contain("RPAWriter2"),
                "Agent.Core must stay usable from headless tests; the VSIX assembly may only depend on it.");
        }

        /// <summary>
        /// The chat domain lives in Core so that models can use it without referencing WPF.
        /// This test keeps it there: moving it back to the VSIX project would make it untestable.
        /// </summary>
        [Test]
        public void ChatDomainTypes_LiveInCore()
        {
            var expected = new[]
            {
                typeof(Agent.Core.Models.ChatMessage),
                typeof(Agent.Core.Models.ChatPayload),
                typeof(Agent.Core.Models.TextPayload),
                typeof(Agent.Core.Models.ToolCallPayload),
                typeof(Agent.Core.Models.QuestionPayload),
                typeof(Agent.Core.Models.SubagentPayload),
                typeof(Agent.Core.Models.CheckpointPayload),
                typeof(Agent.Core.Models.ChatAttachment),
                typeof(Agent.Core.Models.ChatConversation),
                typeof(Agent.Core.Models.ChatHistoryMessage),
                typeof(Agent.Core.Models.PendingMessage),
                typeof(Agent.Core.Models.PendingDelivery),
                typeof(Agent.Core.Models.UiStateData),
                typeof(Agent.Core.Models.AgentPhase),
                typeof(Agent.Core.Services.ChatMessageMapper),
                typeof(Agent.Core.Models.ChatViewMode),
                typeof(Agent.Core.Contracts.IChatHistoryStore),
                typeof(Agent.Core.Contracts.IUiStateStore)
            };

            var wrongAssembly = expected
                .Where(t => t.Assembly != CoreAssembly)
                .Select(t => t.FullName)
                .ToArray();

            Assert.That(wrongAssembly, Is.Empty,
                "Chat domain types must live in Agent.Core: " + string.Join(", ", wrongAssembly));
        }

        /// <summary>
        /// Material.Icons is a view concern: the queue delivery icon is resolved by a converter in
        /// the VSIX project, so <see cref="Agent.Core.Models.PendingMessage"/> exposes no icon.
        /// </summary>
        [Test]
        public void PendingMessage_ExposesNoIconProperty()
        {
            var iconProperties = typeof(Agent.Core.Models.PendingMessage)
                .GetProperties()
                .Where(p => p.Name.IndexOf("Icon", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(p => p.Name)
                .ToArray();

            Assert.That(iconProperties, Is.Empty,
                "Icon selection belongs to the view layer: " + string.Join(", ", iconProperties));
        }

        /// <summary>
        /// The chat model is the reference implementation of the model rules: it lives in
        /// Agent.Models, so it can be constructed and exercised from this net8.0 test project
        /// without Visual Studio.
        /// </summary>
        [Test]
        public void ChatModel_LivesInModelsAssembly()
        {
            Assert.That(typeof(ChatModel).Assembly, Is.EqualTo(ModelsAssembly));
        }

        /// <summary>
        /// Layout state (visibility, focus, geometry) belongs to the view: a model exposing it can no
        /// longer be exercised headless and starts depending on the UI. Rule 10 of
        /// documentation/13-model-layer.md.
        /// </summary>
        [Test]
        public void ChatModel_ExposesNoLayoutState()
        {
            var violations = typeof(ChatModel).GetProperties()
                .Where(p => p.Name.IndexOf("Visib", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(p => p.Name)
                .ToArray();

            Assert.That(violations, Is.Empty,
                "Layout state must stay in the view: " + string.Join(", ", violations));
        }

        /// <summary>
        /// The log analysis model is the second reference implementation of the rules: it is
        /// constructed and exercised from this net8.0 project without a view model or a view.
        /// </summary>
        [Test]
        public void LogAnalyzer_LivesInRpaAssembly()
        {
            Assert.That(typeof(Agent.Rpa.Models.LogAnalyzer).Assembly, Is.EqualTo(RpaAssembly));
        }

        /// <summary>Layout state (panel visibility, focus) is view state: rule 10.</summary>
        [Test]
        public void LogAnalyzer_ExposesNoLayoutState()
        {
            var violations = typeof(Agent.Rpa.Models.LogAnalyzer).GetProperties()
                .Where(p => p.Name.IndexOf("Visib", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(p => p.Name)
                .ToArray();

            Assert.That(violations, Is.Empty,
                "Layout state must stay in the view: " + string.Join(", ", violations));
        }

        /// <summary>
        /// The log analysis services and contracts live in Core, next to the chat domain, so the
        /// model stays testable and the VSIX project only supplies the host adapters.
        /// </summary>
        [Test]
        public void LogAnalysisTypes_LiveInRpa()
        {
            var expected = new[]
            {
                typeof(Agent.Rpa.Contracts.ILogProviderSource),
                typeof(Agent.Rpa.Contracts.IOutputWindowTextSource),
                typeof(Agent.Rpa.Contracts.ILogAnalysisSettings),
                typeof(Agent.Rpa.Models.LogLevelOption),
                typeof(Agent.Rpa.Services.ProdLogAnalysisService),
                typeof(Agent.Rpa.Services.ProdLogQueryBuilder),
                typeof(Agent.Rpa.Services.RobotNameHistory)
            };

            var wrongAssembly = expected
                .Where(t => t.Assembly != RpaAssembly)
                .Select(t => t.FullName)
                .ToArray();

            Assert.That(wrongAssembly, Is.Empty,
                "Log analysis types must live in Agent.Rpa: " + string.Join(", ", wrongAssembly));
        }

        /// <summary>Workspace identity and scoping contracts are host-agnostic domain types in Core.</summary>
        [Test]
        public void WorkspaceTypes_LiveInCore()
        {
            var expected = new[]
            {
                typeof(Agent.Core.Services.WorkspaceIdentity),
                typeof(Agent.Core.Services.WorkspaceInfo),
                typeof(Agent.Core.Services.WorkspaceLogSettings),
                typeof(Agent.Core.Contracts.IWorkspaceProvider),
                typeof(Agent.Core.Contracts.ISecretsStore),
                typeof(Agent.Core.Contracts.IWorkspaceLogSettingsStore)
            };

            var wrongAssembly = expected
                .Where(t => t.Assembly != CoreAssembly)
                .Select(t => t.FullName)
                .ToArray();

            Assert.That(wrongAssembly, Is.Empty,
                "Workspace types must live in Agent.Core: " + string.Join(", ", wrongAssembly));
        }

        /// <summary>The Spec tab model lives in the models assembly, next to <see cref="ChatModel"/>.</summary>
        [Test]
        public void SpecTabModel_LivesInRpaAssembly()
        {
            Assert.That(typeof(Agent.Rpa.Models.SpecTabModel).Assembly, Is.EqualTo(RpaAssembly));
        }

        /// <summary>The Knowledge base tab model lives in the models assembly, next to <see cref="ChatModel"/>.</summary>
        [Test]
        public void KnowledgeBaseModel_LivesInRpaAssembly()
        {
            Assert.That(typeof(Agent.Rpa.Models.KnowledgeBaseModel).Assembly, Is.EqualTo(RpaAssembly));
        }

        /// <summary>Layout state (panel visibility, focus) is view state: rule 10.</summary>
        [Test]
        public void TabModels_ExposeNoLayoutState()
        {
            var violations = new[]
            {
                typeof(Agent.Rpa.Models.SpecTabModel),
                typeof(Agent.Rpa.Models.KnowledgeBaseModel)
            }
            .SelectMany(t => t.GetProperties())
            .Where(p => p.Name.IndexOf("Visib", StringComparison.OrdinalIgnoreCase) >= 0)
            .Select(p => p.DeclaringType.Name + "." + p.Name)
            .ToArray();

            Assert.That(violations, Is.Empty,
                "Layout state must stay in the view: " + string.Join(", ", violations));
        }

        private static void AssertNetStandard(Assembly assembly)
        {
            var attribute = assembly.GetCustomAttribute<TargetFrameworkAttribute>();

            Assert.That(attribute, Is.Not.Null, assembly.GetName().Name + " has no TargetFrameworkAttribute");
            Assert.That(attribute!.FrameworkName, Does.StartWith(".NETStandard"),
                assembly.GetName().Name + " must target netstandard2.0 but targets " + attribute.FrameworkName);
        }

        private static void AssertNoForbiddenReferences(Assembly assembly)
        {
            var references = assembly.GetReferencedAssemblies().Select(r => r.Name).ToList();

            var violations = references
                .Where(name => ForbiddenAssemblies.Any(forbidden =>
                    string.Equals(forbidden, name, StringComparison.OrdinalIgnoreCase)))
                .ToArray();

            Assert.That(violations, Is.Empty,
                assembly.GetName().Name + " must stay UI-free but references: " + string.Join(", ", violations));
        }
    }
}
