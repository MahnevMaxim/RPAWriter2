# 14. План: MVVM-рефакторинг вкладок + разделение рабочих пространств

> Статус: **реализовано** (Фаза A — MVVM-рефакторинг вкладок, Фаза B — разделение рабочих пространств). Документ остаётся как запись принятых решений.

## 0. Цель

1. Вынести бизнес-логику вкладок **Spec**, **Knowledge base** и **Settings** из code-behind
   (`MainWindowControl.xaml.cs`) в модели (`RPAWriter.Models`), данные — только через биндинг.
2. Ввести разделение **рабочих пространств** (по открытому решению Visual Studio): настройки,
   история чата и UI-состояние хранятся отдельно для каждого workspace, с global-фолбэком.

Порядок обязательный: сначала MVVM-рефакторинг (Фаза A), затем workspace-разделение (Фаза B),
потому что скоупить настройки/историю по workspace можно только после того, как вкладки станут
моделями с инжектированными хранилищами.

---

## 1. Текущее состояние

### 1.1 Сборки (из `documentation/13-model-layer.md`)

| Сборка | TFM | Роль |
|---|---|---|
| `RPAWriter.Core` | netstandard2.0 | домен, сервисы, контракты (`ChatMessage`, `SpecModel`, `SettingsData`, `SettingsStore`, `AppDataPaths`, `IChatHistoryStore`, `IUiStateStore`, …) |
| `RPAWriter.Mvvm` | netstandard2.0 | `BaseModel`, `BaseViewModel`, `BaseEntity`, `IView`, `RelayCommand`, `Notify` |
| `RPAWriter.Models` | netstandard2.0 | модели продукта: `ChatModel`, `LogAnalyzer` |
| `RPAWriter2` | net472 (VSIX) | View + code-behind + VS-сервисы + SQLite |
| `RPAWriter.Tests` | net8.0 (NUnit) | тесты Core / Mvvm / Models |

Правила моделей (обязательны): модель стартует без ViewModel/View; без WPF/DTE/SQLite/`Dispatcher`;
всё внешнее — интерфейсы в конструкторе; тест `…_CanBeConstructedWithoutViewModelAndView`;
покрытие ≥90 %; команды — `public`-методы без перегрузок через `Command="{Binding Cmd}"` + `BaseModel.SendCommand`.

### 1.2 Текущее хранение (всё **глобальное**, только по версии VS 17/18)

| Что | Файл | Путь |
|---|---|---|
| Настройки | `settings.json` | `%AppData%\RPAWriter2\<17\|18>\settings.json` (`SettingsStore`) |
| История чата | `chat_history.db` (SQLite) | там же (`ChatHistoryStore` → `SqliteChatHistoryStore`) |
| UI-состояние | `ui_state.json` | там же (`UiStateStore` → `JsonUiStateStore`) |

`AppDataPaths` уже централизует пути и умеет `InitializeScope(vsVersion)` + `MigrateLegacyData()`.

### 1.3 Текущая реализация вкладок

`MainWindowControl.xaml.cs` — ~4955 строк, почти вся логика в code-behind:
- `ChatModel` + `LogAnalyzer` — уже модели (MVVM готово);
- **Spec**, **Knowledge base**, **Settings** (и Packages) — целиком в code-behind: чтение/запись
  напрямую в контролы (`.Text`, `.IsChecked`, `.SelectedIndex`), обработчики `OnImportSpec`,
  `OnGenerateAlgorithm`, `OnGenerateRobot`, `OnFindSimilar`, `OnRobotSecurityAudit`,
  `OnFormKnowledgeBase`, `OnPause/OnStop/OnRefreshKnowledgeBase`, `LoadSettings`, `OnSaveSettings`.
- Состояние размазано по полям: `_settings`, `_currentSpec`, `_specMessages`, `_kbImportCts`,
  `_kbImportPaused`, `_securityAuditCts`, `_packageCatalog`, `_packages` и т.п.
- Settings-вкладка: 4 под-вкладки (API/RPA/General/Sound), ~40 контролов с `x:Name`.

Конфликт имён: в Core уже есть данные-класс `SpecModel` (нормализованное ТЗ). Новый класс-модель
вкладки называем **`SpecTabModel`**, чтобы не конфликтовать.

---

## 2. Как это устроено у Cline и Roo-Code

