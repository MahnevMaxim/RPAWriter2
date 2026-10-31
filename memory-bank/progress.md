# Progress

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
