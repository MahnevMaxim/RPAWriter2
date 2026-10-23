using System;
using System.Collections.Generic;

namespace RPAWriter.Core.Models
{
    /// <summary>
    /// A user/AI-managed mapping from a PIX activity (full name) to a Workflow
    /// Foundation (MWF) activity (full CLR type name). Persisted as JSON so the
    /// converter can reuse the same mapping the mapping control edits.
    /// </summary>
    public class PixMwfMappingDocument
    {
        /// <summary>PIX activity full name → MWF activity full name.</summary>
        public Dictionary<string, string> Mappings { get; set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}
