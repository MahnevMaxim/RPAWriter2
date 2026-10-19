# Сравнение инструментов Cline и RPAWriter (rpawriter)

> Документ — результат исследования. Сравниваются **инструменты агента** (tools),
> их **описания, которые передаются нейросети**, и **функционал**.
> Ничего в коде не менялось.

**Дата исследования:** 19.10.2026

**Источники:**

| Сторона | Источник | Файлы |
|---|---|---|
| **Cline** | [github.com/cline/cline](https://github.com/cline/cline), ветка `main` (на момент исследования — «Autonomous coding agent as an SDK») | `sdk/packages/core/src/extensions/tools/definitions.ts`, `schemas.ts`, `types.ts`, `executors/…` |
| **RPAWriter (rpawriter)** | локальный репозиторий `RPAWriter2` | `RPAWriter.Core/AgentTools/*`, `RPAWriter2/Services/AgentTools/AgentToolFactory.cs` |

---

## 1. Что такое «инструмент» и «описание для нейросети» в обоих проектах

В обоих проектах инструмент — это именованная функция, которую агент (LLM) может
вызвать в диалоге. Для каждого инструмента в системный промпт / в список
`tools` запроса попадают:

- **имя** (machine name, например `read_files` / `read_file`);
- **описание** (`description`) — текст, который объясняет модели, когда и как
  использовать инструмент;
- **схема параметров** (`inputSchema` / `ParametersSchema`) — JSON-схема или
  неформальный JSON с типами аргументов;
- служебные флаги: изменяет ли инструмент состояние, категория прав, таймауты,
  retry-политика и т.д.

Разница в протоколе передачи инструментов модели:

| Аспект | Cline (SDK) | RPAWriter (rpawriter) |
|---|---|---|
| Протокол | Native function calling (`tools` + `tool_calls`) через `AgentRuntime` | Два режима: **JSON-over-chat** (по умолчанию) и **native function calling** (`ToolCallingMode`) |
| Описание инструмента | `AgentToolDefinition`: `name`, `description`, `inputSchema` (JSON Schema из Zod), `timeoutMs`, `retryable`, `maxRetries`, `lifecycle` | `IAgentTool`: `Name`, `Description`, `ParametersSchema` (неформальный JSON), `Mutates`, `Permission`, `ExecuteAsync`, `GetDisplayResultAsync` |
| Фильтрация по режиму | через флаги `enableXxx` в `createDefaultTools` | `AgentToolRegistry.ForMode(mode, allowed)`: **Plan** = только read-only, **Act** = все |
| Исполнители | инжектируются хостом (`executors.readFile`, `executors.bash` …) | привязаны к сервисам VS/ядра (`DTE2`, `RobotTemplateService`, `IAiProvider` …) |

---

## 2. Cline — архитектура инструментов

Текущий репозиторий Cline перешёл на SDK-модель. Набор инструментов по умолчанию
собирается фабрикой `createDefaultTools(...)` в
`sdk/packages/core/src/extensions/tools/definitions.ts`. Каждый инструмент создаётся
через `createTool({ name, description, inputSchema, timeoutMs, retryable, maxRetries,
execute, ... })`, а JSON-схема входных параметров генерируется из Zod-схем
(`zodToJsonSchema`) в `schemas.ts`.

Список инструментов по умолчанию и флаги включения:

| Инструмент | Флаг по умолчанию | Назначение |
|---|---|---|
| `read_files` | `enableReadFiles = true` | чтение текстовых/графических файлов |
| `search_codebase` | `enableSearch = true` | regex-поиск по кодовой базе |
| `run_commands` | `enableBash = true` | выполнение shell-команд |
| `fetch_web_content` | `enableWebFetch = true` | загрузка веб-контента |
| `editor` | `enableEditor = true` | точечное редактирование/создание файлов |
| `apply_patch` | `enableApplyPatch = false` | альтернатива editor — freeform patch-грамматика |
| `skills` | `enableSkills = true` | вызов skill / slash-команды |
| `ask_question` | `enableAskQuestion = true` | уточняющий вопрос пользователю |
| `submit_and_exit` | `enableSubmitAndExit = false` | финальный ответ и завершение |

> `editor` и `apply_patch` взаимоисключающие: если включён `editor`, `apply_patch`
> не добавляется. Аналогично `ask_question` и `submit_and_exit` выбираются по
> конфигурации.

### Соответствие классических имён Cline новым именам SDK

Пользовательская терминология («read_file», «execute_command» и т.п.) относится к
классическому Cline-расширению. В текущем SDK имена изменились:

| Классическое имя | Имя в текущем SDK |
|---|---|
| `read_file` | `read_files` |
| `search_files` | `search_codebase` |
| `execute_command` | `run_commands` |
| `web_fetch` | `fetch_web_content` |
| `write_to_file` / `replace_in_file` | `editor` (или `apply_patch`) |
| `use_skill` | `skills` |
| `ask_followup_question` | `ask_question` |
| `attempt_completion` | `submit_and_exit` |

> В классическом расширении дополнительно были/есть инструменты, которых нет в
> дефолтном наборе SDK: `browser_action`, `use_mcp_tool`, `access_mcp_resource`,
> `component_get_prompt`, `web_search`, `todo_write`, `new_task` (сабагенты),
> `list_files`, `list_code_definition_names`. В SDK они вынесены в расширения
> (`sdk/packages/core/src/extensions/…`) или предоставляются хостом. Ниже
> сравниваются **дефолтные инструменты SDK** (это и есть то, что реально
> «отдаётся нейросети» по умолчанию в текущем репозитории).

---

## 3. Cline — полная таблица инструментов (описание для нейросети + функционал)

Описания приведены **дословно** (англ.), как они передаются модели. Значения лимитов
подставляются из `executors/output-limits.ts`: `MAX_READ_LINES = 2000`,
`MAX_READ_OUTPUT_CHARS = 48000` (~47k), `MAX_SEARCH_OUTPUT_CHARS = 48000` (~48k),
`MAX_COMMAND_OUTPUT_CHARS = 48000` (~47k).

### 3.1 `read_files`

- **Описание (для нейросети):**
  > "Read the content of text or image files at the provided absolute paths, or return only an inclusive one-based line range when start_line/end_line are provided on the same file entry as its path. When you already know multiple files you need, read them together in one call, and call this tool in the same response as other independent tool calls. Each read returns at most 2000 lines / ~47k characters; longer files report their total line count, page through them with start_line/end_line on that file's entry. Binary files that are not image and large files are not supported. Returns file contents or error messages for each path."
- **Функционал:** читает один или несколько файлов (текст/изображения) по абсолютным
  путям; поддерживает диапазоны строк `start_line`/`end_line`; параллельно читает
  несколько файлов; бинарные не-изображения и слишком большие файлы не читаются.
- **Параметры (схема):** `{ files: [{ path, start_line?, end_line? }] }` (также
  принимает одиночную строку/массив путей — union-схема).
- **Служебное:** `timeoutMs` (по умолч. 10000×2), `retryable: true`, `maxRetries: 1`.

### 3.2 `search_codebase`

- **Описание (для нейросети):**
  > "Perform regex pattern searches across the codebase. Supports multiple parallel searches. When several search patterns could be useful and do not depend on each other, run them together in one call, and call this tool in the same response as other independent tool calls. Use for finding code patterns, function definitions, class names, imports, etc. Output beyond ~48k characters per query is middle-truncated; narrow patterns beat broad ones."
- **Функционал:** regex-поиск по кодовой базе; несколько параллельных запросов;
  результат срезается посередине при превышении лимита.
- **Параметры:** `{ queries: string[] }`.
- **Служебное:** `timeoutMs` (30000×2), `retryable: true`, `maxRetries: 1`.

### 3.3 `run_commands`

- **Описание (для нейросети, PowerShell/Windows — зависит от shell):**
  > "Run non-interactive shell commands from the root of the workspace in Windows environment. Use for listing files, checking git status, running builds, executing tests, etc. Commands must be non-interactive. Commands that require follow-up input like pagers should be skipped or used with supported flags/env (e.g. git --no-pager, --non-interactive) to bypass the interaction steps. Output beyond ~47k characters is middle-truncated (start and end preserved); filter output when you need specific sections. Commands run through Windows PowerShell (powershell.exe); quote paths and arguments for powershell.exe and use ';' to sequence commands. Write commands directly; do not wrap them in another powershell.exe -Command invocation. Only start another shell when you intentionally need a different shell or a separate process. Include multiple commands in the same call when they are independent and safe to run concurrently. When independent reads, searches, or edits are also needed, call those tools in the same response."
- **Функционал:** выполняет неинтерактивные shell-команды из корня рабочей области;
  описание строится динамически под фактический shell (PowerShell/CMD/bash/WSL);
  вывод срезается посередине.
- **Параметры:** `RunCommandsInputSchema` (список команд / структурированный ввод).
- **Служебное:** `timeoutMs` (30000×2), `retryable: false`, `maxRetries: 0`.

### 3.4 `fetch_web_content`

- **Описание (для нейросети):**
  > "Fetch content from URLs and analyze them using the provided prompts. Use for retrieving documentation, API references, or any web content. Each request includes a URL and a prompt describing what information to extract. Fetch independent URLs together in one call, and call this tool in the same response as other independent tool calls."
- **Функционал:** HTTP-загрузка содержимого URL и анализ по переданному prompt;
  несколько независимых URL параллельно.
- **Параметры:** `{ requests: [{ url, prompt }] }`.
- **Служебное:** `timeoutMs` (30000×2), `retryable: true`, `maxRetries: 2`.

### 3.5 `editor`

- **Описание (для нейросети):**
  > "An editor for controlled filesystem edits on the text file at the provided path. Provide `insert_line` to insert `new_text` at a specific line number. Otherwise, the tool replaces `old_text` with `new_text`, or creates the file with `new_text` if file does not exist. Use this tool for making small, precise edits to existing files or creating new files over shell commands. If several edits to different files or non-overlapping regions are already known, emit multiple editor tool calls in the same response instead of serializing them across turns."
- **Функционал:** контролируемое редактирование файла: `insert_line` (вставка по
  номеру строки), либо замена `old_text → new_text`, либо создание файла; поддержка
  нескольких правок параллельно.
- **Параметры:** `{ path, old_text?, new_text, insert_line? }` (`EditFileInputSchema`).
- **Служебное:** `timeoutMs` (30000), `retryable: false`, `maxRetries: 0`.

### 3.6 `apply_patch` (альтернатива editor)

- **Описание (для нейросети, фрагмент freeform patch-грамматики):**
  > "Use `apply_patch` to edit files with the canonical freeform patch grammar. Pass the patch text directly as the `input` string. Prefer the exact format below: `*** Begin Patch` / `*** Update File:` / `*** Add File:` / `*** Delete File:` / `*** Move to:`; `@@` markers, `-`/`+` context lines …"
- **Функционал:** применение патча в канонической freeform-грамматике
  (добавление/обновление/удаление/перемещение файлов) через текст патча.
- **Параметры:** `ApplyPatchInputSchema` (принимает строку патча или `{ input }`).
- **Служебное:** `timeoutMs` (30000), `retryable: false`, `maxRetries: 0`.

### 3.7 `skills`

- **Описание (для нейросети):**
  > "Execute a skill within the main conversation. When users ask you to perform tasks, check if any available skills match. When users reference a slash command, invoke it with this tool. Input: `skill` (required) and optional `args`. Example: `skill: "pdf"`, `skill: "commit", args: "-m \"Fix bug\""`, `skill: "review-pr", args: "123"`, `skill: "ms-office-suite:pdf"`. When a skill matches the user's request, invoking this tool is a blocking requirement before any other response. Never mention a skill without invoking this tool."
- **Функционал:** вызывает именованный skill / slash-команду с опциональными
  аргументами; при наличии доступных skill их список дописывается в описание.
- **Параметры:** `{ skill, args? }`.
- **Служебное:** `timeoutMs` (15000), `retryable: false`.

### 3.8 `ask_question`

- **Описание (для нейросети):**
  > "Ask user a question for clarifying or gathering information needed to complete the task. For example, ask the user clarifying questions about a key implementation decision. You should only ask one question. Provide an array of 2-5 options for the user to choose from. Never include an option to toggle to Act mode."
- **Функционал:** задаёт пользователю ровно один уточняющий вопрос с 2–5 вариантами
  ответа.
- **Параметры:** `{ question, options[2..5] }`.
- **Служебное:** `retryable: false`.

### 3.9 `submit_and_exit`

- **Описание (для нейросети):**
  > "Submit the final answer and exit the conversation. For example, submit a summary of the investigation and confirm the issue is resolved. You should only submit once all necessary steps are completed AND you have verified the result by execution. Before calling this, re-read the task, then confirm from your own tool output that every required file exists at the specified path and that its contents match the expected format, data types, and values. If the task provides tests, run them and confirm they pass; if it does not, run your own solution end to end and read the output back as evidence. Do not submit on the assumption that your solution works — submit because you have observed evidence that it does. Provide a summary of the investigation and confirm the issue is resolved."
- **Функционал:** завершает run финальным ответом (сводка расследования + флаг
  верификации). `lifecycle.completesRun = true`.
- **Параметры:** `{ summary (≥10 симв.), verified (boolean) }` (`SubmitInputSchema`).
- **Служебное:** `timeoutMs` (15000), `retryable: false`.

---

## 4. RPAWriter2 (rpawriter) — архитектура инструментов

Инструменты агента живут в двух слоях:

- **`RPAWriter.Core/AgentTools/`** (netstandard2.0, без VS):
  - `IAgentTool` — контракт инструмента;
  - `AgentTool` — реализация на делегате;
  - `AgentToolRegistry` — реестр + фильтрация по режиму и правам;
  - `ToolSchemaConverter` — перевод неформальной схемы в OpenAI JSON Schema;
  - `AgentPromptBuilder` — сборка системного промпта (список инструментов в `{tools}`);
  - `AgentRunner` — агентный цикл (вызов → tool call → результат → повтор).
- **`RPAWriter2/Services/AgentTools/AgentToolFactory.cs`** — фабрика, которая
  регистрирует 32 инструмента, оборачивая сервисы VS/ядра (`DTE2`, `SpecModel`,
  KB-путь, `RobotTemplateService`, `IBuildFeedbackService`, `IAiProvider` и т.д.).

### 4.1 Модель инструмента `IAgentTool`

| Поле | Назначение |
|---|---|
| `Name` | машинное имя в `tool_call` JSON |
| `Description` | человекочитаемое описание, показывается в системном промпте |
| `ParametersSchema` | неформальный JSON-словарь типов (`{"path":"string"}`) |
| `Mutates` | `true`, если инструмент меняет состояние (файлы, решение, сборка, KB) |
| `Permission` | категория прав (по умолчанию: `EditFiles`, если `Mutates`, иначе `ReadFiles`) |
| `ExecuteAsync` | выполнение и текстовый результат, возвращаемый модели |
| `GetDisplayResultAsync` | короткий текст для строки прогресса в чате (или `null`) |

### 4.2 Категории прав (`AgentPermissions`)

`ReadFiles`, `EditFiles`, `ExecuteCommand`, `WebFetch`, `Mcp` (значения из
`Models/Models.cs`; имена для сообщений — в `AgentRunner.PermissionName`).

### 4.3 Фильтрация по режиму

`AgentToolRegistry.ForMode(mode, allowed)`:

- **Plan** — только `!Mutates` (read-only);
- **Act** — все инструменты;
- дополнительно отсекаются инструменты, чья категория `Permission` не входит в
  разрешённый набор `allowed`.

### 4.4 Протокол вызова

- **JSON-over-chat (по умолчанию):** модель возвращает
  `{"action":"tool_call","tool":"<name>","arguments":{...}}` или
  `{"action":"final","text":"..."}`; список инструментов инжектится в промпт как
  `- имя: описание Parameters: {схема}`.
- **Native function calling (опция `ToolCallingMode.Native`):** инструменты уходят
  как `tools` (OpenAI function calling), схема строится `ToolSchemaConverter`.

---

## 5. RPAWriter2 — полная таблица инструментов

Описания приведены **дословно** (англ.), как в `AgentToolFactory.CreateRegistry()`.
Всего **32 инструмента**: 20 read-only (Plan + Act) и 12 mutating (Act only).

### 5.1 Read-only: спецификация, анализ, база знаний, память

| Инструмент | Описание (дословно) | Функционал | Параметры | Права |
|---|---|---|---|---|
| `read_spec` | "Return the currently imported specification (name, steps, inputs, outputs, tasks, raw text)." | возвращает текущий `SpecModel` целиком | `{}` | ReadFiles |
| `read_file` | "Read a text file FROM DISK (saved content only) by absolute or solution-relative path. Does not reflect unsaved editor changes; to read the open editor tab use open_file then read_open_document." | чтение файла с диска (`File.ReadAllText` + обрезка) | `{path}` | ReadFiles |
| `read_robot_sources` | "List the robot source files (.xaml/.cs) under a directory with their contents." | список исходников робота с содержимым | `{directory}` | ReadFiles |
| `analyze_spec` | "Analyze the imported spec into a summary and tasks." | AI-анализ спеков (`AnalyzeSpecAsync`) | `{}` | ReadFiles |
| `decompose_tasks` | "Decompose a text into atomic tasks." | AI-декомпозиция текста на атомарные задачи | `{text}` | ReadFiles |
| `reconstruct_spec` | "Reconstruct a spec from robot sources under a directory." | AI-восстановление спеки из исходников робота | `{robotName,directory}` | ReadFiles |
| `map_task_to_solution` | "Locate the solution for a task in robot sources." | AI-поиск решения задачи в исходниках | `{task,directory}` | ReadFiles |
| `find_similar` | "Find similar tasks in the knowledge base." | поиск похожих задач в KB | `{action,target,description,topK}` | ReadFiles |
| `list_kb` | "List the solutions in the knowledge base." | список записей KB | `{}` | ReadFiles |
| `read_memory` | "Read the project memory bank (memory-bank/*.md) to restore long-term context." | чтение memory-bank файлов | `{}` | ReadFiles |

### 5.2 Read-only: IDE/журналы/файлы/git/web

| Инструмент | Описание (дословно) | Функционал | Параметры | Права |
|---|---|---|---|---|
| `read_errors` | "Read compiler errors from the Visual Studio Error List." | ошибки компиляции из Error List | `{}` | ReadFiles |
| `read_output` | "Read the Visual Studio Output window text." | текст окна Output | `{}` | ReadFiles |
| `open_file` | "Open a file in Visual Studio and activate its tab. Use before read_open_document when the file's tab is not open yet. Does not read the file content from disk." | открыть/активировать вкладку в VS | `{path}` | ReadFiles |
| `read_open_document` | "Read the content currently shown in an open editor tab (includes unsaved changes). Provide \"path\" to read a specific open tab; omit it to read the active document. Never reads from disk. Prefer this over read_file for files being edited." | чтение буфера открытой вкладки (включая несохранённые правки) | `{path}` | ReadFiles |
| `analyze_logs` | "Analyze log files at the given paths." | анализ логов по путям | `{paths:[string]}` | ReadFiles |
| `list_directory` | "List files and subdirectories in a directory (absolute or solution-relative path)." | список файлов/подкаталогов | `{path,recursive}` | ReadFiles |
| `search_files` | "Search text files under a directory for a literal or regex pattern; returns matching file paths with line snippets." | поиск (литерал/regex) с фрагментами строк | `{directory,pattern,useRegex}` | ReadFiles |
| `git_status` | "Show the git working-tree status of the open solution's repository." | git status рабочего дерева | `{}` | ReadFiles |
| `git_diff` | "Show the uncommitted git diff of the open solution's repository (optionally for one path)." | git diff незакоммиченных изменений | `{path}` | ReadFiles |
| `web_fetch` | "Fetch the content of a web URL (HTTP GET) and return the response text." | HTTP GET URL | `{url}` | WebFetch |

### 5.3 Mutating (только Act)

| Инструмент | Описание (дословно) | Функционал | Параметры | Права |
|---|---|---|---|---|
| `generate_robot` | "Generate a robot (XAML/C#) from the imported spec and save the artifacts next to the spec." | генерация робота и запись артефактов | `{}` | EditFiles |
| `write_file` | "Write a text file (create or overwrite) by absolute or solution-relative path." | создание/перезапись файла | `{path,content}` | EditFiles |
| `apply_fix` | "Replace the content of an existing file with corrected content. Edits through the open Visual Studio editor tab (single Undo step) and saves it; falls back to a disk write when the file cannot be opened in the editor." | замена содержимого файла через вкладку редактора + сохранение (один Undo), фолбэк на запись на диск | `{path,content}` | EditFiles |
| `edit_open_document` | "Replace the text of an open editor tab directly in the Visual Studio editor buffer (not a raw disk write), then save it. Provide \"path\" to target a specific file (opens it if needed), or omit \"path\" to edit the active document. Applied as one Undo step." | замена текста открытой вкладки прямо в буфере + сохранение | `{path,content}` | EditFiles |
| `create_robot` | "Create a robot solution (RPA-<number>) from the configured template." | создание решения робота из шаблона | `{number}` | EditFiles |
| `build` | "Run a Release build of the open solution." | Release-сборка решения | `{}` | EditFiles |
| `convert_uipath` | "Convert a UiPath XAML file to Workflow Foundation and write the artifacts." | конвертация UiPath → WF и запись | `{path}` | EditFiles |
| `kb_import` | "Import the knowledge base from the robots directory." | импорт KB из каталога роботов | `{mode:override|continue}` | EditFiles |
| `kb_add` | "Add a record to the knowledge base." | добавление записи в KB | `{name,task,xamlFragment,csharpCode}` | EditFiles |
| `update_memory` | "Write a project memory bank file (activeContext.md, progress.md, etc.)." | запись файла memory-bank | `{file,content}` | EditFiles |
| `run_command` | "Run an arbitrary PowerShell command in the solution directory and return its output. Use for dotnet build/test, git and environment inspection." | выполнение PowerShell-команды | `{command}` | ExecuteCommand |
| `download_file` | "Download a file from a URL and save it to disk." | скачивание файла по URL на диск | `{url,path}` | WebFetch |

---

## 6. Сводная таблица сравнения (Cline ↔ RPAWriter2)

Прямое сопоставление по назначению. «—» означает отсутствие аналога.

| Категория | Cline (SDK) | RPAWriter2 (rpawriter) | Замечание |
|---|---|---|---|
| Чтение файлов | `read_files` | `read_file` (с диска), `read_open_document` (буфер редактора), `read_robot_sources` | Cline читает диск/картинки с диапазонами; rpawriter дополнительно разделяет диск и «открытую вкладку» VS |
| Поиск по коду | `search_codebase` | `search_files` | Cline — regex по всей кодовой базе; rpawriter — литерал/regex в каталоге со сниппетами |
| Листинг каталога | — (классич. `list_files`) | `list_directory` | в SDK отсутствует отдельный list, есть в классическом расширении |
| Выполнение команд | `run_commands` | `run_command` | Cline — мультикоманды/heredoc; rpawriter — одна PowerShell-команда |
| Веб-контент | `fetch_web_content` | `web_fetch` | у Cline анализ по prompt, у rpawriter просто HTTP GET |
| Скачивание файла | — | `download_file` | в Cline — через run_commands/curl |
| Редактирование файлов | `editor` / `apply_patch` | `write_file`, `apply_fix`, `edit_open_document` | rpawriter разделяет «создать/перезаписать», «заменить через редактор», «буфер вкладки» |
| Вопрос пользователю | `ask_question` | — | в rpawriter нет инструмента-вопроса |
| Завершение/финал | `submit_and_exit` | — (финал через `{"action":"final"}` протокола) | rpawriter финализирует ответом в JSON, а не инструментом |
| Skills / slash-команды | `skills` | — | в rpawriter нет skills |
| Git-статус | — (через run_commands) | `git_status`, `git_diff` | rpawriter имеет готовые git-инструменты |
| Ошибки компиляции | — (через run_commands) | `read_errors`, `read_output` | rpawriter привязан к VS Error List/Output |
| Спецификация/анализ | — | `read_spec`, `analyze_spec`, `decompose_tasks`, `reconstruct_spec`, `map_task_to_solution` | уникальная RPA-доменная логика |
| База знаний | — | `find_similar`, `list_kb`, `kb_import`, `kb_add` | уникальная RPA-доменная логика |
| Логи | — | `analyze_logs` | уникально |
| Memory bank | — (методология, файлы) | `read_memory`, `update_memory` | в Cline memory bank — соглашение, не инструмент |
| Генерация робота/сборка/конвертация | — | `generate_robot`, `create_robot`, `build`, `convert_uipath` | уникальная RPA-доменная логика |

---

## 7. Ключевые выводы

1. **Количество и фокус.** Cline (SDK) — 9 универсальных инструментов общего
   назначения. RPAWriter2 — 32 узкоспециализированных инструмента, завязанных на
   Visual Studio (`DTE2`, Error List, Output, editor buffer) и RPA-домен
   (спеки, роботы, база знаний, конвертация UiPath).

2. **Стиль описаний для модели.** Описания Cline длинные и «инструктивные»
   (когда использовать, как объединять вызовы, как обходить лимиты вывода).
   Описания rpawriter короткие и «функциональные» (что делает + какие параметры),
   без поведенческих подсказок — они вынесены в системные промпты
   (`agent-plan/act-system-*.txt`, `agent-json-base.txt`).

3. **Редактирование файлов.** У Cline один гибкий `editor` (create/replace/insert)
   плюс опциональный `apply_patch`. У rpawriter — три раздельных инструмента:
   `write_file` (сырая запись), `apply_fix` (через вкладку + Undo, фолбэк на диск),
   `edit_open_document` (правка буфера вкладки). Это отражает специфику работы с
   несохранёнными буферами VS.

4. **Диалог с пользователем.** Cline имеет `ask_question` (уточняющий вопрос) и
   `submit_and_exit` (финальная верификация). rpawriter обходится JSON-протоколом:
   вопросов нет, финал — `{"action":"final"}`. Функция «уточнить у пользователя»
   отсутствует.

5. **Права и режимы.** Оба проекта гейтят инструменты:
   - Cline — флагами `enableXxx` на этапе сборки набора;
   - rpawriter — режимом Plan/Act (`Mutates`) + категориями прав
     (`ReadFiles/EditFiles/ExecuteCommand/WebFetch/Mcp`).

6. **Проверка результата.** В Cline это встроено в описание `submit_and_exit`
   («проверь выполнением, а не предположением»). В rpawriter верификация через
   `build`, `read_errors`, `read_output`, `run_command` (dotnet build/test) —
   набора специализированных инструментов для обратной связи.

7. **Memory bank.** В Cline memory bank — методология (файлы в проекте). В
   rpawriter — первоклассные инструменты `read_memory` / `update_memory`.

---

## Приложение: точные места в коде

**Cline:**
- `sdk/packages/core/src/extensions/tools/definitions.ts` — `createDefaultTools`,
  описания инструментов.
- `sdk/packages/core/src/extensions/tools/schemas.ts` — Zod-схемы входных параметров.
- `sdk/packages/core/src/extensions/tools/executors/output-limits.ts` — лимиты вывода.
- `sdk/packages/agents/src/agent-runtime.ts` — агентный цикл (вызов/результат/повтор).

**RPAWriter2:**
- `RPAWriter.Core/AgentTools/AgentTool.cs`, `IAgentTool.cs`, `AgentToolRegistry.cs`,
  `ToolSchemaConverter.cs`, `AgentPromptBuilder.cs`, `AgentRunner.cs`.
- `RPAWriter2/Services/AgentTools/AgentToolFactory.cs` — регистрация всех 32
  инструментов (`CreateRegistry`, строки 60–104).

