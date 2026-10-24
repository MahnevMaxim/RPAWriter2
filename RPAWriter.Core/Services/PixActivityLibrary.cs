using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Loads PIX activity descriptors (JSON files, one per activity) from the activity
    /// library's "pix" folder.
    /// </summary>
    public class PixActivityLibrary
    {
        private readonly string _directory;

        public PixActivityLibrary() : this(ActivityLibraryPaths.ResolvePixActivitiesDirectory())
        {
        }

        public PixActivityLibrary(string directory)
        {
            _directory = directory ?? "";
        }

        /// <summary>
        /// Reads the descriptor <c>{activityFullName}.json</c>, or null when the activity
        /// is unknown or the file is unreadable.
        /// </summary>
        public PixActivityDescriptor Load(string activityFullName)
        {
            if (string.IsNullOrWhiteSpace(activityFullName) || string.IsNullOrWhiteSpace(_directory))
                return null;

            try
            {
                var path = Path.Combine(_directory, activityFullName + ".json");
                if (!File.Exists(path))
                    return null;

                var text = File.ReadAllText(path, Encoding.UTF8);
                return JsonConvert.DeserializeObject<PixActivityDescriptor>(text);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Returns the full names of all activities known to the library. Each descriptor
        /// is stored as <c>{activityFullName}.json</c>, so the known names are the file
        /// names without the extension. Comparison is case-insensitive.
        /// </summary>
        public ISet<string> GetKnownActivityNames()
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(_directory))
                return result;

            try
            {
                foreach (var file in Directory.EnumerateFiles(_directory, "*.json", SearchOption.TopDirectoryOnly))
                    result.Add(Path.GetFileNameWithoutExtension(file));
            }
            catch
            {
                // Unreadable directory — fall back to the empty set.
            }

            return result;
        }

        /// <summary>
        /// Writes (or overwrites) the descriptor JSON for a single activity so it becomes
        /// known to the library. Used to append activities discovered in a scanned PIX
        /// robot folder that are not yet part of the library. No-op for a null descriptor
        /// or a descriptor without an activity full name.
        /// </summary>
        public void EnsureDescriptor(PixActivityDescriptor descriptor)
        {
            if (descriptor == null || string.IsNullOrWhiteSpace(descriptor.ActivityFullName) ||
                string.IsNullOrWhiteSpace(_directory))
                return;

            try
            {
                Directory.CreateDirectory(_directory);
                var path = Path.Combine(_directory, descriptor.ActivityFullName.Trim() + ".json");
                var json = JsonConvert.SerializeObject(descriptor, Formatting.Indented,
                    new JsonSerializerSettings
                    {
                        ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
                    });
                File.WriteAllText(path, json, new UTF8Encoding(false));
            }
            catch
            {
                // The library may be read-only (e.g. deployed next to the VSIX); the activity
                // then remains available only through the in-memory mapping table rows.
            }
        }
    }
}
