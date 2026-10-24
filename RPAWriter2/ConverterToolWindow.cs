using Microsoft.VisualStudio.Shell;
using System;
using System.Runtime.InteropServices;

namespace RPAWriter2
{
    /// <summary>
    /// Separate tool window hosting the robot converter (UiPath/PIX → WF, activity-library
    /// import and PIX → MWF mapping). Opens in the document (MDI) area.
    /// </summary>
    [Guid("5f8c2a6e-9b1d-4c3e-8a7f-0d2b6e4a1c9f")]
    public class ConverterToolWindow : ToolWindowPane
    {
        /// <summary>Initializes a new instance of the <see cref="ConverterToolWindow"/> class.</summary>
        public ConverterToolWindow() : base(null)
        {
            this.Caption = "RPA Writer Converter";

            // Hosted user control; ToolWindowPane disposes the Content object itself.
            this.Content = new ConverterToolWindowControl();
        }

        /// <summary>The hosted user control.</summary>
        public ConverterToolWindowControl? Control => this.Content as ConverterToolWindowControl;
    }
}
