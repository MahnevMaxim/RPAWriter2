using System;
using System.Collections.Generic;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;

namespace RPAWriter2.Services
{
    /// <summary>
    /// Applies the Visual Studio "Environment → Documents" options that the extension
    /// requires, so they do not have to be configured manually on every machine:
    /// - "Reload modified files unless there are unsaved changes" = true
    /// - "Check for consistent line endings on load" = false (when available; the
    ///   option was removed from the EnvDTE object model in Visual Studio 2026)
    /// Options that could not be applied are reported as human-readable warnings;
    /// options that are simply absent on the current Visual Studio version are skipped.
    /// </summary>
    public static class VsOptionsService
    {
        private static readonly object Gate = new object();
        private static IReadOnlyList<string> _lastWarnings = Array.Empty<string>();

        /// <summary>Human-readable warnings from the last <see cref="ApplyRequiredDocumentOptions"/> run.</summary>
        public static IReadOnlyList<string> LastWarnings
        {
            get { lock (Gate) { return _lastWarnings; } }
        }

        /// <summary>Must be called on the UI thread.</summary>
        public static void ApplyRequiredDocumentOptions(DTE2 dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var warnings = new List<string>();
            if (dte == null)
            {
                warnings.Add("Visual Studio (DTE) is not available.");
                Store(warnings);
                return;
            }

            try
            {
                var documents = dte.Properties["Environment", "Documents"];
                if (documents == null)
                {
                    warnings.Add("Visual Studio \"Environment → Documents\" options are not available.");
                }
                else
                {
                    SetBoolean(documents, "AutoloadExternalChanges", true,
                        "Reload modified files unless there are unsaved changes", warnings,
                        missingIsWarning: true);
                    // "CheckLineEndingsOnLoad" was removed from the EnvDTE object model
                    // in Visual Studio 2026 (Microsoft.VisualStudio.Interop v17.14), so its
                    // absence is expected and is logged as info rather than surfaced as a warning.
                    SetBoolean(documents, "CheckLineEndingsOnLoad", false,
                        "Check for consistent line endings on load", warnings,
                        missingIsWarning: false);
                }
            }
            catch (Exception ex)
            {
                warnings.Add("Could not read Visual Studio \"Environment → Documents\" options: " + ex.Message);
            }

            Store(warnings);
            foreach (var warning in warnings)
                LogService.Warn("VsOptionsService: " + warning);
        }

        private static void SetBoolean(Properties properties, string name, bool expected, string displayName, List<string> warnings, bool missingIsWarning)
        {
            try
            {
                var prop = FindProperty(properties, name);
                if (prop == null)
                {
                    var message = "Visual Studio option \"" + displayName + "\" is not available in this version of Visual Studio.";
                    if (missingIsWarning)
                        warnings.Add(message);
                    else
                        LogService.Info("VsOptionsService: " + message);
                    return;
                }

                if (!ValueEquals(prop.Value, expected))
                    prop.Value = expected;

                if (!ValueEquals(prop.Value, expected))
                {
                    warnings.Add("Visual Studio option \"" + displayName +
                        "\" could not be set to " + expected + " (Tools → Options → Environment → Documents).");
                }
            }
            catch (Exception ex)
            {
                warnings.Add("Could not set Visual Studio option \"" + displayName + "\": " + ex.Message);
            }
        }

        private static Property FindProperty(Properties properties, string name)
        {
            foreach (Property candidate in properties)
            {
                if (string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }
            return null;
        }

        private static bool ValueEquals(object value, bool expected)
        {
            if (value == null)
                return false;
            if (value is bool b)
                return b == expected;
            return bool.TryParse(value.ToString(), out var parsed) && parsed == expected;
        }

        private static void Store(IReadOnlyList<string> warnings)
        {
            lock (Gate) { _lastWarnings = warnings; }
        }
    }
}
