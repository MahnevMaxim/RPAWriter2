using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.Services
{
    /// <summary>Parametrizes an analogous solution against a new spec ({{key}} substitution).</summary>
    public class SolutionTemplateEngine
    {
        private static readonly Regex Placeholder =
            new Regex(@"\{\{\s*(?<key>[a-zA-Z0-9_.-]+)\s*\}\}", RegexOptions.Compiled);

        public SolutionRecord Apply(SolutionRecord template, IReadOnlyDictionary<string, string> parameters)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            var result = Clone(template);
            if (parameters == null || parameters.Count == 0) return result;

            result.XamlFragment = Replace(result.XamlFragment, parameters);
            result.CSharpCode = Replace(result.CSharpCode, parameters);
            return result;
        }

        private static string Replace(string text, IReadOnlyDictionary<string, string> parameters)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return Placeholder.Replace(text, m =>
            {
                string value;
                return parameters.TryGetValue(m.Groups["key"].Value, out value) ? value : m.Value;
            });
        }

        public SolutionRecord Clone(SolutionRecord source)
        {
            var result = new SolutionRecord
            {
                Id = source.Id,
                Name = source.Name,
                XamlFragment = source.XamlFragment,
                CSharpCode = source.CSharpCode,
                SourceRobot = source.SourceRobot,
                Documentation = source.Documentation,
                CreatedUtc = source.CreatedUtc
            };
            foreach (var t in source.Tasks ?? new List<TaskDescriptor>())
                result.Tasks.Add(CloneTask(t));
            return result;
        }

        private static TaskDescriptor CloneTask(TaskDescriptor source)
        {
            var t = new TaskDescriptor
            {
                Action = source.Action,
                Target = source.Target,
                Description = source.Description,
                ActivityType = source.ActivityType
            };
            if (source.Inputs != null) t.Inputs.AddRange(source.Inputs);
            if (source.Outputs != null) t.Outputs.AddRange(source.Outputs);
            if (source.Keywords != null) t.Keywords.AddRange(source.Keywords);
            return t;
        }
    }
}
