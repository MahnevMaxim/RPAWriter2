using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using RPAWriter.Core.Contracts;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Discovers third-party <see cref="IProdLogProvider"/> implementations by scanning
    /// a directory of DLLs and instantiating every exported concrete type that has a
    /// public parameterless constructor.
    /// </summary>
    public static class LogProviderLoader
    {
        private static readonly string CoreAssemblyFullName = typeof(IProdLogProvider).Assembly.FullName;

        public static IReadOnlyList<IProdLogProvider> Discover(string directory)
        {
            var result = new List<IProdLogProvider>();
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return result;

            foreach (var file in Directory.GetFiles(directory, "*.dll", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    // Never load a second copy of RPAWriter.Core: providers reference the
                    // same IProdLogProvider type, and loading another copy of Core would
                    // break IsAssignableFrom via a type-identity mismatch.
                    if (string.Equals(AssemblyName.GetAssemblyName(file).FullName, CoreAssemblyFullName, StringComparison.Ordinal))
                        continue;

                    var assembly = Assembly.LoadFrom(file);
                    foreach (var type in assembly.GetExportedTypes())
                    {
                        if (type.IsAbstract || type.IsInterface) continue;
                        if (!typeof(IProdLogProvider).IsAssignableFrom(type)) continue;
                        if (type.GetConstructor(Type.EmptyTypes) == null) continue;

                        if (Activator.CreateInstance(type) is IProdLogProvider provider)
                            result.Add(provider);
                    }
                }
                catch
                {
                    // A broken or unrelated DLL must never break provider discovery.
                }
            }

            return result;
        }
    }
}
