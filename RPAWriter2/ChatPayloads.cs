using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using RPAWriter.Core.Checkpoints;

namespace RPAWriter2
{
    /// <summary>
    /// Base type for the typed content payload attached to a <see cref="ChatMessage"/>.
    /// The chat list renders each payload type via its own DataTemplate (same pattern as
    /// <c>MessageBlock</c> → <c>TextMessageBlock</c>/<c>CodeMessageBlock</c>).
    /// </summary>
    public abstract class ChatPayload
    {
    }

    /// <summary>Plain markdown text — the common case for user and agent messages.</summary>
    public sealed class TextPayload : ChatPayload
    {
        public TextPayload()
        {
        }

        public TextPayload(string text)
        {
            Text = text ?? "";
        }

        public string Text { get; set; } = "";
    }

    /// <summary>A tool invocation: executed or blocked (in Plan mode).</summary>
    public sealed class ToolCallPayload : ChatPayload
    {
        public string ToolName { get; set; } = "";
        public string Result { get; set; } = "";
        public bool Blocked { get; set; }
    }

    /// <summary>
    /// An interactive question asked by the agent, with answer options. The chat list
    /// renders it as a message with answer buttons; <see cref="Answer"/> is set when the
    /// user picks an option (or skips/cancels), which disables the buttons.
    /// </summary>
    public sealed class QuestionPayload : ChatPayload, INotifyPropertyChanged
    {
        private string _answer = "";
        private bool _isSkipped;

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>The question text.</summary>
        public string Question { get; set; } = "";

        /// <summary>The answer options shown as buttons (2..5 items).</summary>
        public IReadOnlyList<string> Options { get; set; } = new List<string>();

