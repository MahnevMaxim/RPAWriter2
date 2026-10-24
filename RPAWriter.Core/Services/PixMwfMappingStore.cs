using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private readonly Dictionary<string, List<PixMwfArgumentMapping>> _argumentMappings =
            new Dictionary<string, List<PixMwfArgumentMapping>>(StringComparer.OrdinalIgnoreCase);

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

        /// <summary>Copy of all activity-level mappings (PIX → MWF).</summary>
        public IReadOnlyDictionary<string, string> GetAll()
            => new Dictionary<string, string>(_mappings, StringComparer.OrdinalIgnoreCase);

        /// <summary>Argument mappings stored for a PIX activity (a copy; never null).</summary>
        public IReadOnlyList<PixMwfArgumentMapping> GetArgumentMappings(string pixActivityFullName)
        {
            if (string.IsNullOrWhiteSpace(pixActivityFullName))
                return new List<PixMwfArgumentMapping>();

            List<PixMwfArgumentMapping> list;
            return _argumentMappings.TryGetValue(pixActivityFullName.Trim(), out list)
                ? new List<PixMwfArgumentMapping>(list)
                : new List<PixMwfArgumentMapping>();
        }

        /// <summary>
        /// Sets (or, when <paramref name="mwfArgument"/> is empty, removes) the mapping for one
        /// PIX argument of a PIX activity, together with an optional conversion.
        /// </summary>
        public void SetArgumentMapping(
            string pixActivityFullName,
            string pixArgument,
            string mwfArgument,
            ConversionKind conversion = ConversionKind.None,
            string conversionExpression = null)
        {
            if (string.IsNullOrWhiteSpace(pixActivityFullName) || string.IsNullOrWhiteSpace(pixArgument))
                return;

            var key = pixActivityFullName.Trim();
            List<PixMwfArgumentMapping> list;
            if (!_argumentMappings.TryGetValue(key, out list))
            {
                list = new List<PixMwfArgumentMapping>();
                _argumentMappings[key] = list;
            }

            var existing = list.FirstOrDefault(m =>
                string.Equals(m.PixArgument, pixArgument, StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrWhiteSpace(mwfArgument))
            {
                if (existing != null)
                    list.Remove(existing);
                if (list.Count == 0)
                    _argumentMappings.Remove(key);
                return;
            }

            if (existing == null)
            {
                existing = new PixMwfArgumentMapping { PixArgument = pixArgument.Trim() };
                list.Add(existing);
            }

            existing.MwfArgument = mwfArgument.Trim();
            existing.Conversion = conversion;
            existing.ConversionExpression = conversionExpression;
        }

        /// <summary>Removes the argument mapping for a single PIX argument, if any.</summary>
        public void RemoveArgumentMapping(string pixActivityFullName, string pixArgument)
        {
            SetArgumentMapping(pixActivityFullName, pixArgument, null);
        }

        /// <summary>Removes all argument mappings for a PIX activity.</summary>
        public void ClearArgumentMappings(string pixActivityFullName)
        {
            if (string.IsNullOrWhiteSpace(pixActivityFullName))
                return;

            _argumentMappings.Remove(pixActivityFullName.Trim());
        }

        /// <summary>Reloads the mapping document from disk, discarding unsaved changes.</summary>
        public void Load()
        {
            _mappings.Clear();
            _argumentMappings.Clear();
            try
            {
                if (!File.Exists(_filePath))
                    return;

                var json = File.ReadAllText(_filePath, Encoding.UTF8);
                var document = JsonConvert.DeserializeObject<PixMwfMappingDocument>(json);
                if (document == null)
                    return;

                if (document.Mappings != null)
                {
                    foreach (var pair in document.Mappings)
                        if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                            _mappings[pair.Key.Trim()] = pair.Value.Trim();
                }

                if (document.ArgumentMappings != null)
                {
                    foreach (var pair in document.ArgumentMappings)
                    {
                        if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null)
                            continue;

                        var list = pair.Value
                            .Where(m => m != null && !string.IsNullOrWhiteSpace(m.PixArgument))
                            .ToList();
                        if (list.Count > 0)
                            _argumentMappings[pair.Key.Trim()] = list;
                    }
                }
            }
            catch
            {
                // Unreadable mapping file — fall back to the empty mapping.
            }
        }

        /// <summary>Writes the current mapping document to the store's default file path.</summary>
        public void Save()
            => SaveAs(_filePath);

        /// <summary>
        /// Writes the current mapping document to an arbitrary file path (used by "Save As"
        /// so the user can choose the file name). The store keeps writing to its default
        /// path on subsequent <see cref="Save"/> calls.
        /// </summary>
        public void SaveAs(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return;

            try
            {
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                var document = new PixMwfMappingDocument
                {
                    Mappings = new Dictionary<string, string>(_mappings, StringComparer.OrdinalIgnoreCase),
                    ArgumentMappings = new Dictionary<string, List<PixMwfArgumentMapping>>(_argumentMappings, StringComparer.OrdinalIgnoreCase)
                };
                File.WriteAllText(filePath, JsonConvert.SerializeObject(document, Formatting.Indented), new UTF8Encoding(false));
            }
            catch
            {
                // Ignore write errors — the mapping simply stays in memory only.
            }
        }
    }
}
