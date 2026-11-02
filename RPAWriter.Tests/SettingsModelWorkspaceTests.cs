using NUnit.Framework;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Services;
using RPAWriter.Models;
using RPAWriter.Tests.Fakes;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class SettingsModelWorkspaceTests
    {
        private sealed class InMemorySettingsStore : ISettingsStore
        {
            public SettingsData Stored { get; set; } = new SettingsData();
            public int SaveCount { get; private set; }

            public SettingsData Load() => Stored;

            public void Save(SettingsData settings)
            {
                Stored = settings;
                SaveCount++;
            }
        }

        [Test]
        public void Load_WithoutWorkspace_UsesGlobalLogPaths()
        {
            var global = new InMemorySettingsStore
            {
                Stored = new SettingsData { LogFilePath = "C:\\global.log", ChatLogFilePath = "C:\\chat.log" }
            };
            var model = new SettingsModel(global);

            model.Run();

            Assert.That(model.LogFilePath, Is.EqualTo("C:\\global.log"));
            Assert.That(model.ChatLogFilePath, Is.EqualTo("C:\\chat.log"));
        }

        [Test]
        public void Load_WithWorkspaceLogs_WorkspaceWins()
        {
            var global = new InMemorySettingsStore
            {
                Stored = new SettingsData { LogFilePath = "C:\\global.log" }
            };
            var workspaceLogs = new InMemoryWorkspaceLogSettingsStore
            {
                Stored = new WorkspaceLogSettings { LogFilePath = "C:\\workspace.log" }
            };
            var provider = new FakeWorkspaceProvider
            {
                Current = new WorkspaceInfo { Id = "ws1" }
            };
            var model = new SettingsModel(global, workspaceLogs, provider);

            model.Run();

            Assert.That(model.LogFilePath, Is.EqualTo("C:\\workspace.log"));
        }

        [Test]
        public void Load_WithWorkspaceButEmptyLogs_FallsBackToGlobal()
        {
            var global = new InMemorySettingsStore
            {
                Stored = new SettingsData { LogFilePath = "C:\\global.log" }
            };
            var provider = new FakeWorkspaceProvider
            {
                Current = new WorkspaceInfo { Id = "ws1" }
            };
            var model = new SettingsModel(global, new InMemoryWorkspaceLogSettingsStore(), provider);

            model.Run();

            Assert.That(model.LogFilePath, Is.EqualTo("C:\\global.log"));
        }

        [Test]
        public void Save_WithWorkspace_WritesLogsToWorkspaceAndKeepsGlobalFallback()
        {
            var global = new InMemorySettingsStore
            {
                Stored = new SettingsData { LogFilePath = "C:\\global.log", Model = "gpt-4o" }
            };
            var workspaceLogs = new InMemoryWorkspaceLogSettingsStore();
            var provider = new FakeWorkspaceProvider { Current = new WorkspaceInfo { Id = "ws1" } };
            var model = new SettingsModel(global, workspaceLogs, provider);
            model.Run();

            model.LogFilePath = "C:\\new-workspace.log";
            model.SaveSettingsCommand();

            Assert.That(workspaceLogs.Stored.LogFilePath, Is.EqualTo("C:\\new-workspace.log"));
            // The global fallback must stay unchanged.
            Assert.That(global.Stored.LogFilePath, Is.EqualTo("C:\\global.log"));
            // Non-log settings still go to the global store.
            Assert.That(global.Stored.Model, Is.EqualTo("gpt-4o"));
        }

        [Test]
        public void Save_WithoutWorkspace_WritesLogsToGlobal()
        {
            var global = new InMemorySettingsStore();
            var model = new SettingsModel(global);
            model.Run();

            model.LogFilePath = "C:\\new.log";
            model.SaveSettingsCommand();

            Assert.That(global.Stored.LogFilePath, Is.EqualTo("C:\\new.log"));
        }

        [Test]
        public void Save_WritesApiKeyToSecretsAndClearsFromJson()
        {
            var global = new InMemorySettingsStore();
            var secrets = new InMemorySecretsStore();
            var model = new SettingsModel(global, secretsStore: secrets);
            model.Run();

            model.ApiKey = "sk-secret";
            model.SaveSettingsCommand();

            Assert.That(secrets.ApiKey, Is.EqualTo("sk-secret"));
            Assert.That(global.Stored.ApiKey, Is.Empty);
        }

        [Test]
        public void Load_ReadsApiKeyFromSecretsStore()
        {
            var secrets = new InMemorySecretsStore { ApiKey = "sk-from-secrets" };
            var model = new SettingsModel(new InMemorySettingsStore(), secretsStore: secrets);

            model.Run();

            Assert.That(model.ApiKey, Is.EqualTo("sk-from-secrets"));
        }

        [Test]
        public void Load_ApiKeyFallsBackToJsonWhenNoSecret()
        {
            var global = new InMemorySettingsStore { Stored = new SettingsData { ApiKey = "legacy-key" } };
            var model = new SettingsModel(global, secretsStore: new InMemorySecretsStore());

            model.Run();

            Assert.That(model.ApiKey, Is.EqualTo("legacy-key"));
        }

        [Test]
        public void ReloadWorkspaceScope_UpdatesEffectiveLogPaths()
        {
            var global = new InMemorySettingsStore
            {
                Stored = new SettingsData { LogFilePath = "C:\\global.log" }
            };
            var workspaceLogs = new InMemoryWorkspaceLogSettingsStore
            {
                Stored = new WorkspaceLogSettings { LogFilePath = "C:\\workspace.log" }
            };
            var provider = new FakeWorkspaceProvider();
            var model = new SettingsModel(global, workspaceLogs, provider);
            model.Run();

            Assert.That(model.LogFilePath, Is.EqualTo("C:\\global.log"));

            provider.Current = new WorkspaceInfo { Id = "ws1" };
            model.ReloadWorkspaceScope();

            Assert.That(model.LogFilePath, Is.EqualTo("C:\\workspace.log"));
        }
    }
}
