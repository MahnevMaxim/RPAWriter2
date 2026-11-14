# 14. Рабочие пространства

> Статус: **реализовано**. Документ остаётся как запись принятых решений.

## 0. Цель

Разделить данные расширения на два уровня — **global** (пользователь × версия VS) и
**per-workspace** (по открытому решению Visual Studio или открытой папке):

- история чата и UI-состояние хранятся отдельно для каждого workspace;
- из настроек per-workspace скоупятся только пути логов (`LogFilePath`, `ChatLogFilePath`) —
  с global-фолбэком, когда решение/папка не открыты;
- API-ключ вынесен из настроек в защищённое хранилище (DPAPI) и не дублируется по workspace.

---

## 1. Текущее хранение (всё **глобальное**, только по версии VS 17/18)

| Что | Файл | Путь |
|---|---|---|
| Настройки | `settings.json` | `%AppData%\RPAWriter2\<17\|18>\settings.json` (`SettingsStore`) |
| История чата | `chat_history.db` (SQLite) | там же (`ChatHistoryStore` → `SqliteChatHistoryStore`) |
| UI-состояние | `ui_state.json` | там же (`UiStateStore` → `JsonUiStateStore`) |

`AppDataPaths` централизует пути и умеет `InitializeScope(vsVersion)` + `MigrateLegacyData()`.

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
(по открытому решению или открытой папке). Идентификатор workspace = путь открытого `.sln`/каталога
решения.

---

## 3. Разделение рабочих пространств

### 3.1 Модель двух уровней