        /// <summary>The option selected by the user, or empty while unanswered.</summary>
        public string Answer
        {
            get => _answer;
            set
            {
                if (_answer != value)
                {
                    _answer = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsAnswered));
                    OnPropertyChanged(nameof(IsFreeTextAnswer));
                }
            }
        }

        /// <summary>True once the user answered (or skipped/cancelled) the question.</summary>
        public bool IsAnswered => !string.IsNullOrEmpty(Answer);

        /// <summary>
        /// True when the question was answered with free text that does not match any offered
        /// option (e.g. the user typed the answer in the chat instead of clicking a button).
        /// Drives the distinct "answer" line in the question card.
        /// </summary>
        public bool IsFreeTextAnswer => IsAnswered && !IsSkipped && !(Options?.Contains(Answer) ?? false);

        /// <summary>True when the user explicitly skipped the question.</summary>
        public bool IsSkipped
        {
            get => _isSkipped;
            set
            {
                if (_isSkipped != value)
                {
                    _isSkipped = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsFreeTextAnswer));
                }
            }
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Live status of a single sub-agent (spawn_agent): updated in place as the
    /// sub-agent runs (tool calls, latest tool) and finalized with its report.
    /// </summary>
    public sealed class SubagentStatusItem : INotifyPropertyChanged
    {
        private string _status = "pending";
        private int _toolCalls;
        private string _latestToolCall = "";
        private string _result = "";
        private string _error = "";

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>The task/prompt the sub-agent was asked to complete.</summary>
        public string Prompt { get; set; } = "";

        /// <summary>pending | running | completed | failed | cancelled.</summary>
        public string Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnChanged();
                    OnChanged(nameof(StatusText));
                    OnChanged(nameof(HasOutput));
                    OnChanged(nameof(HasError));
                }
            }
        }

        /// <summary>Number of tool calls the sub-agent has executed so far.</summary>
        public int ToolCalls
        {
            get => _toolCalls;
            set
            {
                if (_toolCalls != value)
                {
                    _toolCalls = value;
                    OnChanged();
                    OnChanged(nameof(StatusText));
                }
            }
        }

        /// <summary>The most recent tool call the sub-agent made (for the progress line).</summary>
        public string LatestToolCall
        {
            get => _latestToolCall;
            set { if (_latestToolCall != value) { _latestToolCall = value; OnChanged(); } }
        }

        /// <summary>The sub-agent's final report (markdown), when completed.</summary>
        public string Result
        {
            get => _result;
            set { if (_result != value) { _result = value; OnChanged(); } }
        }

        /// <summary>The failure message, when the sub-agent failed.</summary>
        public string Error
        {
            get => _error;
            set { if (_error != value) { _error = value; OnChanged(); } }
        }

        /// <summary>Short human-readable status line shown under the prompt.</summary>
        public string StatusText
        {
            get
            {
                var toolLabel = ToolCalls > 0 ? " · " + ToolCalls + " tool call(s)" : "";
                switch (Status)
                {
                    case "running": return "Running" + toolLabel + "…";
                    case "completed": return "Completed" + toolLabel;
                    case "failed": return "Failed";
                    case "cancelled": return "Cancelled";
                    default: return "Pending…";
                }
            }
        }

        /// <summary>True once the sub-agent finished (success or failure) and has a report/error to show.</summary>
        public bool HasOutput => Status == "completed" || Status == "failed";

        /// <summary>True when the sub-agent failed with an error message.</summary>
        public bool HasError => Status == "failed" && !string.IsNullOrEmpty(Error);

        private void OnChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>Payload for a <see cref="ChatMessageKind.Subagent"/> status card.</summary>
    public sealed class SubagentPayload : ChatPayload
    {
        /// <summary>The single sub-agent tracked by this card (v1 spawns one sub-agent at a time).</summary>
        public SubagentStatusItem Item { get; set; } = new SubagentStatusItem();
    }

    /// <summary>Payload for a <see cref="ChatMessageKind.Checkpoint"/> indicator with Compare/Restore actions.</summary>
    public sealed class CheckpointPayload : ChatPayload, INotifyPropertyChanged
    {
        private bool _hasChanges = true;

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>Commit hash in the shadow repository.</summary>
        public string CommitHash { get; set; } = "";

        /// <summary>Human-readable checkpoint message.</summary>
        public string Message { get; set; } = "";

        /// <summary>Task/conversation id owning the shadow repository.</summary>
        public string TaskId { get; set; } = "";

        /// <summary>Workspace directory the checkpoint belongs to.</summary>
        public string WorkspaceDir { get; set; } = "";

        /// <summary>1-based sequence of the checkpoint within the task.</summary>
        public int Sequence { get; set; }

        /// <summary>When the checkpoint was created (UTC).</summary>
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// True when files changed after this checkpoint (a newer snapshot exists), so a
        /// Compare diff can be shown. False for the latest checkpoint (nothing changed since).
        /// </summary>
        public bool HasChanges
        {
            get => _hasChanges;
            set
            {
                if (_hasChanges != value)
                {
                    _hasChanges = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>Shortened commit hash for display.</summary>
        public string ShortHash
        {
            get
            {
                var hash = CommitHash ?? "";
                return hash.Length > 8 ? hash.Substring(0, 8) : hash;
            }
        }

        /// <summary>Title line shown in the indicator.</summary>
        public string DisplayTitle => string.IsNullOrWhiteSpace(Message) ? "Checkpoint" : Message;

        /// <summary>Secondary line shown in the indicator.</summary>
        public string Detail => "checkpoint " + ShortHash + " · " + CreatedAtUtc.ToLocalTime().ToString("HH:mm:ss");

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>Payload for a <see cref="ChatMessageKind.Diff"/> indicator: a commit hash whose diff opens in the diff tool window.</summary>
    public sealed class DiffPayload : ChatPayload
    {
        /// <summary>Commit whose diff is shown (short form is displayed on the button).</summary>
        public string CommitHash { get; set; } = "";

        /// <summary>Precomputed diff files for the commit; opened directly by the diff tool window.</summary>
        public IReadOnlyList<DiffFileModel> Files { get; set; } = new List<DiffFileModel>();

        /// <summary>Shortened commit hash for display.</summary>
        public string ShortHash
        {
            get
            {
                var hash = CommitHash ?? "";
                return hash.Length > 8 ? hash.Substring(0, 8) : hash;
            }
        }
    }
}
