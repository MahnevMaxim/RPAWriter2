using NUnit.Framework;
using RPAWriter.Models;
using RPAWriter.Mvvm;
using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;

namespace RPAWriter.Tests
{
    /// <summary>
    /// Guards the boundary described in documentation/13-model-layer.md:
    /// <list type="bullet">
    /// <item><c>RPAWriter.Models</c> and <c>RPAWriter.Mvvm</c> must be <c>netstandard2.0</c>.</item>
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
        private static Assembly CoreAssembly => typeof(RPAWriter.Core.Models.ChatMessage).Assembly;

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
                "RPAWriter.Models must never depend on the VSIX assembly: that would break headless tests.");
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

            Assert.That(references, Does.Contain("RPAWriter.Mvvm"));
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
                "RPAWriter.Core must stay usable from headless tests; the VSIX assembly may only depend on it.");
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
                typeof(RPAWriter.Core.Models.ChatMessage),
                typeof(RPAWriter.Core.Models.ChatPayload),
                typeof(RPAWriter.Core.Models.TextPayload),
                typeof(RPAWriter.Core.Models.ToolCallPayload),
                typeof(RPAWriter.Core.Models.QuestionPayload),
                typeof(RPAWriter.Core.Models.SubagentPayload),
                typeof(RPAWriter.Core.Models.CheckpointPayload),
                typeof(RPAWriter.Core.Models.ChatAttachment),
                typeof(RPAWriter.Core.Models.ChatConversation),
                typeof(RPAWriter.Core.Models.ChatHistoryMessage),
                typeof(RPAWriter.Core.Models.PendingMessage),
                typeof(RPAWriter.Core.Models.PendingDelivery),
                typeof(RPAWriter.Core.Models.UiStateData),
                typeof(RPAWriter.Core.Models.AgentPhase),
                typeof(RPAWriter.Core.Services.ChatMessageMapper),
                typeof(RPAWriter.Core.Models.ChatViewMode),
                typeof(RPAWriter.Core.Contracts.IChatHistoryStore),
                typeof(RPAWriter.Core.Contracts.IUiStateStore)
            };

            var wrongAssembly = expected
                .Where(t => t.Assembly != CoreAssembly)
                .Select(t => t.FullName)
                .ToArray();

            Assert.That(wrongAssembly, Is.Empty,
                "Chat domain types must live in RPAWriter.Core: " + string.Join(", ", wrongAssembly));
        }

        /// <summary>
        /// Material.Icons is a view concern: the queue delivery icon is resolved by a converter in
        /// the VSIX project, so <see cref="RPAWriter.Core.Models.PendingMessage"/> exposes no icon.
        /// </summary>
        [Test]
        public void PendingMessage_ExposesNoIconProperty()
        {
            var iconProperties = typeof(RPAWriter.Core.Models.PendingMessage)
                .GetProperties()
                .Where(p => p.Name.IndexOf("Icon", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(p => p.Name)
                .ToArray();

            Assert.That(iconProperties, Is.Empty,
                "Icon selection belongs to the view layer: " + string.Join(", ", iconProperties));
        }

        /// <summary>
        /// The chat model is the reference implementation of the model rules: it lives in
        /// RPAWriter.Models, so it can be constructed and exercised from this net8.0 test project
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
        public void LogAnalyzer_LivesInModelsAssembly()
        {
            Assert.That(typeof(RPAWriter.Models.LogAnalyzer).Assembly, Is.EqualTo(ModelsAssembly));
        }

        /// <summary>Layout state (panel visibility, focus) is view state: rule 10.</summary>
        [Test]
        public void LogAnalyzer_ExposesNoLayoutState()
        {
            var violations = typeof(RPAWriter.Models.LogAnalyzer).GetProperties()
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
        public void LogAnalysisTypes_LiveInCore()
        {
            var expected = new[]
            {
                typeof(RPAWriter.Core.Contracts.ILogProviderSource),
                typeof(RPAWriter.Core.Contracts.IOutputWindowTextSource),
                typeof(RPAWriter.Core.Contracts.IChatPromptSink),
                typeof(RPAWriter.Core.Contracts.IUserMessageSink),
                typeof(RPAWriter.Core.Contracts.ILogAnalysisSettings),
                typeof(RPAWriter.Core.Models.LogLevelOption),
                typeof(RPAWriter.Core.Services.ProdLogAnalysisService),
                typeof(RPAWriter.Core.Services.ProdLogQueryBuilder),
                typeof(RPAWriter.Core.Services.RobotNameHistory)
            };

            var wrongAssembly = expected
                .Where(t => t.Assembly != CoreAssembly)
                .Select(t => t.FullName)
                .ToArray();

            Assert.That(wrongAssembly, Is.Empty,
                "Log analysis types must live in RPAWriter.Core: " + string.Join(", ", wrongAssembly));
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
