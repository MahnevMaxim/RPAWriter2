using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Removes the git environment variables that can redirect a git command away from
    /// the repository it is supposed to operate on (for example <c>GIT_DIR</c> or
    /// <c>GIT_WORK_TREE</c>). Both the checkpoint shadow repository and the main-solution
    /// commit control rely on this so a leaked environment variable can never make git
    /// read or write the wrong repository.
    /// </summary>
    public static class GitEnvironmentSanitizer
    {
        private static readonly string[] VariablesToRemove =
        {
            "GIT_DIR",
            "GIT_WORK_TREE",
            "GIT_INDEX_FILE",
            "GIT_OBJECT_DIRECTORY",
            "GIT_ALTERNATE_OBJECT_DIRECTORIES",
            "GIT_CEILING_DIRECTORIES",
            "GIT_TEMPLATE_DIR",
        };

        /// <summary>The git environment variables removed by <see cref="Sanitize"/>.</summary>
        public static IReadOnlyList<string> SanitizedVariables => VariablesToRemove;

        /// <summary>
        /// Git configuration forced onto every child git process so text output, changed
        /// paths and commit messages are always UTF-8: <c>core.quotepath=false</c> stops
        /// non-ASCII paths from being octal-escaped, <c>i18n.logOutputEncoding=UTF-8</c>
        /// forces UTF-8 command output and <c>i18n.commitEncoding=UTF-8</c> makes git store
        /// commit messages (fed via stdin) as UTF-8.
        /// </summary>
        public const string ForcedConfigArguments =
            "-c core.quotepath=false -c i18n.logOutputEncoding=UTF-8 -c i18n.commitEncoding=UTF-8";

        /// <summary>
        /// Prefixes the given git arguments with the forced UTF-8 configuration. Returns just
        /// the configuration when <paramref name="arguments"/> is null or whitespace.
        /// </summary>
        public static string WithForcedConfig(string arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments))
                return ForcedConfigArguments;

            return ForcedConfigArguments + " " + arguments;
        }

        /// <summary>Removes the redirecting git environment variables from the process start info.</summary>
        public static void Sanitize(ProcessStartInfo startInfo)
        {
            if (startInfo == null)
                throw new ArgumentNullException(nameof(startInfo));

            foreach (var name in VariablesToRemove)
            {
                startInfo.Environment.Remove(name);
            }
        }
    }
}