Оба — VS Code-расширения, используют модель **двух уровней**:

| Уровень | Механизм VS Code | Что хранят |
|---|---|---|
| Глобальный (пользователь/машина) | `ExtensionContext.globalState` (Memento) | глобальные настройки, профили провайдеров, история задач (у Cline — с полем `cwd`) |
| Глобальные секреты | `ExtensionContext.secrets` (SecretStorage, OS keychain) | API-ключи |
| Per-workspace | `ExtensionContext.workspaceState` (Memento) + `storageUri` | состояние/UI конкретного workspace |
| Проектные файлы (в репозитории) | папки/файлы в корне проекта | правила, команды, навыки, MCP-конфиг, ignore |

- **Roo-Code**: каталог `.roo/` в корне workspace — `rules/`, `commands/`, `skills/`, `mcp.json`;
  плюс `.roomodes` (кастомные режимы) и `.rooignore`. Проектно-скоупленные настройки коммитятся в VCS.
- **Cline**: `.clinerules/` (правила), `.clineignore`, `cline_mcp_settings.json`; глобальные
  настройки/профили/история — в `globalState`, ключи — в `secrets`.

**Вывод для RPAWriter2:** два уровня — (1) global (пользователь × версия VS) и (2) per-workspace
(по открытому решению). Идентификатор workspace = путь открытого `.sln`/каталога решения.

---

## 3. Фаза A — MVVM-рефакторинг вкладок (Spec / Knowledge base / Settings)

### A.0 Новые контракты (Core)

Все модели получают зависимости через интерфейсы. Сейчас есть только `IChatHistoryStore` и
`IUiStateStore`. Добавить:

- `ISettingsStore` (`SettingsData Load()`, `void Save(SettingsData)`) — сейчас `SettingsStore`
  статичен; адаптер `JsonSettingsStore` в VSIX (по образцу `JsonUiStateStore`).
- `IWorkspaceProvider` (`WorkspaceInfo? GetCurrentWorkspace()`) — общий для фаз A и B; в фазе A
  используется как `ISolutionPathProvider` (`string? GetSolutionDirectory()`).
- `IFilePicker` / `IFolderPicker` — вместо `OpenFileDialog`/`FolderBrowserDialog` в моделях
  (в VSIX уже есть `Services/FolderPicker.cs` — оформить в контракт).
- `IDocumentOpener` — «открыть файл в VS» (сегодня `VsDocumentService.OpenOrActivateAsync`
  вызывается напрямую).
- `IFileWriter`/confirm — для «перезаписать/частично/оставить» (`WriteMarkdownWithConfirm`).

Прогресс/статус в моделях — через `Notify`/`ShowDialogMessage` (уже есть в `BaseModel`) и
`ObservableCollection`, **не** через сообщения чата (правило AGENTS.md).

### A.1 `SettingsModel`

- Владеет снимком `SettingsData` и экспонирует типизированные observable-свойства для каждой
  настройки (string/int/double/bool) — XAML привязывается TwoWay без цикла `x:Name`.
- Команды: `SaveSettingsCommand` (валидация/дефолты — вынести текущий санитайзер `SettingsStore`
  в чистую функцию `SettingsData.Normalize()`), `RefreshPromptsFromDiskCommand`,
  `ExportDocumentationCommand`, `ExportPromptsCommand`, `OpenLogCommand`, `OpenChatLogCommand`.
- Событие `SettingsSaved` (или `NotifyMessage`) — хост пересоздаёт `IAiProvider`,
  `SpeechToTextFactory`, `LogService.Configure` (сегодня `RecreateAiProvider()` и т.п.).
- Ограничение: `PasswordBox` (ApiKeyBox) не биндится — синхронизация в code-behind
  (или attached-behavior). Зафиксировать как известное исключение.
- `TemperatureSlider` + подпись: вычислимое свойство `TemperatureText` (или `StringFormat`).
- `SettingsViewModel` — тонкий адаптер (как `ChatViewModel`) или биндинг модели напрямую.

### A.2 `SpecTabModel`

- Состояние: `ObservableCollection<ChatMessage> SpecMessages`, `SpecModel CurrentSpec`,
  `ObservableCollection<DocItem> Docs`, `bool IsImporting`, `string ImportStatus`,
  прогресс/сообщения аудита, признак `HasOutput` для кнопки Copy all.
