using System.Windows.Controls;

namespace RPAWriter2.Controls
{
    /// <summary>
    /// Domain "Knowledge base" tab: import controls, progress bar and the messages transcript.
    /// The model/view-model are attached by the host; only the scrollable messages box is exposed
    /// so the shell can auto-scroll it when the model appends text.
    /// </summary>
    public partial class KnowledgeBaseControl : UserControl
    {
        public KnowledgeBaseControl()
        {
            InitializeComponent();
        }

        public TextBox KbMessagesBox => KbMessagesBoxControl;
    }
}
