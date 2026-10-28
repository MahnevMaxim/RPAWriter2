using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace RPAWriter2
{
    /// <summary>
    /// Separate tool window hosting the graphical checkpoint diff viewer.
    /// Opens in the document (MDI) area, next to the main editor.
    /// </summary>
    [Guid("8d2e1f4a-7c6b-4a9e-8b1d-3e5f7a9c1d2b")]
    public class DiffToolWindow : ToolWindowPane
    {
        /// <summary>Initializes a new instance of the <see cref="DiffToolWindow"/> class.</summary>
        public DiffToolWindow() : base(null)
        {
            Caption = "RPA Writer Diff";

            // Hosted user control; ToolWindowPane disposes the Content object itself.
            Content = new DiffToolWindowControl();
        }

        /// <summary>The hosted user control.</summary>
        public DiffToolWindowControl? Control => Content as DiffToolWindowControl;
    }
}
