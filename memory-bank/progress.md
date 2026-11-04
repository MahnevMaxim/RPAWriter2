# Progress

- Кнопка «Do it!!!» читает поле ввода: `DoItButton` переведён с `Command={Binding Cmd}` на `Click="OnDoItClick"` (соседние кнопки футера уже на Click); обработчик в `MainWindowControl.xaml.cs` читает `InputBox.Text.Trim()`, очищает поле и вызывает `ChatModel.DoItCommand(string)`. Модель: `SwitchMode(Act)` + отправка через `IChatPromptSink` промта `«<ввод>\nDo it!!!»` (пустой ввод → `Do it!!!`). `DoItCommand()` → `public async Task DoItCommand(string)` (+ `using System.Threading.Tasks;` в модели и тестах). Тесты: 6 `DoItCommand_*` (обновлены 4, добавлены 2), всего 895 зелёных. Документация обновлена в `02-chat.md` и `13-model-layer.md` (§17.1, §17.3, §20).

- Закрытие чата ≠ просмотр истории (ChatModel, §17.7 `documentation/13-model-layer.md`): источник истины «активна/закрыт» — только `ChatModel.ActiveChatConversation` (`null` = закрыт). Добавлены `CloseActiveChat()` (save → `ClearConversationState` → `null` → `ViewMode = History`) и `EnsureActiveConversation()` (идемпотентное создание беседы + очистка остатков закрытого чата); `ResetToFreshConversation` заменён на `ClearConversationState` без создания беседы; конструктор модели беседу больше не создаёт; `ResetConversation()` после delete/clear активной беседы оставляет `null`; `ResolveConversationToOpen("")` возвращает `null` (закрытый чат не воскресает). Хост: `OnCloseChat` закрывает беседу (cancel + `ApplyChatViewMode` + `ResetAgentStateAfterConversationRemoved`), `OnShowHistory` её не трогает, единая точка `EnsureChatReadyToReceive()` для «Send errors»/«Send to RPA Writer»/«Send exception to RPA Writer»/баннеров и `EnsureActiveConversation()` в `RunSingleTurnAsync` (убрано NRE на `_currentConversation.Title`); восстановление сессии с пустым `LastConversationId` показывает историю, а новый чат открывает только при пустой истории. Тесты: 81 в `ChatModelTests` (10 новых про закрытие/переоткрытие), всего 796.

- Футер чата на биндингах (фаза E): `ModeToggle.SelectedValue` — двусторонняя привязка к `Chat.Mode` через новый `AgentModeToToggleValueConverter` (имена режима — в Core `AgentModeNames`, покрыто `AgentModeNamesTests`); рамка `InputBox` — `DataTrigger` по `Chat.Mode` (плюс `BasedOn` неявного стиля `TextBox`); `CancelButton.Visibility` и `DoItButton.IsEnabled` — через `EnumMatchConverter` по `Chat.Phase`; `ResumeButton.Visibility` — через новую проекцию `ChatModel.CanResume` (`HasResumableRun` + `Phase == Idle`). Модель пишет запись «Mode: …» сама (`ChatModel.SwitchMode`), хост только персистит. Удалены `SetPhase`, `UpdateResumeButtonVisibility`, `ApplyModeBrush`, `AppendModeSwitch`, алиасы `_mode`/`_phase` и поле `_resumeAvailable`. Разметка прогнана на отдельном WPF-стенде (19 проверок, 0 ошибок биндинга). Правила — §20 `documentation/13-model-layer.md`; 786 тестов.

- ChatModel, инвариант «одна активная беседа»: единственное определение активности — свойство `ActiveChatConversation` (беседа активна, пока оно не `null`); `ActiveConversationId`, `HasActiveConversation` и `ChatConversation.IsActive` — только производные проекции и состояние не хранят; `IsActive` пишет исключительно `ChatModel.UpdateActiveFlags()`, в SQLite не пишется, View его не трогает. Контракт зафиксирован в XML-комментариях `ChatModel`/`ChatConversation`/`_currentConversation`/`Ellipse` и в §17.1, 17.4, 17.5 `documentation/13-model-layer.md`.

