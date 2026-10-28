using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using RPAWriter.Core.Checkpoints;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class CheckpointServiceTests
    {
        private string _workspace;
        private string _state;

        [SetUp]
        public void SetUp()
        {
            _workspace = Path.Combine(Path.GetTempPath(), "rpawriter_cp_ws_" + Guid.NewGuid().ToString("N"));
            _state = Path.Combine(Path.GetTempPath(), "rpawriter_cp_state_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_workspace);
            Directory.CreateDirectory(_state);
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(_workspace, recursive: true); } catch { }
            try { Directory.Delete(_state, recursive: true); } catch { }
        }

        private CheckpointService CreateService(string taskId = "task1")
        {
            return new CheckpointService(new CheckpointServiceOptions
            {
                TaskId = taskId,
                WorkspaceDir = _workspace,
                StateDir = _state,
            });
        }

        private static bool GitAvailable()
        {
            try
            {
                var psi = new ProcessStartInfo("git.exe", "--version")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using (var p = Process.Start(psi))
                {
                    p.WaitForExit(10000);
                    return p.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private static void RequireGit()
        {
            if (!GitAvailable()) Assert.Ignore("git.exe is not available on PATH.");
        }

        private string Write(string relative, string content)
        {
            var full = Path.Combine(_workspace, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, content);
            return full;
        }

        private string Read(string relative)
        {
            return File.ReadAllText(Path.Combine(_workspace, relative.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static string RunGit(string workingDir, string arguments)
        {
            var psi = new ProcessStartInfo("git.exe", arguments)
            {
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using (var p = Process.Start(psi))
            {
                var stdout = p.StandardOutput.ReadToEnd();
                var stderr = p.StandardError.ReadToEnd();
                p.WaitForExit();
                return (stdout + "\n" + stderr).Trim();
            }
        }

        [Test]
        public async Task Initialize_creates_shadow_repo_and_reopen_is_idempotent()
        {
            RequireGit();
            var svc = CreateService();
            var res = await svc.InitializeAsync();

            Assert.That(res.Created, Is.True);
            Assert.That(svc.IsInitialized, Is.True);
            Assert.That(res.BaseHash, Is.Not.Null.And.Not.Empty);

            var svc2 = CreateService();
            var res2 = await svc2.InitializeAsync();
            Assert.That(res2.Created, Is.False);
            Assert.That(res2.BaseHash, Is.EqualTo(res.BaseHash));
        }

        [Test]
        public void Initialize_rejects_nested_git_repository()
        {
            RequireGit();
            var nested = Path.Combine(_workspace, "subproject");
            Directory.CreateDirectory(nested);
            RunGit(nested, "init");

            var svc = CreateService();
            var ex = Assert.ThrowsAsync<InvalidOperationException>(async () => await svc.InitializeAsync());
            Assert.That(ex.Message, Does.Contain("nested git repository"));
        }

        [Test]
        public async Task Initialize_rejects_workspace_mismatch()
        {
            RequireGit();
            var svc = CreateService();
            await svc.InitializeAsync();

            var other = Path.Combine(Path.GetTempPath(), "rpawriter_cp_other_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(other);
            try
            {
                var svc2 = new CheckpointService(new CheckpointServiceOptions
                {
                    TaskId = "task1",
                    WorkspaceDir = other,
                    StateDir = _state,
                });
                var ex = Assert.ThrowsAsync<InvalidOperationException>(async () => await svc2.InitializeAsync());
                Assert.That(ex.Message, Does.Contain("original workspace"));
            }
            finally
            {
                try { Directory.Delete(other, recursive: true); } catch { }
            }
        }

        [Test]
        public async Task Save_creates_commit_and_appends_checkpoint()
        {
            RequireGit();
            var svc = CreateService();
            await svc.InitializeAsync();

            Write("a.txt", "hello");
            var res = await svc.SaveAsync("save a", allowEmpty: false);

            Assert.That(res.IsEmpty, Is.False);
            Assert.That(res.CommitHash, Is.Not.Null.And.Not.Empty);
            Assert.That(svc.Checkpoints, Has.Count.EqualTo(1));
            Assert.That(svc.Checkpoints[0].Ref, Is.EqualTo(res.CommitHash));
        }

        [Test]
        public async Task Save_no_changes_returns_empty()
        {
            RequireGit();
            var svc = CreateService();
            await svc.InitializeAsync();

            var res = await svc.SaveAsync("noop", allowEmpty: false);

            Assert.That(res.IsEmpty, Is.True);
            Assert.That(res.CommitHash, Is.Null);
            Assert.That(svc.Checkpoints, Is.Empty);
        }

        [Test]
        public async Task Save_allow_empty_creates_commit()
        {
            RequireGit();
            var svc = CreateService();
            await svc.InitializeAsync();

            var res = await svc.SaveAsync("baseline", allowEmpty: true);

            Assert.That(res.IsEmpty, Is.False);
            Assert.That(res.CommitHash, Is.Not.Null.And.Not.Empty);
            Assert.That(svc.Checkpoints, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task Restore_reverts_modified_file()
        {
            RequireGit();
            var svc = CreateService();
            await svc.InitializeAsync();

            Write("a.txt", "v1");
            var saved = await svc.SaveAsync("save v1", allowEmpty: false);

            Write("a.txt", "v2");

            await svc.RestoreAsync(saved.CommitHash);

            Assert.That(Read("a.txt"), Is.EqualTo("v1"));
        }

        [Test]
        public async Task Restore_removes_files_created_after_checkpoint()
        {
            RequireGit();
            var svc = CreateService();
            await svc.InitializeAsync();

            Write("a.txt", "1");
            var saved = await svc.SaveAsync("save a", allowEmpty: false);

            Write("b.txt", "new file");

            await svc.RestoreAsync(saved.CommitHash);

            Assert.That(File.Exists(Path.Combine(_workspace, "b.txt")), Is.False);
        }

        [Test]
        public async Task GetDiff_returns_before_after_for_changed_files()
        {
            RequireGit();
            var svc = CreateService();
            await svc.InitializeAsync();

            Write("a.txt", "before");
            var h1 = (await svc.SaveAsync("save before", allowEmpty: false)).CommitHash;

            Write("a.txt", "after");
            var h2 = (await svc.SaveAsync("save after", allowEmpty: false)).CommitHash;

            var diffs = await svc.GetDiffAsync(h1, h2);

            Assert.That(diffs, Has.Count.EqualTo(1));
            Assert.That(diffs[0].RelativePath, Is.EqualTo("a.txt"));
            Assert.That(diffs[0].Before, Is.EqualTo("before"));
            Assert.That(diffs[0].After, Is.EqualTo("after"));
        }

        [Test]
        public async Task GetDiff_returns_empty_for_unchanged()
        {
            RequireGit();
            var svc = CreateService();
            await svc.InitializeAsync();

            Write("a.txt", "same");
            var h1 = (await svc.SaveAsync("save 1", allowEmpty: false)).CommitHash;
            var h2 = (await svc.SaveAsync("save 2", allowEmpty: true)).CommitHash;

            var diffs = await svc.GetDiffAsync(h1, h2);
            Assert.That(diffs, Is.Empty);
        }

        [Test]
        public async Task Excluded_paths_are_not_captured()
        {
            RequireGit();
            var svc = CreateService();
            await svc.InitializeAsync();

            Write("bin/output.dll", "binary");
            Write("src/Program.cs", "code");

            var a = (await svc.SaveAsync("with excluded", allowEmpty: false)).CommitHash;

            Write("bin/output.dll", "changed-binary");
            Write("src/Program.cs", "changed-code");
            var b = (await svc.SaveAsync("after", allowEmpty: false)).CommitHash;

            var diffs = await svc.GetDiffAsync(a, b);
            var paths = diffs.Select(d => d.RelativePath).ToList();

            Assert.That(paths, Does.Contain("src" + Path.DirectorySeparatorChar + "Program.cs"));
            Assert.That(paths, Does.Not.Contain("bin" + Path.DirectorySeparatorChar + "output.dll"));
        }

        [Test]
        public async Task Gitignore_is_respected()
        {
            RequireGit();
            var svc = CreateService();
            await svc.InitializeAsync();

            Write(".gitignore", "ignored.txt\n");
            Write("ignored.txt", "secret");

            var a = (await svc.SaveAsync("baseline", allowEmpty: true)).CommitHash;

            Write("ignored.txt", "secret-changed");
            var b = (await svc.SaveAsync("after", allowEmpty: true)).CommitHash;

            var diffs = await svc.GetDiffAsync(a, b);
            Assert.That(diffs, Is.Empty);
            Assert.That(diffs.Select(d => d.RelativePath), Does.Not.Contain("ignored.txt"));
        }

        [Test]
        public async Task Restore_transaction_preserves_uncommitted_changes()
        {
            RequireGit();
            var svc = CreateService();
            await svc.InitializeAsync();

            Write("a.txt", "v1");
            var saved = (await svc.SaveAsync("save v1", allowEmpty: false)).CommitHash;

            Write("a.txt", "v2-uncommitted");

            await svc.RestoreAsync(saved);
            Assert.That(Read("a.txt"), Is.EqualTo("v1"));

            var stashList = RunGit(Path.Combine(_state, "tasks", "task1", "checkpoints"), "stash list");
            Assert.That(stashList, Does.Contain("restore transaction"));
        }

        [Test]
        public async Task DeleteAsync_removes_shadow_repository()
        {
            RequireGit();
            var svc = CreateService();
            await svc.InitializeAsync();
            var shadowDir = Path.Combine(_state, "tasks", "task1", "checkpoints");
            Assert.That(Directory.Exists(shadowDir), Is.True);

            await svc.DeleteAsync();

            Assert.That(Directory.Exists(shadowDir), Is.False);
        }
    }
}
