using RPAWriter.Mvvm;
using System;
using System.Collections.Generic;
using System.Windows;

namespace RPAWriter2.Mvvm
{
    /// <summary>
    /// WPF adapter over the UI-free MVVM framework (<see cref="RPAWriter.Mvvm"/>).
    /// This is the only place that knows about <see cref="FrameworkElement"/>, <see cref="Window"/>
    /// and the attach order; models and the framework assembly stay UI-free.
    /// <para>Rules: <c>documentation/13-model-layer.md</c>.</para>
    /// </summary>
    public static class MvvmFactory
    {
        /// <summary>
        /// Attaches a model to a window: runs the model, wires events, then shows or
        /// shows the window modally.
        /// </summary>
        public static void CreateWindow(BaseModel model, BaseViewModel viewModel, IView view, ViewMode viewMode, List<BaseModel>? childModels = null)
        {
            if (view is FrameworkElement element)
            {
                Attach(model, viewModel, element, view, childModels);
            }
            else
            {
                throw new ArgumentException("view must be a FrameworkElement", nameof(view));
            }

            if (!(view is Window))
                return;

            if (viewMode == ViewMode.Show)
            {
                view.Show();
            }

            if (viewMode == ViewMode.ShowDialog)
            {
                _ = view.ShowDialog();
            }
        }

        /// <summary>
        /// Attaches a model to any <see cref="FrameworkElement"/> (a tool window tab, a user control)
        /// without showing it. Safe to call with <paramref name="viewModel"/> = <c>null</c>:
        /// then the model itself becomes the data context.
        /// </summary>
        /// <param name="host">
        /// Optional <see cref="IView"/> that renders model messages. When <c>null</c>, messages
        /// raised by the model are ignored (headless mode).
        /// </param>
        public static void Attach(BaseModel model, BaseViewModel? viewModel, FrameworkElement view, IView? host = null, IEnumerable<BaseModel>? childModels = null)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));
            if (view == null)
                throw new ArgumentNullException(nameof(view));

            // Run() is UI-free by contract, so it goes first, before any view wiring.
            model.Run();

            if (viewModel != null)
            {
                viewModel.SetModel(model, host ?? viewModel.View);
                view.DataContext = viewModel;
            }
            else
            {
                view.DataContext = model;
            }

            model.ShowDialog += (sender, message) => host?.ShowDialogMessage(message);
            model.Notify += (sender, notify) => host?.NotifyMessage(notify);

            view.Loaded += (sender, e) => model.Loaded();
            view.Unloaded += (sender, e) => model.Closing();

            model.AfterLoaded();

            if (childModels != null)
            {
                foreach (var child in childModels)
                    RegisterModel(model, child);
            }
        }

        /// <summary>
        /// Attaches a child model to a parent model. Unlike the original EasyMvvm implementation
        /// this does not require the parent to have a view model yet: messages of the child are
        /// relayed to the parent and only the parent needs a view. This is what allows models to be
        /// created and registered lazily (and headless, in tests).
        /// </summary>
        public static void RegisterModel(BaseModel parentModel, BaseModel childModel)
        {
            if (parentModel == null)
                throw new ArgumentNullException(nameof(parentModel));
            if (childModel == null)
                throw new ArgumentNullException(nameof(childModel));

            childModel.ViewModel = parentModel.ViewModel;
            childModel.ShowDialog += (sender, message) => parentModel.ShowDialogMessage(message);
            childModel.Notify += (sender, notify) => parentModel.NotifyMessage(notify);
        }
    }
}
