using System.Collections.Generic;
using System.Windows.Controls;
using Agent.Core.Checkpoints;
using Agent.Rpa.AgentTools;
using Agent.Rpa.Contracts;
using Agent.Rpa.Models;
using Agent.Rpa.Services;

namespace RPAWriter2
{
    /// <summary>Content of the <see cref="DiffToolWindow"/>: a thin wrapper over <see cref="Controls.DiffViewerControl"/>.</summary>
    public partial class DiffToolWindowControl : UserControl
    {
        public DiffToolWindowControl()
        {
            InitializeComponent();
        }

        /// <summary>Displays the given diff files under the given title.</summary>
        public void ShowDiff(IReadOnlyList<DiffFileModel> files, string title)
        {
            DiffViewer.SetDiff(files, title);
        }
    }
}