- Команды: `ImportSpecCommand`, `ClearSpecCommand`, `GenerateAlgorithmCommand`,
  `GenerateRobotCommand`, `FindSimilarCommand`, `SecurityAuditCommand`, `ConvertMarkdownToDocxCommand`.
  (Копирование в буфер — View-забота, остаётся в хосте.)
- Зависимости: `IAiProvider`, `IWorkspaceProvider`/`ISolutionPathProvider`, `IFilePicker`,
  `IFileWriter` (confirm), `RobotSecurityAuditService` (Core), конвейер чтения
  (`OpenXmlDocumentReader` и др.), `IDocumentOpener`.
- `CancellationTokenSource` аудита (`_securityAuditCts`) — в модель, отмена — команда модели;
  прогресс — через `Notify`/коллекции. `RobotSecurityAuditService.FindRobotSolutionsInSiblingFolders`
  остаётся в Core, модель лишь оркеструет.

### A.3 `KnowledgeBaseModel`

- Состояние: `KnowledgeBaseFormationMode ImportMode` (Override/Continue), `bool IsImporting`,
  `string StatusText`, `double Progress`, `int ContextWindowUsage`,
  `ObservableCollection<string> Messages`, `bool AutoScroll`.
- Команды: `ImportFromRobotsCommand`, `PauseCommand`, `StopCommand`, `RefreshCommand`.
- Зависимости: `KnowledgeBaseFormationService`/`RobotKnowledgeBaseImportService` (Core),
  `IAiProvider`, `IWorkspaceProvider`, источник настроек `KnowledgeBasePath`/`RobotsRootPath`
  (через `SettingsModel` как shared-источник или инжектированный источник), `IFilePicker`.
- `_kbImportCts`/`_kbImportPaused` — в модель (`Pause`/`Stop` = команды).

### A.4 Изменения View

- `MainWindowControl.xaml`: заменить `Click=`-обработчики вкладок Spec/KB/Settings на
  `Command="{Binding Cmd}" CommandParameter="…"`; поля привязать `Text="{Binding …}"`/
  `IsChecked="{Binding …}"`/`SelectedIndex`/`SelectedValue` (через существующие конвертеры,
  как уже сделано для чата).
- Code-behind: удалить `LoadSettings`/`OnSaveSettings`-циклы, `AppendSpecMessage/AppendSpecText/
  SetKbStatus/SetKbProgress/AppendKbMessage` и обработчики вкладок; оставить только View-концерны
  (DTE-события, таймеры, тосты, `UpdateCreateRobotVisibility`, диалоги).
- `MvvmFactory.Attach` для трёх моделей; `_chatModel` остаётся корневым.

### A.5 Тесты

- `SettingsModelTests`, `SpecTabModelTests`, `KnowledgeBaseModelTests` +
  `…_CanBeConstructedWithoutViewModelAndView`.
- Ручные фейки: `InMemorySettingsStore`, `StubAiProvider`, `FakeFilePicker`,
  `StubSolutionPathProvider`, `FakeDocumentOpener` (по образцу `InMemoryChatHistoryStore`/
  `InMemoryUiStateStore`).
- `ModelArchitectureTests` — новые модели не тянут WPF/SQLite/DTE.

**Критерий готовности фазы A:** вкладки Spec/KB/Settings управляются моделями, code-behind для
них пуст от бизнес-логики, тесты зелёные, покрытие моделей ≥90 %.

---

## 4. Фаза B — Разделение рабочих пространств

### B.0 Модель двух уровней

