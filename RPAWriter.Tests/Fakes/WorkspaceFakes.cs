using System;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests.Fakes
{
    /// <summary>In-memory <see cref="IWorkspaceProvider"/>: returns a fixed workspace and can raise changes.</summary>
    public sealed class FakeWorkspaceProvider : IWorkspaceProvider
    {
        public WorkspaceInfo Current { get; set; }

        public event EventHandler WorkspaceChanged;

        public WorkspaceInfo GetCurrentWorkspace() => Current;

        public void RaiseChanged() => WorkspaceChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>In-memory <see cref="IWorkspaceLogSettingsStore"/> for settings-model scoping tests.</summary>
    public sealed class InMemoryWorkspaceLogSettingsStore : IWorkspaceLogSettingsStore
    {
        public WorkspaceLogSettings Stored { get; set; } = new WorkspaceLogSettings();
        public int LoadCount { get; private set; }
        public int SaveCount { get; private set; }

        public WorkspaceLogSettings Load()
        {
            LoadCount++;
            return Stored;
        }

        public void Save(WorkspaceLogSettings settings)
        {
            SaveCount++;
            Stored = settings;
        }
    }

    /// <summary>In-memory <see cref="ISecretsStore"/> for API-key tests.</summary>
    public sealed class InMemorySecretsStore : ISecretsStore
    {
        public string ApiKey { get; set; } = "";
        public int SaveCount { get; private set; }
        public int ClearCount { get; private set; }

        public string LoadApiKey() => ApiKey;

        public void SaveApiKey(string apiKey)
        {
            SaveCount++;
            ApiKey = apiKey ?? "";
        }

        public void ClearApiKey()
        {
            ClearCount++;
            ApiKey = "";
        }
    }
}
