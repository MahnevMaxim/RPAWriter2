using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Knowledge-base formation mode: reads robot documentation,
    /// decomposes it into small tasks, and locates a solution for each task in the robot code.
    /// </summary>
    public class KnowledgeBaseFormationService
    {
        private readonly IDocumentReader _reader;
        private readonly IAiProvider _ai;
        private readonly IKnowledgeBase _kb;

        public KnowledgeBaseFormationService(IDocumentReader reader, IAiProvider ai, IKnowledgeBase kb)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _ai = ai ?? throw new ArgumentNullException(nameof(ai));
            _kb = kb ?? throw new ArgumentNullException(nameof(kb));
        }

        public async Task<int> BuildFromRobotDocsAsync(
            IEnumerable<string> docPaths, string robotCode, CancellationToken cancellationToken = default)
        {
            if (docPaths == null) return 0;
            var added = 0;
            foreach (var path in docPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var spec = _reader.Read(path);
                var tasks = await _ai.DecomposeTasksAsync(spec.RawText, cancellationToken).ConfigureAwait(false);
                foreach (var task in tasks)
                {
                    var robotFiles = new List<RobotSourceFile>
                    {
                        new RobotSourceFile { RelativePath = "robot", Content = robotCode ?? "" }
                    };
                    var solution = await _ai.MapTaskToSolutionAsync(task, robotFiles, cancellationToken).ConfigureAwait(false);
                    solution.SourceRobot = spec.SourcePath;
                    solution.Documentation = spec.RawText;
                    await _kb.AddAsync(solution, cancellationToken).ConfigureAwait(false);
                    added++;
                }
            }
            return added;
        }
    }
}
