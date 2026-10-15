using System;
using System.Threading;
using System.Threading.Tasks;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.Services
{
    /// <summary>Orchestrates the end-to-end robot generation pipeline.</summary>
    public class RpaPipelineService
    {
        private readonly IDocumentReader _reader;
        private readonly IAiProvider _ai;
        private readonly IKnowledgeBase _kb;
        private readonly IRobotGenerator _generator;

        public RpaPipelineService(IDocumentReader reader, IAiProvider ai, IKnowledgeBase kb, IRobotGenerator generator)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _ai = ai ?? throw new ArgumentNullException(nameof(ai));
            _kb = kb ?? throw new ArgumentNullException(nameof(kb));
            _generator = generator ?? throw new ArgumentNullException(nameof(generator));
        }

        public async Task<PipelineResult> GenerateRobotFromSpecAsync(
            string specPath, CancellationToken cancellationToken = default)
        {
            var spec = _reader.Read(specPath);
            var analysis = await _ai.AnalyzeSpecAsync(spec, cancellationToken).ConfigureAwait(false);

            foreach (var task in analysis.Tasks)
            {
                var similar = await _kb.FindSimilarTasksAsync(task, 3, cancellationToken).ConfigureAwait(false);
                analysis.ReusedSolutions.AddRange(similar);
            }

            var plan = await _ai.GenerateRobotAsync(analysis, cancellationToken).ConfigureAwait(false);
            var xaml = _generator.EmitWorkflowXaml(plan);
            var code = _generator.EmitHelperCode(plan);

            return new PipelineResult
            {
                Spec = spec,
                Analysis = analysis,
                Plan = plan,
                Xaml = xaml,
                CSharpCode = code,
                Reused = analysis.ReusedSolutions
            };
        }
    }
}
