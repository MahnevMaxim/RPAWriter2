using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RPAWriter.Core.Contracts;

namespace RPAWriter.Tests.Fakes
{
    /// <summary>Provider discovery stand-in: returns prepared providers (or throws) and records the folder.</summary>
    public sealed class FakeLogProviderSource : ILogProviderSource
    {
        public List<IProdLogProvider> Discovered { get; } = new List<IProdLogProvider>();

        /// <summary>Offline fallback used when <see cref="Discovered"/> is empty; <c>null</c> simulates "nothing at all".</summary>
        public IProdLogProvider Fallback { get; set; }

        public string Directory { get; set; } = @"C:\providers";

        public Exception DiscoverError { get; set; }

        public int DiscoverCalls { get; private set; }

        public string LastDirectory { get; private set; }

        public string ResolveDirectory()
        {
            return Directory;
        }

        public IReadOnlyList<IProdLogProvider> Discover(string directory)
        {
            DiscoverCalls++;
            LastDirectory = directory;

            if (DiscoverError != null)
                throw DiscoverError;

            return Discovered;
        }

        public IProdLogProvider CreateFallback()
        {
            return Fallback;
        }
    }

    /// <summary>Output window stand-in.</summary>
    public sealed class FakeOutputWindowTextSource : IOutputWindowTextSource
    {
        public string Text { get; set; } = "";

        public Exception Error { get; set; }

        public int Calls { get; private set; }

        public Task<string> GetTextAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Error != null)
                throw Error;

            return Task.FromResult(Text);
        }
    }

    /// <summary>
    /// Records everything the model sends to the user: prompts destined to the chat,
    /// notices and error cards.
    /// </summary>
    public sealed class RecordingChatSink : IChatPromptSink, IUserMessageSink
    {
        public List<string> Prompts { get; } = new List<string>();

        public List<string> Notices { get; } = new List<string>();

        public List<string> Errors { get; } = new List<string>();

        /// <summary>When set, sending a prompt throws it.</summary>
        public Exception SendError { get; set; }

        public Task SendPromptAsync(string prompt, CancellationToken cancellationToken = default)
        {
            if (SendError != null)
                throw SendError;

            Prompts.Add(prompt);
            return Task.CompletedTask;
        }

        public Task NoticeAsync(string message, CancellationToken cancellationToken = default)
        {
            Notices.Add(message);
            return Task.CompletedTask;
        }

        public Task ErrorAsync(string message, CancellationToken cancellationToken = default)
        {
            Errors.Add(message);
            return Task.CompletedTask;
        }
    }

    /// <summary>Settings stand-in: remembers the robot name history in memory and counts saves.</summary>
    public sealed class InMemoryLogAnalysisSettings : ILogAnalysisSettings
    {
        public string LogProvidersPath { get; set; } = "";

        public List<string> RobotNameHistory { get; set; } = new List<string>();

        public int SaveCount { get; private set; }

        /// <summary>When set, saving the history throws it.</summary>
        public Exception SaveError { get; set; }

        public IReadOnlyList<string> GetRobotNameHistory()
        {
            return RobotNameHistory.ToList();
        }

        public void SetRobotNameHistory(IReadOnlyList<string> history)
        {
            if (SaveError != null)
                throw SaveError;

            RobotNameHistory = history.ToList();
            SaveCount++;
        }
    }
}
