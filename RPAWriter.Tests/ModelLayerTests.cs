using NUnit.Framework;
using RPAWriter.Mvvm;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RPAWriter.Tests
{
    /// <summary>
    /// Contract tests for the UI-free MVVM framework.
    /// They intentionally never touch WPF: the whole point is that a model can be created,
    /// driven and asserted without a view model and without a view.
    /// Rules: documentation/13-model-layer.md.
    /// </summary>
    [TestFixture]
    public class ModelLayerTests
    {
        #region Test doubles

        private sealed class SampleModel : BaseModel
        {
            private string _title = "";

            public List<string> Lifecycle { get; } = new List<string>();
            public int Counter { get; private set; }
            public string LastArgument { get; private set; }

            public string Title
            {
                get => _title;
                set => SetProperty(ref _title, value);
            }

            public override void Run() => Lifecycle.Add(nameof(Run));
            public override void AfterLoaded() => Lifecycle.Add(nameof(AfterLoaded));
            public override void Loaded() => Lifecycle.Add(nameof(Loaded));
            public override void Closing() => Lifecycle.Add(nameof(Closing));

            public void Increment() => Counter++;

            public void Add(int amount) => Counter += amount;

            public void Store(string value) => LastArgument = value;

            public void RaisePropertyChanged() => OnPropertyChanged(nameof(Counter));
        }

        private sealed class SampleViewModel : BaseViewModel
        {
            public bool Initialized { get; private set; }

            public override void InitializeViewModel() => Initialized = true;
        }

        private sealed class FakeView : IView
        {
            public List<string> Dialogs { get; } = new List<string>();
            public List<Notify> Notifications { get; } = new List<Notify>();

            public bool? ShowDialog() => true;

            public void Show() { }

            public void NotifyMessage(Notify notify) => Notifications.Add(notify);

            public bool? ShowDialogMessage(string message)
            {
                Dialogs.Add(message);
                return true;
            }
        }

        #endregion

        #region Rule 1: the model starts without view model and view

        [Test]
        public void Model_CanBeConstructedWithoutViewModelAndView()
        {
            var model = new SampleModel();

            Assert.That(model.ViewModel, Is.Null);
        }

        [Test]
        public void Model_LifecycleRunsWithoutViewModelAndView()
        {
            var model = new SampleModel();

            Assert.DoesNotThrow(() =>
            {
                model.Run();
                model.AfterLoaded();
                model.Loaded();
                model.Closing();
            });

            Assert.That(model.Lifecycle, Is.EqualTo(new[] { "Run", "AfterLoaded", "Loaded", "Closing" }));
        }

        [Test]
        public void Model_ExposesBusinessLogicAsPlainCallableMethods()
        {
            var model = new SampleModel();

            model.Increment();
            model.Increment();
            model.Add(5);

            Assert.That(model.Counter, Is.EqualTo(7));
        }

        #endregion

        #region Rule 3: state is a property with change notification

        [Test]
        public void Model_RaisesPropertyChanged_OnStateChange()
        {
            var model = new SampleModel();
            var changed = new List<string>();
            model.PropertyChanged += (sender, e) => changed.Add(e.PropertyName);

            model.Title = "first";
            model.Title = "first";   // no change -> no notification
            model.Title = "second";

            Assert.That(changed, Is.EqualTo(new[] { nameof(SampleModel.Title), nameof(SampleModel.Title) }));
            Assert.That(model.Title, Is.EqualTo("second"));
        }

        [Test]
        public void Model_ForwardsPropertyChangedToAttachedViewModel()
        {
            var model = new SampleModel();
            var viewModel = new SampleViewModel();
            var changed = new List<string>();
            viewModel.PropertyChanged += (sender, e) => changed.Add(e.PropertyName);

            // A view model can be attached without any view: View is optional.
            viewModel.SetModel(model, null);
            model.RaisePropertyChanged();

            Assert.That(viewModel.Model, Is.SameAs(model));
            Assert.That(viewModel.View, Is.Null);
            Assert.That(changed, Does.Contain(nameof(SampleModel.Counter)));
        }

        #endregion

        #region Rule 4: commands are public methods without overloads

        [Test]
        public void SendCommand_ByName_InvokesPublicMethod()
        {
            var model = new SampleModel();

            model.SendCommand(nameof(SampleModel.Increment));

            Assert.That(model.Counter, Is.EqualTo(1));
        }

        [Test]
        public void SendCommand_WithArgument_InvokesPublicMethodWithParameter()
        {
            var model = new SampleModel();

            model.SendCommand(new List<object> { nameof(SampleModel.Add), 41 });

            Assert.That(model.Counter, Is.EqualTo(41));
        }

        [Test]
        public void SendCommand_WithTwoArguments_InvokesPublicMethodWithTwoParameters()
        {
            var model = new SampleModel();
            var twoArgumentModel = new TwoArgumentModel();

            twoArgumentModel.SendCommand(new List<object> { nameof(TwoArgumentModel.Combine), "a", "b" });

            Assert.That(twoArgumentModel.Result, Is.EqualTo("ab"));
            Assert.That(model.Counter, Is.Zero);
        }

        [Test]
        public void SendCommand_UnknownMethod_IsIgnored()
        {
            var model = new SampleModel();

            Assert.DoesNotThrow(() => model.SendCommand("NoSuchMethod"));
        }

        [Test]
        public void ViewModelCommand_ForwardsToModel()
        {
            var model = new SampleModel();
            var viewModel = new SampleViewModel();
            viewModel.SetModel(model, null);

            viewModel.Cmd.Execute(nameof(SampleModel.Increment));

            Assert.That(viewModel.Initialized, Is.True);
            Assert.That(model.Counter, Is.EqualTo(1));
        }

        [Test]
        public void ViewModelCommand_WithoutModel_DoesNotThrow()
        {
            var viewModel = new SampleViewModel();

            Assert.DoesNotThrow(() => viewModel.Cmd.Execute("anything"));
        }

        #endregion

        #region Rule 5: no UI in the model - messages are events

        [Test]
        public void ShowDialogMessage_RaisesShowDialogEvent()
        {
            var model = new SampleModel();
            var messages = new List<string>();
            model.ShowDialog += (sender, message) => messages.Add(message);

            model.ShowDialogMessage("Saved");

            Assert.That(messages, Is.EqualTo(new[] { "Saved" }));
        }

        [Test]
        public void NotifyMessage_RaisesNotifyEvent()
        {
            var model = new SampleModel();
            var notifications = new List<Notify>();
            model.Notify += (sender, notify) => notifications.Add(notify);

            model.NotifyMessage(new Notify { Message = "Done", NotifyLevel = NotifyLevel.Success });

            Assert.That(notifications, Has.Count.EqualTo(1));
            Assert.That(notifications[0].Message, Is.EqualTo("Done"));
            Assert.That(notifications[0].NotifyLevel, Is.EqualTo(NotifyLevel.Success));
        }

        [Test]
        public void Messages_WithoutSubscribers_AreIgnored()
        {
            var model = new SampleModel();

            Assert.DoesNotThrow(() =>
            {
                model.ShowDialogMessage("nobody listens");
                model.NotifyMessage(new Notify { Message = "nobody listens" });
            });
        }

        #endregion

        #region RelayCommand

        [Test]
        public void RelayCommand_ExecutesAction()
        {
            var executed = 0;
            var command = new RelayCommand(_ => executed++);

            command.Execute(null);

            Assert.That(executed, Is.EqualTo(1));
            Assert.That(command.CanExecute(null), Is.True);
        }

        [Test]
        public void RelayCommand_RespectsCanExecute()
        {
            var command = new RelayCommand(_ => { }, _ => false);

            Assert.That(command.CanExecute(null), Is.False);
        }

        [Test]
        public void RelayCommand_RaisesCanExecuteChanged()
        {
            var command = new RelayCommand(_ => { });
            var raised = 0;
            command.CanExecuteChanged += (sender, e) => raised++;

            command.RaiseCanExecuteChanged();

            Assert.That(raised, Is.EqualTo(1));
        }

        [Test]
        public void RelayCommand_RequiresExecuteAction()
        {
            Assert.Throws<ArgumentNullException>(() => new RelayCommand(null));
        }

        #endregion

        private sealed class TwoArgumentModel : BaseModel
        {
            public string Result { get; private set; }

            public void Combine(string first, string second) => Result = first + second;
        }
    }
}
