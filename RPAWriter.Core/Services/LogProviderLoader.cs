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
        public static IReadOnlyList<IProdLogProvider> Discover(string directory)
        {
            var result = new List<IProdLogProvider>();
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return result;

            foreach (var file in Directory.GetFiles(directory, "*.dll", SearchOption.TopDirectoryOnly))
            {
                try
                {
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
