using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.Services
{
    /// <summary>On-disk knowledge base: a single JSON index in a directory.</summary>
    public class FileKnowledgeBase : IKnowledgeBase
    {
        private readonly string _indexFile;
        private readonly SimilarityIndex _similarity = new SimilarityIndex();
        private readonly object _gate = new object();

        public FileKnowledgeBase(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentNullException(nameof(directory));
            Directory.CreateDirectory(directory);
            _indexFile = Path.Combine(directory, "index.json");
        }

        public Task AddAsync(SolutionRecord record, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                var all = Load().ToList();
                if (string.IsNullOrWhiteSpace(record.Id))
                    record.Id = Guid.NewGuid().ToString("N");
                var existing = all.FirstOrDefault(r => string.Equals(r.Id, record.Id, StringComparison.OrdinalIgnoreCase));
                if (existing != null) all.Remove(existing);
                all.Add(record);
                Save(all);
            }
            return Task.CompletedTask;
        }

        public Task<int> RemoveWhereAsync(Func<SolutionRecord, bool> predicate, CancellationToken cancellationToken = default)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                var all = Load().ToList();
                var remaining = new List<SolutionRecord>(all.Count);
                var removed = 0;
                foreach (var record in all)
                {
                    if (predicate(record)) removed++;
                    else remaining.Add(record);
                }
                if (removed > 0) Save(remaining);
                return Task.FromResult(removed);
            }
        }

        public Task<IReadOnlyList<SolutionRecord>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                return Task.FromResult<IReadOnlyList<SolutionRecord>>(Load().ToList());
            }
        }

        public Task<IReadOnlyList<SimilarTaskMatch>> FindSimilarTasksAsync(
            TaskDescriptor query, int topK, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                var records = Load().ToList();
                var matches = _similarity.FindSimilar(query, records, topK);
                return Task.FromResult<IReadOnlyList<SimilarTaskMatch>>(matches.ToList());
            }
        }

        public Task<SolutionRecord> GetSolutionAsync(string id, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                var found = Load().FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));
                return Task.FromResult(found);
            }
        }

        private IEnumerable<SolutionRecord> Load()
        {
            if (!File.Exists(_indexFile)) return Enumerable.Empty<SolutionRecord>();
            var json = File.ReadAllText(_indexFile);
            if (string.IsNullOrWhiteSpace(json)) return Enumerable.Empty<SolutionRecord>();
            return JsonConvert.DeserializeObject<List<SolutionRecord>>(json) ?? new List<SolutionRecord>();
        }

        private void Save(IEnumerable<SolutionRecord> records)
        {
            var json = JsonConvert.SerializeObject(records, Formatting.Indented);
            File.WriteAllText(_indexFile, json);
        }
    }
}
