using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Services;

namespace RPAWriter2.Services
{
    /// <summary>
    /// Host implementations of the contracts the log analysis model depends on
    /// (<c>documentation/13-model-layer.md</c>, section 18). They are the only place that knows about
    /// the provider folder on disk, the Visual Studio Output window and the settings file.
    /// </summary>
    internal sealed class LogProviderSourceAdapter : ILogProviderSource
    {
        private readonly Func<string?> _configuredPath;

        public LogProviderSourceAdapter(Func<string?> configuredPath)
        {
            _configuredPath = configuredPath;
        }

        public string ResolveDirectory()
        {
            var configured = (_configuredPath() ?? "").Trim();
            return string.IsNullOrWhiteSpace(configured) ? AppDataPaths.Resolve("Providers") : configured;
        }

        public IReadOnlyList<IProdLogProvider> Discover(string directory) => LogProviderLoader.Discover(directory);

        public IProdLogProvider CreateFallback() => new StubProdLogProvider();
    }

    /// <summary>Reads the Output window text the "Analyze logs → Output window" scenario analyzes.</summary>
    internal sealed class OutputWindowTextSourceAdapter : IOutputWindowTextSource
    {
        private readonly Func<CancellationToken, Task<string>> _read;

        public OutputWindowTextSourceAdapter(Func<CancellationToken, Task<string>> read)
        {
            _read = read;
        }

        public Task<string> GetTextAsync(CancellationToken cancellationToken = default) => _read(cancellationToken);
    }

    /// <summary>Delivers a prompt the model built into the chat, as if the user had typed it.</summary>
    internal sealed class ChatPromptSinkAdapter : IChatPromptSink
    {
        private readonly Func<string, CancellationToken, Task> _send;

        public ChatPromptSinkAdapter(Func<string, CancellationToken, Task> send)
        {
            _send = send;
        }

        public Task SendPromptAsync(string prompt, CancellationToken cancellationToken = default)
            => _send(prompt, cancellationToken);
    }

    /// <summary>Renders the model's notices and errors in the chat (notice card / error card).</summary>
    internal sealed class UserMessageSinkAdapter : IUserMessageSink
    {
        private readonly Action<string> _notice;
        private readonly Action<string> _error;

        public UserMessageSinkAdapter(Action<string> notice, Action<string> error)
        {
            _notice = notice;
            _error = error;
        }

        public Task NoticeAsync(string message, CancellationToken cancellationToken = default)
        {
            _notice(message);
            return Task.CompletedTask;
        }

        public Task ErrorAsync(string message, CancellationToken cancellationToken = default)
        {
            _error(message);
            return Task.CompletedTask;
        }
    }

    /// <summary>Settings slice of the log analysis model: provider folder and robot-name history.</summary>
    internal sealed class SettingsLogAnalysisAdapter : ILogAnalysisSettings
    {
        private readonly Func<SettingsData?> _settings;

        public SettingsLogAnalysisAdapter(Func<SettingsData?> settings)
        {
            _settings = settings;
        }

        public string LogProvidersPath => (_settings()?.LogProvidersPath ?? "").Trim();

        public IReadOnlyList<string> GetRobotNameHistory()
            => _settings()?.RobotNameHistory ?? new List<string>();

        public void SetRobotNameHistory(IReadOnlyList<string> history)
        {
            var settings = _settings();
            if (settings == null)
                return;

            settings.RobotNameHistory = new List<string>(history);
            SettingsStore.Save(settings);
        }
    }
}