- **Global** — `%AppData%\RPAWriter2\<version>\` — не зависит от решения.
- **Workspace** — `%AppData%\RPAWriter2\<version>\workspaces\<workspaceId>\` — зависит от решения.
- `workspaceId` = `SHA1(канонизированный путь .sln/каталога решения)` в нижнем регистре
  (хэш предпочтительнее slug — не ломается от спецсимволов/длины). Рядом хранить
  `workspace.json` с `Id`, `Path`, `Name`, `LastOpenedUtc` (для будущего списка «недавние»).

### B.1 Идентификатор и провайдер

- `RPAWriter.Core/Services/WorkspaceIdentity.cs`: `WorkspaceIdentity.ComputeId(string path)` (чистая,
  тестируемая), класс `WorkspaceInfo`.
- Контракт `IWorkspaceProvider` (Core.Contracts): `WorkspaceInfo? GetCurrentWorkspace()`.
- Реализация в VSIX (`Services/VsWorkspaceProvider.cs`) — обёртка над `dte.Solution.FullName`/
  `IsOpen` (переиспользовать `GetOpenSolutionDirectory()`), события открытия/закрытия решения →
  переключение скоупа.

### B.2 Скоуп хранилищ (`AppDataPaths`)

- Добавить: `WorkspaceScopedRoot`, `ResolveWorkspace(workspaceId, params segments)`,
  `SetWorkspaceScope(string? workspaceId)` (аналог `InitializeScope`).
- Все три стора (`SettingsStore`, `ChatHistoryStore`, `UiStateStore`) умеют писать либо в global,
  либо в workspace-каталог (через `AppDataPaths`; контракты data-only не меняются).

### B.3 Настройки: global vs workspace (**решено**)

> **Решение:** per-workspace — **только логи**; всё остальное — global.

Workspace-скоупленные настройки (только лог-файлы):

- `LogFilePath` — путь к файлу журнала расширения;
- `ChatLogFilePath` — путь к файлу журнала чата.

Всё остальное (`BaseUrl`, `ApiKey`, `Model`, `Temperature`, `ReasoningEffort`, контекстное окно,
сеть/повторы, цены, `ToolCallingMode`, `MemoryBankMode`, `UseToolRole`, итерации/циклы, sub-agent
параметры, `UserName`, `AddressByName`, `SpeechToText*`, `VoskModelPath`, `SpeechAutoSend`,
`ShowControls`, `AutoCaptureRuntimeExceptions`, `KnowledgeBasePath`, `RobotsRootPath`,
`RobotTemplateName`, `PackagesPath`, `RulesPath`, `SkillsPath`, `LogProvidersPath`,
`SecurityAuditMode`, `ShowRobotCreator`, `CheckpointsEnabled`, `Allow*`) — **global**.

Примечание: `LogProvidersPath` — это каталог провайдеров логов (общий ресурс, не «логи»),
остаётся global. Если потребуется скоупить и его — это однострочное добавление.

Реализация:

- `SettingsData` (global) — как сейчас, минус `LogFilePath`/`ChatLogFilePath` как **эффективных**
  значений (остаются как global-фолбэк для случая «без решения», по умолчанию пусто = «рядом с
  расширением»).
- `WorkspaceLogSettings { LogFilePath, ChatLogFilePath }` — хранится в `workspaces/<id>/settings.json`.
- `SettingsModel` композирует: эффективные лог-пути = workspace-значения (если заданы) → иначе
  global-значения → иначе дефолт «рядом с расширением». `Save` пишет лог-пути в workspace-скоуп,
  остальное — в global.

### B.4 История чата per-workspace (**решено**)

- **Одна база** `chat_history.db`; добавить колонку `workspace_id TEXT NOT NULL DEFAULT ''` в
  таблицу `conversations` (+ индекс). Миграция схемы — в `ChatHistoryStore.Initialize()`
  (паттерн `EnsureColumn` уже есть).
- `IChatHistoryStore` получает текущий `workspace_id` (метод `Initialize(string workspaceId)` или
  стора создаётся уже со скоупом). `ChatModel` при старте узнаёт workspace через `IWorkspaceProvider`
  и подставляет `workspace_id` во все операции истории.
- UI: в списке истории — пометка «текущий workspace»; опционально переключатель «показать все».

### B.5 UI-состояние per-workspace

- `ui_state.json` → в workspace-каталог (`LastConversationId`, `ActiveTab`, `ChatViewMode`,
  `AgentMode` — всё про конкретное решение). Глобально можно оставить `ActiveTab` (опционально).
- `IUiStateStore` data-only — менять только реализацию пути через `AppDataPaths`.

### B.6 Секреты (API key) — DPAPI (**решено, делаем сейчас**)

- `ApiKey` выносится из `SettingsData` в защищённое хранилище:
  - Core-контракт `ISecretsStore`: `string? LoadApiKey()`, `void SaveApiKey(string?)`,
    `void ClearApiKey()`.
  - VSIX-реализация `DpapiSecretsStore` (Windows DPAPI `ProtectedData`, scope
    `CurrentUser`) — ключ не лежит в JSON и не дублируется по workspace.
- `SettingsModel` читает/пишет ключ через `ISecretsStore`; `PasswordBox` синхронизируется в
  code-behind (см. A.1).

### B.7 Миграция

- Расширить паттерн `AppDataPaths.MigrateLegacyData()`:
  - при первом открытии решения → создать `workspaces/<id>/`, создать/преобразовать
    `ui_state.json` и лог-настройки workspace; историю чата пометить `workspace_id` текущего
    решения (или оставить `''` = legacy/global, чтобы ничего не потерялось);
  - «без решения» — продолжать писать в global (совместимо с текущим поведением).

### B.8 UI-индикация

- Показывать текущий workspace (имя/путь) — в шапке окна или в Settings рядом с кнопкой Save;
  кнопка «сбросить состояние workspace» (очистить workspace-настройки/историю/UI). Только
  бейджи/`NotificationBanner`, без сообщений в чат (правило AGENTS.md).

---

## 5. Порядок работ

**Фаза A (MVVM)**
1. Контракты Core: `ISettingsStore`, `IWorkspaceProvider`/`ISolutionPathProvider`, `IFilePicker`,
   `IFolderPicker`, `IDocumentOpener`, `IFileWriter` + адаптеры в VSIX.
2. `SettingsModel` + `SettingsViewModel` + XAML Settings на биндинги (кроме `PasswordBox`).
3. `SpecTabModel` + перенос обработчиков Spec; XAML Spec на биндинги.
4. `KnowledgeBaseModel` + перенос KB; XAML KB на биндинги.
5. Чистка `MainWindowControl.xaml.cs` (удалить state/циклы/обработчики вкладок).
6. Тесты по каждой модели + `ModelArchitectureTests`; `dotnet test`.

**Фаза B (workspaces)**
7. `WorkspaceIdentity` + `IWorkspaceProvider` + `VsWorkspaceProvider` + события DTE.
8. `AppDataPaths`: workspace-корень, `SetWorkspaceScope`, `ResolveWorkspace`.
9. `WorkspaceLogSettings` (`LogFilePath`, `ChatLogFilePath`) + два стора; `SettingsModel` пишет
   логи в workspace, остальное — в global.
10. `chat_history.db`: колонка `workspace_id` + фильтрация в `IChatHistoryStore`; `ChatModel`
    скоупится по workspace.
11. `ui_state.json` per-workspace.
12. `ISecretsStore` + `DpapiSecretsStore` для `ApiKey`.
13. Миграция legacy → workspace при первом открытии.
14. UI-индикация workspace + «сброс workspace».
15. Тесты миграции/скоупов + обновление документации (`05-settings.md`, `13-model-layer.md`,
    `00-index.md`).

---

## 6. Риски и ограничения

- **`PasswordBox`** не биндится — вынужденный код в code-behind (или attached-behavior).
- **Миграция истории**: `workspace_id = ''` трактовать как legacy/global; при открытии решения —
  не «переписывать» существующую историю, а связывать её с workspace только при явном действии,
  чтобы не потерять данные.
- **DPAPI** привязан к пользователю/машине — нормально для локального расширения; недоступен из
  `netstandard2.0` напрямую → реализация только в VSIX (`net472`), в Core — только контракт.
- **Переключение решения на лету**: при смене workspace нужно пересоздавать/переинициализировать
  модели (`SettingsModel`, `ChatModel`-историю, `ui_state`) и корректно доскроллить/сфокусировать UI.

---

## 7. Журнал решений

| # | Вопрос | Решение |
|---|---|---|
| 1 | Какие настройки per-workspace? | Только логи (`LogFilePath`, `ChatLogFilePath`); остальное — global. |
| 2 | Хранилище истории | Одна база `chat_history.db` с колонкой `workspace_id`. |
| 3 | API key | Выносить в DPAPI/secret-store сейчас (`ISecretsStore` + `DpapiSecretsStore`). |
| 4 | Воркспейс = только `.sln`? | Нет: поддерживается и «Открыть папку» (Open Folder). Путь папки берётся через `IVsSolution` (`VSPROPID_IsInOpenFolderMode` + `GetSolutionInfo`); выбор решения/папки — в Core `WorkspaceResolver.Resolve`. |
