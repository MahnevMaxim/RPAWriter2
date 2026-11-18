using Agent.Models;
using Agent.Mvvm;
using Agent.Rpa.AgentTools;
using Agent.Rpa.Contracts;
using Agent.Rpa.Models;
using Agent.Rpa.Services;

namespace RPAWriter2.ViewModels
{
    /// <summary>
    /// View model of the Knowledge base tab: a thin adapter between
    /// <see cref="KnowledgeBaseModel"/> and the XAML. It holds no business logic — commands are
    /// forwarded to the model and bindings read the model through <see cref="KnowledgeBase"/>.
    /// </summary>
    public sealed class KnowledgeBaseViewModel : BaseViewModel
    {
        /// <summary>The knowledge base model this view model adapts.</summary>
        public KnowledgeBaseModel KnowledgeBase => Model as KnowledgeBaseModel;
    }
}
