using System;
using System.Collections.Generic;

namespace RPAWriter.Core.Models
{
    /// <summary>How a PIX argument value is converted when assigned to an MWF argument.</summary>
    public enum ConversionKind
    {
        /// <summary>Assign the value as-is.</summary>
        None = 0,

        /// <summary>Convert the value to a string with <c>.ToString()</c>.</summary>
        ToString = 1,

        /// <summary>Convert the value with <c>System.Convert.ChangeType</c>; the expression names the target type.</summary>
        Parse = 2,

        /// <summary>Parse a date/time with <c>System.DateTime.ParseExact</c>; the expression is the format string.</summary>
        DateTimeFormat = 3,

        /// <summary>Free-form C# expression template; <c>{0}</c> is the source value.</summary>
        Custom = 4
    }

    /// <summary>
    /// Maps one PIX activity argument (property) to one MWF activity argument, with an
    /// optional type conversion. Persisted together with the activity-level mapping so
    /// the converter can reuse the same argument map the mapping control edits.
    /// </summary>
    public class PixMwfArgumentMapping
    {
        /// <summary>PIX argument/property name.</summary>
        public string PixArgument { get; set; }

        /// <summary>MWF argument/property name, or null/empty to leave the argument unmapped.</summary>
        public string MwfArgument { get; set; }

        /// <summary>How the value is converted on assignment.</summary>
        public ConversionKind Conversion { get; set; } = ConversionKind.None;

        /// <summary>Optional format/expression used by <see cref="ConversionKind.Parse"/>, <see cref="ConversionKind.DateTimeFormat"/> or <see cref="ConversionKind.Custom"/>.</summary>
        public string ConversionExpression { get; set; }
    }

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

        /// <summary>PIX activity full name → its argument-level mappings.</summary>
        public Dictionary<string, List<PixMwfArgumentMapping>> ArgumentMappings { get; set; } =
            new Dictionary<string, List<PixMwfArgumentMapping>>(StringComparer.OrdinalIgnoreCase);
    }
}
