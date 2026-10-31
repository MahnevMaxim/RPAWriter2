using RPAWriter.Models;
using RPAWriter.Mvvm;

namespace RPAWriter2.ViewModels
{
    /// <summary>
    /// View model of the chat tab: a thin adapter between <see cref="ChatModel"/> and the XAML.
    /// <para>
    /// It holds no business logic — commands are forwarded to the model and bindings read the model
    /// through <see cref="Chat"/>. Everything that decides or changes the conversation lives in the
    /// model so it can be tested headless (see <c>documentation/13-model-layer.md</c>).
    /// </para>
    /// </summary>
    public sealed class ChatViewModel : BaseViewModel
    {
        /// <summary>The chat model this view model adapts.</summary>
        public ChatModel Chat => Model as ChatModel;

        /// <summary>Hook for bindings that need a value before the view is loaded.</summary>
        public override void InitializeViewModel()
        {
            // Nothing to project yet: the XAML binds straight to the model's collections.
        }
    }
}
