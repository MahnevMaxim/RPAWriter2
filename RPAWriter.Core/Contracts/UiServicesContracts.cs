using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RPAWriter.Core.Services;

namespace RPAWriter.Core.Contracts
{
    /// <summary>
    /// Returns the directory of the currently open Visual Studio solution, or null when no
    /// solution is open. Host concern: models depend on this seam instead of talking to DTE.
    /// </summary>
    public interface ISolutionPathProvider
    {
        /// <summary>Directory of the open solution, or null when none is open.</summary>
        string GetSolutionDirectory();
    }

    /// <summary>
    /// Describes the currently open workspace and signals when it changes. Models use it to scope
    /// their storage (history, ui state, log settings) and the host subscribes to
    /// <see cref="WorkspaceChanged"/> to re-scope on the fly.
    /// </summary>
    public interface IWorkspaceProvider
    {
        /// <summary>The current workspace, or null when no solution is open.</summary>
        WorkspaceInfo GetCurrentWorkspace();

        /// <summary>Raised when the open solution changes (opened, closed or switched).</summary>
        event EventHandler WorkspaceChanged;
    }

    /// <summary>
    /// Shows an OS file-open dialog and returns the selected path, or null when cancelled.
    /// Kept as a contract so models (spec import, markdown conversion) stay dialog-free and testable.
    /// </summary>
    public interface IFilePicker
    {
        /// <param name="title">Dialog title.</param>
        /// <param name="filter">File-dialog filter, e.g. "Specs (*.md)|*.md|All files (*.*)|*.*".</param>
        /// <returns>The selected path, or null when cancelled.</returns>
        string PickFile(string title, string filter);
    }

    /// <summary>
    /// Shows an OS folder picker and returns the selected path, or null when cancelled.
    /// </summary>
    public interface IFolderPicker
    {
        /// <param name="title">Dialog title.</param>
        /// <param name="initialDirectory">Folder selected initially; may be empty.</param>
        /// <returns>The selected path, or null when cancelled.</returns>
        string PickFolder(string title, string initialDirectory);
    }

    /// <summary>
    /// Opens (or activates) a document in Visual Studio. Host seam over
    /// <c>VsDocumentService.OpenOrActivateAsync</c>, so models never touch DTE.
    /// </summary>
    public interface IDocumentOpener
    {
        Task OpenAsync(string path, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Opens a file with the user's preferred external application (an external text editor for
    /// log files). Host seam over <c>Process.Start</c>, so models never launch processes directly.
    /// </summary>
    public interface IFileOpener
    {
        /// <summary>Opens the file; a null or empty path is a no-op.</summary>
        void Open(string path);
    }

    /// <summary>What to do when a file the model wants to write already exists.</summary>
    public enum FileOverwriteChoice
    {
        /// <summary>Replace the file entirely.</summary>
        Overwrite,

        /// <summary>Compare the files and propose/apply a partial fix.</summary>
        ApplyFixes,

        /// <summary>Leave the existing file unchanged.</summary>
        Keep
    }

    /// <summary>
    /// Asks the user what to do when a generated file already exists. The dialog itself is a
    /// host concern; the model owns the compare/write decision around this answer.
    /// </summary>
    public interface IFileOverwritePrompter
    {
        FileOverwriteChoice AskOverwrite(string displayName);
    }

    /// <summary>
    /// Filesystem seam for the files the models write (docs/spec.md, tasks.md, alg*.md, robot
    /// artifacts). Models depend on this contract instead of calling <c>System.IO</c> directly,
    /// so the writes are testable with an in-memory fake.
    /// </summary>
    public interface IFileWriter
    {
        bool FileExists(string path);

        /// <summary>Returns the file content, or null when the file does not exist.</summary>
        string ReadAllText(string path);

        void WriteAllText(string path, string content);

        void CreateDirectory(string path);

        void CopyFile(string sourcePath, string destinationPath, bool overwrite);

        /// <summary>Returns true when the directory exists.</summary>
        bool DirectoryExists(string path);

        /// <summary>
        /// Returns the full paths of all files under <paramref name="path"/> (recursive).
        /// Empty when the directory does not exist.
        /// </summary>
        IReadOnlyList<string> EnumerateFiles(string path);

        /// <summary>
        /// Deletes the directory and, when <paramref name="recursive"/> is true, everything inside
        /// it. Read-only files are reset before deletion so the call never throws for that reason.
        /// </summary>
        void DeleteDirectory(string path, bool recursive);
    }
}
