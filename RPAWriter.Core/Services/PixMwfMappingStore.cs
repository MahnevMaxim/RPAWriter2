using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Loads/saves the PIX → MWF activity mapping document at
    /// %AppData%\RPAWriter2\pix-mwf-map.json. After construction the store is a
    /// plain in-memory dictionary; call <see cref="Save"/> to persist changes.
    /// </summary>
    public class PixMwfMappingStore
    {
        private readonly string _filePath;
        private readonly Dictionary<string, string> _mappings =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public PixMwfMappingStore() : this(DefaultFilePath)
        {
        }

        public PixMwfMappingStore(string filePath)
        {
            _filePath = string.IsNullOrWhiteSpace(filePath) ? DefaultFilePath : filePath;
            Load();
        }

        /// <summary>Default on-disk location of the mapping document.</summary>
        public static string DefaultFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RPAWriter2",
            "pix-mwf-map.json");

        /// <summary>Full path of the file this store reads and writes.</summary>
        public string FilePath => _filePath;

        /// <summary>Number of mappings currently held in memory.</summary>
        public int Count => _mappings.Count;

        /// <summary>MWF activity mapped for a PIX activity, or null when the activity is unmapped.</summary>
        public string GetMapping(string pixActivityFullName)
        {
            if (string.IsNullOrWhiteSpace(pixActivityFullName))
                return null;

            string value;
            return _mappings.TryGetValue(pixActivityFullName.Trim(), out value) ? value : null;
        }

        /// <summary>Maps (or, when <paramref name="mwfActivityFullName"/> is empty, unmaps) a PIX activity.</summary>
        public void SetMapping(string pixActivityFullName, string mwfActivityFullName)
        {
            if (string.IsNullOrWhiteSpace(pixActivityFullName))
                return;

            var key = pixActivityFullName.Trim();
            if (string.IsNullOrWhiteSpace(mwfActivityFullName))
                _mappings.Remove(key);
            else
                _mappings[key] = mwfActivityFullName.Trim();
        }

        /// <summary>Removes the mapping for a PIX activity, if any.</summary>
        public void RemoveMapping(string pixActivityFullName)
        {
            if (string.IsNullOrWhiteSpace(pixActivityFullName))
                return;

            _mappings.Remove(pixActivityFullName.Trim());
        }

        /// <summary>Copy of all mappings (PIX → MWF).</summary>
        public IReadOnlyDictionary<string, string> GetAll()
            => new Dictionary<string, string>(_mappings, StringComparer.OrdinalIgnoreCase);

        /// <summary>Reloads the mapping document from disk, discarding unsaved changes.</summary>
        public void Load()
        {
            _mappings.Clear();
            try
            {
                if (!File.Exists(_filePath))
                    return;

                var json = File.ReadAllText(_filePath, Encoding.UTF8);
                var document = JsonConvert.DeserializeObject<PixMwfMappingDocument>(json);
                if (document?.Mappings == null)
                    return;

                foreach (var pair in document.Mappings)
                    if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                        _mappings[pair.Key.Trim()] = pair.Value.Trim();
            }
            catch
            {
                // Unreadable mapping file — fall back to the empty mapping.
            }
        }

        /// <summary>Writes the current mapping document to disk.</summary>
        public void Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                var document = new PixMwfMappingDocument
                {
                    Mappings = new Dictionary<string, string>(_mappings, StringComparer.OrdinalIgnoreCase)
                };
                File.WriteAllText(_filePath, JsonConvert.SerializeObject(document, Formatting.Indented), new UTF8Encoding(false));
            }
            catch
            {
                // Ignore write errors — the mapping simply stays in memory only.
            }
        }
    }
}
