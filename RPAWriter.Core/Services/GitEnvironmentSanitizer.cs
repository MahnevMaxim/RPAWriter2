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
