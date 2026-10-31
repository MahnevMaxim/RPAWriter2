using RPAWriter.Mvvm;
using System;

namespace RPAWriter.Models
{
    /// <summary>
    /// Marker type that lets architecture tests locate this assembly
    /// (<c>ModelArchitectureTests</c> in RPAWriter.Tests).
    /// It also documents the boundary: everything in <c>RPAWriter.Models</c> must stay
    /// free of WPF, Material.Icons, SQLite, EnvDTE and other Visual Studio dependencies.
    /// </summary>
    public static class ModelsAssemblyMarker
    {
        /// <summary>
        /// Base type of every model. Exposed here so that the reference to
        /// <c>RPAWriter.Mvvm</c> stays explicit and cannot be pruned by the compiler
        /// while the list of models is still small.
        /// </summary>
        public static Type ModelBaseType => typeof(BaseModel);
    }
}