- Panels on bindings (фаза E): `ProdLogsPanel` получает `DataContext="{Binding Chat.LogAnalyzer}"`; на биндинги переведены `MinLevelCombo`, `FromDatePicker`/`ToDatePicker`, `RobotNameBox`, `ProviderCombo` (видимость — через новый `CountToVisibilityConverter`), `TasksGrid`, `LogsGrid`, `ProdLogsStatusText`. Удалены `InitializeLogAnalyzerPanel`, `SyncLogAnalyzerPanel`, `SyncRobotNameBox`, `ApplyFilterFromView`, `OnProviderChanged`; остался один мост «экран → модель» `PushPanelInputsToModel` (WPF поднимает `SelectionChanged`/`SelectedDateChanged` раньше двусторонней привязки). Значение по умолчанию поля робота переехало в модель (`LoadRobotNameHistory`); `ChatModel.LogAnalyzer` уведомляет об изменении и запускает модель, назначенную после `Run()`. Правила — §19 `documentation/13-model-layer.md`; 769 тестов.

- LogAnalyzer (фаза D): модель панели Analyze logs в `RPAWriter.Models/LogAnalyzer.cs` — провайдеры (`EnsureProviders`/`InvalidateProviders`), загрузка задач и логов, история имён роботов, фильтры уровня и дат, оба сценария анализа (Output window и production servers) через контракты `ILogProviderSource`/`IOutputWindowTextSource`/`IChatPromptSink`/`IUserMessageSink`/`ILogAnalysisSettings`; сервисы сценария в Core: `ProdLogAnalysisService`, `ProdLogQueryBuilder`, `RobotNameHistory`, `LogLevelOption`; адаптеры хоста в `RPAWriter2/Services/LogAnalysisAdapters.cs`; `ToolWindow1Control` делегирует в модель (удалены `EnsureLogProviders`, `LoadProdTasksAsync`, `AddRobotNameToHistory`, `ReloadSelectedTaskLogsAsync`, `BuildProdLogQuery`, `MapToLogEntry`, `BuildProdLogsPrompt`, `FormatTimestamp`, `FormatParameters`, `InitializeMinLevelCombo` и поля `_logProviders`/`_selectedLogProvider`/`_robotNameHistory`); +87 тестов (`LogAnalyzerTests`, `ProdLogAnalysisServiceTests`, `ProdLogQueryBuilderTests`, `RobotNameHistoryTests`, архитектурные стражи).

## What works
- Импорт и анализ ТЗ, декомпозиция на задачи.
- Генерация роботов (XAML + C#), база знаний, поиск похожих задач.
- UiPath → Workflow Foundation конвертация.
- Чат-агент с режимами Plan/Act, gating инструментов, историей (SQLite).
- Управление контекстным окном: transcript, оценка токенов, auto-compact, overflow/max-tokens recovery, «Compact now», прогресс-бар.
- Memory Bank: сервис + инструменты + кнопка.
- Tool calling: 2 режима — Native function calling и JSON over chat (настройка «Tool calling»).
- Слой моделей выделен в отдельные сборки: `RPAWriter.Mvvm` (каркас MVVM, UI-free) + `RPAWriter.Models` (`ChatModel`, `LogAnalyzer`), правила — `documentation/13-model-layer.md`, граница проверяется `ModelArchitectureTests`.
- Домен чата перенесён в `RPAWriter.Core/Models` (`ChatMessage`, `ChatPayload`-типы, `ChatAttachment`, `ChatConversation`, `ChatHistoryMessage`, `PendingMessage`, `UiStateData`); в Core объявлены контракты `IChatHistoryStore`/`IUiStateStore`, в `RPAWriter2/Services` — адаптеры `SqliteChatHistoryStore`/`JsonUiStateStore`; иконка очереди вынесена во View-конвертер.

- ChatModel (фаза C): состояние вкладки Chat (transcript, pending-очередь, вложения, история, активная беседа, pinned, view mode, mode, phase) живёт в `RPAWriter.Models/ChatModel.cs`; сценарии New/Open/Delete/Clear/Save/UI-state и очередь с приоритетом Steer покрыты 63 тестами `ChatModelTests`; маппинг транскрипта вынесен в `RPAWriter.Core/Services/ChatMessageMapper.cs` (14 тестов); зелёный индикатор активной беседы (`ChatConversation.IsActive`) добавлен в карточку истории; `ChatViewModel` — тонкий адаптер, подключение через `MvvmFactory.Attach`.

## What's left
- Валидация на реальном VS с реальным провайдером.
- Опционально: больше интеграционных тестов.

## Known issues (см. todo.md)
- `SolutionTemplateEngine` не подключён.
- Удалённые логи (`RemoteLogService`) не подключены.
- ActivityLog/Event Log не читаются.
- `Flowchart` не поддерживается (понижается до Sequence).
- `InvokeMethod → Result → переменная` не реализовано.
- C#-методы генерируются без параметров (`// TODO: implement`).