- **Global** — `%AppData%\RPAWriter2\<version>\` — не зависит от решения.
- **Workspace** — `%AppData%\RPAWriter2\<version>\workspaces\<workspaceId>\` — зависит от решения.
- `workspaceId` = `SHA1(канонизированный путь .sln/каталога решения)` в нижнем регистре
  (хэш предпочтительнее slug — не ломается от спецсимволов/длины). Рядом хранить
  `workspace.json` с `Id`, `Path`, `Name`, `LastOpenedUtc` (для будущего списка «недавние»).

### 3.2 Идентификатор и провайдер

- `RPAWriter.Core/Services/WorkspaceIdentity.cs`: `WorkspaceIdentity.ComputeId(string path)` (чистая,
  тестируемая), класс `WorkspaceInfo`.
- Контракт `IWorkspaceProvider` (Core.Contracts): `WorkspaceInfo? GetCurrentWorkspace()`.
- Реализация в VSIX (`Services/VsWorkspaceProvider.cs`) — обёртка над `dte.Solution.FullName`/
  `IsOpen` (переиспользовать `GetOpenSolutionDirectory()`), события открытия/закрытия решения →
  переключение скоупа.

### 3.3 Скоуп хранилищ (`AppDataPaths`)

- Добавить: `WorkspaceScopedRoot`, `ResolveWorkspace(workspaceId, params segments)`,
  `SetWorkspaceScope(string? workspaceId)` (аналог `InitializeScope`).
- Все три стора (`SettingsStore`, `ChatHistoryStore`, `UiStateStore`) умеют писать либо в global,
  либо в workspace-каталог (через `AppDataPaths`; контракты data-only не меняются).

### 3.4 Настройки: global vs workspace (**решено**)

> **Решение:** per-workspace — **только логи**; всё остальное — global.

Workspace-скоупленные настройки (только лог-файлы):

- `LogFilePath` — путь к файлу журнала расширения;
- `ChatLogFilePath` — путь к файлу журнала чата.

Всё остальное (`BaseUrl`, `Model`, `Temperature`, `ReasoningEffort`, контекстное окно,
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

### 3.5 История чата per-workspace (**решено**)

- **Одна база** `chat_history.db`; добавить колонку `workspace_id TEXT NOT NULL DEFAULT ''` в
  таблицу `conversations` (+ индекс). Миграция схемы — в `ChatHistoryStore.Initialize()`
  (паттерн `EnsureColumn` уже есть).
- `IChatHistoryStore` получает текущий `workspace_id` (метод `Initialize(string workspaceId)` или
  стора создаётся уже со скоупом). `ChatModel` при старте узнаёт workspace через `IWorkspaceProvider`
  и подставляет `workspace_id` во все операции истории.
- UI: в списке истории — пометка «текущий workspace»; опционально переключатель «показать все».

### 3.6 UI-состояние per-workspace

- `ui_state.json` → в workspace-каталог (`LastConversationId`, `ActiveTab`, `ChatViewMode`,
  `AgentMode` — всё про конкретное решение). Глобально можно оставить `ActiveTab` (опционально).
- `IUiStateStore` data-only — менять только реализацию пути через `AppDataPaths`.

### 3.7 Секреты (API key) — DPAPI (**решено**)

- `ApiKey` выносится из `SettingsData` в защищённое хранилище:
  - Core-контракт `ISecretsStore`: `string? LoadApiKey()`, `void SaveApiKey(string?)`,
    `void ClearApiKey()`.
  - VSIX-реализация `DpapiSecretsStore` (Windows DPAPI `ProtectedData`, scope
    `CurrentUser`) — ключ не лежит в JSON и не дублируется по workspace.
- `SettingsModel` читает/пишет ключ через `ISecretsStore`.

### 3.8 Миграция

- Расширить паттерн `AppDataPaths.MigrateLegacyData()`:
  - при первом открытии решения → создать `workspaces/<id>/`, создать/преобразовать
    `ui_state.json` и лог-настройки workspace; историю чата пометить `workspace_id` текущего
    решения (или оставить `''` = legacy/global, чтобы ничего не потерялось);
  - «без решения» — продолжать писать в global (совместимо с текущим поведением).

### 3.9 UI-индикация

- Показывать текущий workspace (имя/путь) — в шапке окна или в Settings рядом с кнопкой Save;
  кнопка «сбросить состояние workspace» (очистить workspace-настройки/историю/UI). Только
  бейджи/`NotificationBanner`, без сообщений в чат (правило AGENTS.md).

---

## 4. Порядок работ

1. `WorkspaceIdentity` + `IWorkspaceProvider` + `VsWorkspaceProvider` + события DTE.
2. `AppDataPaths`: workspace-корень, `SetWorkspaceScope`, `ResolveWorkspace`.
3. `WorkspaceLogSettings` (`LogFilePath`, `ChatLogFilePath`) + два стора; `SettingsModel` пишет
   логи в workspace, остальное — в global.
4. `chat_history.db`: колонка `workspace_id` + фильтрация в `IChatHistoryStore`; `ChatModel`
   скоупится по workspace.
5. `ui_state.json` per-workspace.
6. `ISecretsStore` + `DpapiSecretsStore` для `ApiKey`.
7. Миграция legacy → workspace при первом открытии.
8. UI-индикация workspace + «сброс workspace».
9. Тесты миграции/скоупов + обновление документации (`05-settings.md`, `13-model-layer.md`,
   `00-index.md`).

---

## 5. Риски и ограничения

- **Миграция истории**: `workspace_id = ''` трактовать как legacy/global; при открытии решения —
  не «переписывать» существующую историю, а связывать её с workspace только при явном действии,
  чтобы не потерять данные.
- **DPAPI** привязан к пользователю/машине — нормально для локального расширения; недоступен из
  `netstandard2.0` напрямую → реализация только в VSIX (`net472`), в Core — только контракт.
- **Переключение решения на лету**: при смене workspace нужно пересоздавать/переинициализировать
  модели (`SettingsModel`, `ChatModel`-историю, `ui_state`) и корректно доскроллить/сфокусировать UI.

---

## 6. Журнал решений

| # | Вопрос | Решение |
|---|---|---|
| 1 | Какие настройки per-workspace? | Только логи (`LogFilePath`, `ChatLogFilePath`); остальное — global. |
| 2 | Хранилище истории | Одна база `chat_history.db` с колонкой `workspace_id`. |
| 3 | API key | Выносить в DPAPI/secret-store сейчас (`ISecretsStore` + `DpapiSecretsStore`). |
| 4 | Воркспейс = только `.sln`? | Нет: поддерживается и «Открыть папку» (Open Folder). Путь папки берётся через `IVsSolution` (`VSPROPID_IsInOpenFolderMode` + `GetSolutionInfo`); выбор решения/папки — в Core `WorkspaceResolver.Resolve`. |
