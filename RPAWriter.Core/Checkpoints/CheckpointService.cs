using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RPAWriter.Core.Checkpoints
{
    /// <summary>
    /// Per-task file checkpoints backed by an isolated shadow git repository whose
    /// <c>core.worktree</c> points at the user's workspace. The user's own git
    /// history is never touched.
    /// </summary>
    public sealed class CheckpointService : ICheckpointService
    {
        private readonly CheckpointServiceOptions _options;
        private readonly IGitRunner _git;
        private readonly string _shadowDir;
        private readonly List<Checkpoint> _checkpoints = new List<Checkpoint>();

        private bool _initialized;
        private bool _disposed;
        private string _baseHash;

        public CheckpointService(CheckpointServiceOptions options)
            : this(options, new ProcessGitRunner())
        {
        }

        internal CheckpointService(CheckpointServiceOptions options, IGitRunner git)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(options.TaskId)) throw new ArgumentException("TaskId is required.", nameof(options));
            if (string.IsNullOrWhiteSpace(options.WorkspaceDir)) throw new ArgumentException("WorkspaceDir is required.", nameof(options));
            if (string.IsNullOrWhiteSpace(options.StateDir)) throw new ArgumentException("StateDir is required.", nameof(options));

            _options = options;
            _git = git ?? throw new ArgumentNullException(nameof(git));

            var workspace = NormalizeDir(options.WorkspaceDir);
            EnsureNotProtectedPath(workspace);

            _options.WorkspaceDir = workspace;
            _options.StateDir = NormalizeDir(options.StateDir);
            _shadowDir = Path.Combine(_options.StateDir, "tasks", options.TaskId, "checkpoints");
        }

        public bool IsInitialized => _initialized;

        public IReadOnlyList<Checkpoint> Checkpoints => _checkpoints;

        public async Task<InitResult> InitializeAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (_initialized)
                throw new InvalidOperationException("Checkpoint service is already initialized.");

            var stopwatch = Stopwatch.StartNew();

            Directory.CreateDirectory(_shadowDir);

            var nested = FindNestedGitRepository();
            if (nested != null)
            {
                throw new InvalidOperationException(
                    "Checkpoints are disabled because a nested git repository was detected at: " + nested);
            }

            if (Directory.Exists(Path.Combine(_shadowDir, ".git")))
            {
                var worktree = await GetConfigAsync("core.worktree", ct).ConfigureAwait(false);
                var worktreeTrimmed = (worktree ?? "").Trim();
                if (string.IsNullOrEmpty(worktreeTrimmed) || !ArePathsEqual(worktreeTrimmed, _options.WorkspaceDir))
                {
                    throw new InvalidOperationException(
                        "Checkpoints can only be used in the original workspace: " + worktreeTrimmed + " != " + _options.WorkspaceDir);
                }

                await WriteExcludeFileAsync().ConfigureAwait(false);
                _baseHash = (await RunGitAsync(ct, "rev-parse", "HEAD").ConfigureAwait(false)).StdOut.Trim();
                _initialized = true;
                return new InitResult { Created = false, BaseHash = _baseHash, DurationMs = stopwatch.ElapsedMilliseconds };
            }

            await RunGitAsync(ct, "init", "--template=").ConfigureAwait(false);
            await RunGitAsync(ct, "config", "core.worktree", _options.WorkspaceDir).ConfigureAwait(false);
            await RunGitAsync(ct, "config", "commit.gpgSign", "false").ConfigureAwait(false);
            await RunGitAsync(ct, "config", "user.name", "RPAWriter").ConfigureAwait(false);
            await RunGitAsync(ct, "config", "user.email", "noreply@example.com").ConfigureAwait(false);
            await WriteExcludeFileAsync().ConfigureAwait(false);

            await RunGitAsync(ct, "add", "-A", "--ignore-errors").ConfigureAwait(false);
            await RunGitAsync(ct, "commit", "--allow-empty", "-m", "initial checkpoint").ConfigureAwait(false);

            _baseHash = (await RunGitAsync(ct, "rev-parse", "HEAD").ConfigureAwait(false)).StdOut.Trim();
            _initialized = true;

            return new InitResult { Created = true, BaseHash = _baseHash, DurationMs = stopwatch.ElapsedMilliseconds };
        }

        public async Task<CheckpointSaveResult> SaveAsync(string message, bool allowEmpty, CancellationToken ct = default)
        {
            EnsureInitialized();
            var finalMessage = string.IsNullOrWhiteSpace(message) ? "checkpoint" : message;

            await RunGitAsync(ct, "add", "-A", "--ignore-errors").ConfigureAwait(false);

            var args = new List<string> { "commit", "-m", finalMessage };
            if (allowEmpty) args.Add("--allow-empty");

            var result = await _git.RunAsync(_shadowDir, args, ct).ConfigureAwait(false);
            if (result.Success)
            {
                var hash = (await RunGitAsync(ct, "rev-parse", "HEAD").ConfigureAwait(false)).StdOut.Trim();
                _checkpoints.Add(new Checkpoint
                {
                    Ref = hash,
                    Sequence = _checkpoints.Count + 1,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    Message = finalMessage,
                });
                return new CheckpointSaveResult { CommitHash = hash, IsEmpty = false };
            }

            if (IsNothingToCommit(result))
                return new CheckpointSaveResult { CommitHash = null, IsEmpty = true };

            throw new InvalidOperationException("git commit failed: " + DescribeFailure(result));
        }

        public async Task RestoreAsync(string commitHash, CancellationToken ct = default)
        {
            EnsureInitialized();
            if (string.IsNullOrWhiteSpace(commitHash)) throw new ArgumentException("commitHash is required.", nameof(commitHash));

            var beforeHead = (await RunGitAsync(ct, "rev-parse", "HEAD").ConfigureAwait(false)).StdOut.Trim();
            var txId = Guid.NewGuid().ToString("N");

            // Capture the current (possibly unsnapshotted) state so a restore is always reversible.
            await _git.RunAsync(_shadowDir, new[] { "stash", "push", "--include-untracked", "-m", "rpa restore transaction " + txId }, ct)
                .ConfigureAwait(false);

            try
            {
                await RunGitAsync(ct, "reset", "--hard", commitHash).ConfigureAwait(false);
                await RunGitAsync(ct, "clean", "-fd").ConfigureAwait(false);
            }
            catch
            {
                try
                {
                    await RunGitAsync(ct, "reset", "--hard", beforeHead).ConfigureAwait(false);
                    await RunGitAsync(ct, "clean", "-fd").ConfigureAwait(false);
                    await _git.RunAsync(_shadowDir, new[] { "stash", "pop" }, ct).ConfigureAwait(false);
                }
                catch
                {
                    // Best effort: the stash entry remains for manual recovery.
                }
                throw;
            }

            var index = _checkpoints.FindIndex(c => string.Equals(c.Ref, commitHash, StringComparison.OrdinalIgnoreCase));
            if (index >= 0 && index + 1 < _checkpoints.Count)
                _checkpoints.RemoveRange(index + 1, _checkpoints.Count - index - 1);
        }

        public async Task<IReadOnlyList<CheckpointDiff>> GetDiffAsync(string from, string to, CancellationToken ct = default)
        {
            EnsureInitialized();
            if (string.IsNullOrWhiteSpace(from)) throw new ArgumentException("from is required.", nameof(from));

            var range = string.IsNullOrWhiteSpace(to) ? from : from + ".." + to;
            var names = (await RunGitAsync(ct, "diff", "--name-only", "--no-renames", "-z", range).ConfigureAwait(false)).StdOut;
            var changed = names.Split('\0').Where(s => !string.IsNullOrEmpty(s)).ToList();

            var diffs = new List<CheckpointDiff>(changed.Count);
            foreach (var rel in changed)
            {
                var relative = rel.Replace('/', Path.DirectorySeparatorChar);
                diffs.Add(new CheckpointDiff
                {
                    RelativePath = relative,
                    AbsolutePath = Path.Combine(_options.WorkspaceDir, relative),
                    Before = await ShowPathAsync(from, rel, ct).ConfigureAwait(false),
                    After = await ShowPathAsync(string.IsNullOrWhiteSpace(to) ? "HEAD" : to, rel, ct).ConfigureAwait(false),
                });
            }
            return diffs;
        }

        public async Task DeleteAsync(CancellationToken ct = default)
        {
            if (_disposed) return;

            _initialized = false;
            _checkpoints.Clear();
            _baseHash = null;

            await Task.Run(() => DeleteDirectoryRecursive(_shadowDir), ct).ConfigureAwait(false);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _initialized = false;
            _checkpoints.Clear();
        }

        private void EnsureInitialized()
        {
            ThrowIfDisposed();
            if (!_initialized) throw new InvalidOperationException("Checkpoint service is not initialized.");
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(CheckpointService));
        }

        private async Task<GitResult> RunGitAsync(CancellationToken ct, params string[] args)
        {
            var result = await _git.RunAsync(_shadowDir, args, ct).ConfigureAwait(false);
            if (!result.Success)
                throw new InvalidOperationException("git " + string.Join(" ", args) + " failed: " + DescribeFailure(result));
            return result;
        }

        private async Task<string> GetConfigAsync(string key, CancellationToken ct)
        {
            var result = await _git.RunAsync(_shadowDir, new[] { "config", "--get", key }, ct).ConfigureAwait(false);
            return result.Success ? result.StdOut : null;
        }

        private async Task WriteExcludeFileAsync()
        {
            var infoDir = Path.Combine(_shadowDir, ".git", "info");
            Directory.CreateDirectory(infoDir);
            var patterns = CheckpointExcludePatterns.Build(_options.WorkspaceDir);
            await Task.Run(() => File.WriteAllText(Path.Combine(infoDir, "exclude"), string.Join("\n", patterns) + "\n"))
                .ConfigureAwait(false);
        }

        private async Task<string> ShowPathAsync(string reference, string relativePath, CancellationToken ct)
        {
            var result = await _git.RunAsync(_shadowDir, new[] { "show", reference + ":" + relativePath }, ct).ConfigureAwait(false);
            return result.Success ? result.StdOut : "";
        }

        private string FindNestedGitRepository()
        {
            var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".git", ".vs", "bin", "obj", "node_modules", "packages", "TestResults",
            };

            var stack = new Stack<string>();
            stack.Push(_options.WorkspaceDir);

            while (stack.Count > 0)
            {
                var current = stack.Pop();
                string[] subdirs;
                try
                {
                    subdirs = Directory.GetDirectories(current);
                }
                catch
                {
                    continue;
                }

                foreach (var sub in subdirs)
                {
                    var name = Path.GetFileName(sub);
                    if (name.Equals(".git", StringComparison.OrdinalIgnoreCase))
                    {
                        var parent = Path.GetDirectoryName(sub);
                        if (parent != null && ArePathsEqual(parent, _options.WorkspaceDir))
                            continue; // the user's own repository at the workspace root is expected
                        return sub;
                    }

                    if (skip.Contains(name))
                        continue;

                    stack.Push(sub);
                }
            }

            return null;
        }

        private static void DeleteDirectoryRecursive(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;

            try
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
                }
                foreach (var dir in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories))
                {
                    try { File.SetAttributes(dir, FileAttributes.Normal); } catch { }
                }
            }
            catch
            {
                // best-effort attribute clearing; fall through to the final delete
            }

            try { Directory.Delete(path, recursive: true); } catch { /* best-effort */ }
        }

        private static bool IsNothingToCommit(GitResult result)
        {
            var text = (result.StdOut ?? "") + "\n" + (result.StdErr ?? "");
            return text.IndexOf("nothing to commit", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string DescribeFailure(GitResult result)
        {
            var text = ((result.StdErr ?? "") + " " + (result.StdOut ?? "")).Trim();
            return string.IsNullOrEmpty(text) ? ("exit code " + result.ExitCode) : text;
        }

        private static string NormalizeDir(string path)
        {
            var full = Path.GetFullPath(path);
            if (full.Length > 3)
                full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return full;
        }

        private static bool ArePathsEqual(string a, string b)
        {
            return string.Equals(NormalizeDir(a), NormalizeDir(b), StringComparison.OrdinalIgnoreCase);
        }

        private static void EnsureNotProtectedPath(string workspace)
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var protectedPaths = new[]
            {
                home,
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            };
            var downloads = Path.Combine(home, "Downloads");

            foreach (var p in protectedPaths)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                if (ArePathsEqual(workspace, p))
                    throw new InvalidOperationException("Cannot use checkpoints in " + workspace);
            }

            if (ArePathsEqual(workspace, downloads))
                throw new InvalidOperationException("Cannot use checkpoints in " + workspace);
        }
    }
}
