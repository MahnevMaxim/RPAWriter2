using System;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Resolves a Workflow Foundation activity type name (from the MWF activity
    /// library) to the canonical <see cref="ActivityNodeKind"/> understood by the
    /// converter/emitter. Returns null for custom activities that have no canonical
    /// snippet representation.
    /// </summary>
    public static class MwfActivityTypeMapper
    {
        public static ActivityNodeKind? Resolve(string mwfActivityFullName)
        {
            switch (ShortName(mwfActivityFullName))
            {
                case "sequence": return ActivityNodeKind.Sequence;
                case "if": return ActivityNodeKind.If;
                case "while": return ActivityNodeKind.While;
                case "foreach": return ActivityNodeKind.ForEach;
                case "assign": return ActivityNodeKind.Assign;
                case "invokemethod": return ActivityNodeKind.InvokeMethod;
                case "writeline": return ActivityNodeKind.WriteLine;
                case "delay": return ActivityNodeKind.Delay;
                case "trycatch": return ActivityNodeKind.TryCatch;
                case "throw": return ActivityNodeKind.Throw;
                case "return": return ActivityNodeKind.Return;
                default: return null;
            }
        }

        private static string ShortName(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
                return string.Empty;

            var index = fullName.LastIndexOf('.');
            var last = index < 0 ? fullName : fullName.Substring(index + 1);

            // Strip generic arity ("ForEach`1" → "ForEach").
            var tick = last.IndexOf('`');
            if (tick >= 0)
                last = last.Substring(0, tick);

            return last.Trim().ToLowerInvariant();
        }
    }
}
