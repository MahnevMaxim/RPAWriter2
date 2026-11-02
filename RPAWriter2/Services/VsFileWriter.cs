using System.Collections.Generic;
using System.IO;
using RPAWriter.Core.Contracts;

namespace RPAWriter2.Services
{
    /// <summary>
    /// <see cref="IFileWriter"/> over the real filesystem. Used for the files the models write
    /// (docs/spec.md, tasks.md, alg*.md, robot artifacts).
    /// </summary>
    public sealed class VsFileWriter : IFileWriter
    {
        public bool FileExists(string path) => File.Exists(path);

        public string? ReadAllText(string path) => File.ReadAllText(path);

        public void WriteAllText(string path, string content) => File.WriteAllText(path, content);

        public void CreateDirectory(string path) => Directory.CreateDirectory(path);

        public void CopyFile(string sourcePath, string destinationPath, bool overwrite)
            => File.Copy(sourcePath, destinationPath, overwrite);

        public bool DirectoryExists(string path) => Directory.Exists(path);

        public IReadOnlyList<string> EnumerateFiles(string path)
        {
            if (!Directory.Exists(path))
                return new List<string>();

            return Directory.GetFiles(path, "*", SearchOption.AllDirectories);
        }

        public void DeleteDirectory(string path, bool recursive)
        {
            if (!Directory.Exists(path))
                return;

            if (recursive)
            {
                foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(path, recursive);
        }
    }
}
