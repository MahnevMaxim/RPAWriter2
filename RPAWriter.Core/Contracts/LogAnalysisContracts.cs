using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RPAWriter.Core.Contracts
{
    /// <summary>
    /// Supplies the production-log providers available on this machine.
    /// Injected into the log analysis model so discovery (file system, <c>AppData</c> paths) can be
    /// replaced by a fake in tests.
    /// </summary>
    public interface ILogProviderSource
    {
        /// <summary>Directory configured by the user, or the default <c>Providers</c> folder when none is set.</summary>
        string ResolveDirectory();

        /// <summary>External providers found in <paramref name="directory"/>; empty when the folder has none.</summary>
        IReadOnlyList<IProdLogProvider> Discover(string directory);

        /// <summary>Offline fallback provider, used only when discovery returns nothing.</summary>
        IProdLogProvider CreateFallback();
    }

    /// <summary>Text currently shown in the Visual Studio Output window (one analysis source of the model).</summary>
    public interface IOutputWindowTextSource
    {
        Task<string> GetTextAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Destination of a prompt built by the model: the chat tab behaves as if the user had typed it.
    /// Implemented by the host; the model never touches the chat collections directly.
    /// </summary>
    public interface IChatPromptSink
    {
        Task SendPromptAsync(string prompt, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Non-prompt messages the model wants the user to see (empty source, failed provider call).
    /// Kept separate from <see cref="IChatPromptSink"/> so a fake can record them independently.
    /// </summary>
    public interface IUserMessageSink
    {
        Task NoticeAsync(string message, CancellationToken cancellationToken = default);

        Task ErrorAsync(string message, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The slice of user settings the log analysis model reads and writes:
    /// the provider folder and the robot-name history.
    /// </summary>
    public interface ILogAnalysisSettings
    {
        /// <summary>User-configured log provider directory (may be empty: the default folder is used).</summary>
        string LogProvidersPath { get; }

        /// <summary>Remembered robot names, most recent first.</summary>
        IReadOnlyList<string> GetRobotNameHistory();

        /// <summary>Stores the robot-name history and persists the settings.</summary>
        void SetRobotNameHistory(IReadOnlyList<string> history);
    }
}
