using System.Collections.Generic;

namespace RPAWriter.Roslyn
{
    /// <summary>A single node in a source file outline: namespace, type or member.</summary>
    public sealed class CodeOutlineEntry
    {
        /// <summary>Human-readable node kind: namespace, class, interface, method, property, field, ...</summary>
        public string Kind { get; set; }

        /// <summary>Short identifier of the node (type/member name).</summary>
        public string Name { get; set; }

        /// <summary>Full signature produced by Roslyn (or a one-line syntax fallback), without method bodies.</summary>
        public string Signature { get; set; }

        /// <summary>One-based start line of the whole declaration.</summary>
        public int StartLine { get; set; }

        /// <summary>One-based end line of the whole declaration.</summary>
        public int EndLine { get; set; }

        /// <summary>Nested members.</summary>
        public List<CodeOutlineEntry> Children { get; set; } = new List<CodeOutlineEntry>();
    }

    /// <summary>One compiler-verified reference to a symbol.</summary>
    public sealed class SymbolUsage
    {
        public string FilePath { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
        public string CodeLine { get; set; }
    }

    /// <summary>Inheritance / interface implementation tree of a named type.</summary>
    public sealed class InheritanceInfo
    {
        public string Symbol { get; set; }
        public string Kind { get; set; }
        public List<string> BaseTypes { get; set; } = new List<string>();
        public List<string> Interfaces { get; set; } = new List<string>();
        public List<string> DerivedTypes { get; set; } = new List<string>();
        public List<string> Implementations { get; set; } = new List<string>();
    }

    /// <summary>A single Roslyn background diagnostic (warning or higher), without running a build.</summary>
    public sealed class CodeDiagnostic
    {
        public string Severity { get; set; }
        public string Id { get; set; }
        public string Message { get; set; }
        public string FilePath { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
    }
}
