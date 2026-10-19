# Agent Tools Comparison — RPAWriter, OpenCode, Roo-Code, Cline

> Сводная таблица и список инструментов четырёх агентов. Документ — результат
> исследования исходников; ничего в коде не менялось.

**Дата исследования:** 19.10.2026

## 1. Источники

| Агент | Репозиторий | Где определены инструменты |
|---|---|---|
| **RPAWriter (rpawriter)** | локальный `RPAWriter2` | `RPAWriter2/Services/AgentTools/AgentToolFactory.cs` (регистрация, `CreateRegistry`) |
| **OpenCode** | https://github.com/anomalyco/opencode | `packages/core/src/tool/plugin/*.ts` (встроенные), `packages/core/src/tool/mcp.ts`, `tool.ts` |
| **Roo-Code** | https://github.com/RooCodeInc/Roo-Code | `packages/types/src/tool.ts` (`toolNames`), `apps/cli/src/ui/components/tools/types.ts` |
| **Cline** | https://github.com/cline/cline | SDK: `@cline/core` (`read_files`, `search_codebase`, …); legacy: `apps/vscode/src/shared/tools.ts` (`ClineDefaultTool`) |

Методика: список инструментов взят из места регистрации/объявления инструментов в
коде каждого агента, а не из README, чтобы отразить фактическое состояние на момент
исследования (ветка `main`).

## 2. Сводная таблица по функциональным категориям

Условные обозначения: «имя_инструмента» — есть специализированный инструмент;
«—» — отсутствует (достигается через другой инструмент или не поддерживается).

| Категория | RPAWriter | OpenCode | Roo-Code | Cline (SDK) |
|---|---|---|---|---|
| Чтение файлов / буфера | `read_files`, `read_robot_sources` | `read` | `read_file`, `read_command_output` | `read_files` |
| Листинг файлов/директорий | `list_files` | `read` (directory) | `list_files` | — |
| Regex/grep по коду | `search_codebase` | `grep` | `search_files`, `codebase_search` | `search_codebase` |
| Glob-поиск файлов | — | `glob` | — (через `search_files`) | — |
| Точное редактирование | `edit_open_document`, `apply_fix` | `edit` | `apply_diff`, `edit`, `search_and_replace`, `edit_file` | `editor` |
| Запись/создание файла | `write_file` | `write` | `write_to_file` | `editor` |
| Применение patch | — (перезапись) | `patch` | `apply_patch` | `apply_patch` |
| Shell-команды | `run_command` | `shell` | `execute_command` | `run_commands` |
| Веб-загрузка (fetch) | `web_fetch` | `webfetch` | — | `fetch_web_content` |
| Веб-поиск | — | `websearch` | — | `web_search` |
| Браузерная автоматизация | — | — (через MCP/плагины) | — (browser удалён) | — (legacy `browser_action`) |
| Вопрос пользователю | `ask_question` | `question` | `ask_followup_question` | `ask_question` |
| Skills / slash-команды | — | `skill` | `skill`, `run_slash_command` | `skills` |
| Субагенты / новые задачи | — | `subagent` | `new_task` | `spawn_agent` |
| MCP-инструменты | — | `mcp__*`, `mcp_resource` | `use_mcp_tool`, `access_mcp_resource` | MCP (`use_mcp_tool`, `access_mcp_resource`) |
| Todo-список | — (`decompose_tasks` частично) | — | `update_todo_list` | — |
| Завершение ответа | — (JSON `action:"final"`) | — (завершение текстом) | `attempt_completion` | `submit_and_exit` |
| Переключение режимов | Plan/Act (гейтинг `Mutates`) | — (permission/codemode) | `switch_mode` | — (legacy `plan_mode_respond`/`act_mode_respond`) |
| Git-инструменты | `git_status`, `git_diff` | — (через `shell`) | — (через `execute_command`) | — (через `run_commands`) |
| Ошибки компиляции / output | `read_errors`, `read_output` | — (через `shell`) | — (через `execute_command`) | — (через `run_commands`) |
| Спецификация / анализ (RPA) | `read_spec`, `analyze_spec`, `decompose_tasks`, `reconstruct_spec`, `map_task_to_solution` | — | — | — |
| База знаний | `find_similar`, `list_kb`, `kb_import`, `kb_add` | — | — | — |
| Анализ логов | `analyze_logs` | — | — | — |
| Memory bank | `read_memory`, `update_memory` | — (файлы/AGENTS.md) | — | — (файлы) |
| Генерация/сборка/конвертация (RPA) | `generate_robot`, `create_robot`, `build`, `convert_uipath` | — | — | — |
| Скачивание файла | `download_file` | — (через `webfetch`/`shell`) | — | — |

## 3. Полные списки инструментов

### 3.1 RPAWriter (rpawriter) — 31 активный инструмент

Регистрация: `RPAWriter2/Services/AgentTools/AgentToolFactory.cs`, метод `CreateRegistry`.
`Mutates: false` — доступно в Plan и Act; `Mutates: true` — только в Act.

| # | Инструмент | Mutates | Назначение |
|---|---|---|---|
| 1 | `read_spec` | нет | текущая импортированная спецификация |
| 2 | `read_files` | нет | чтение текстовых/графических файлов (батч + start/end line) |
| 3 | `read_robot_sources` | нет | список .xaml/.cs робота с содержимым |
| 4 | `analyze_spec` | нет | анализ спецификации в сводку и задачи |
| 5 | `decompose_tasks` | нет | декомпозиция текста на атомарные задачи |
| 6 | `reconstruct_spec` | нет | восстановление спецификации из исходников робота |
| 7 | `map_task_to_solution` | нет | поиск решения задачи в исходниках |
| 8 | `find_similar` | нет | поиск похожих задач в базе знаний |
| 9 | `list_kb` | нет | список записей базы знаний |
| 10 | `read_errors` | нет | ошибки компиляции из Error List |
| 11 | `read_output` | нет | текст окна Output |
| 12 | `analyze_logs` | нет | анализ файлов логов |
| 13 | `read_memory` | нет | чтение memory bank (memory-bank/*.md) |
| 14 | `ask_question` | нет | уточняющий вопрос пользователю (2–5 вариантов) |
| 15 | `list_files` | нет | список файлов/директорий (рекурсивно, .gitignore) |
| 16 | `search_codebase` | нет | regex-поиск по кодовой базе (несколько запросов) |
| 17 | `git_status` | нет | git working-tree статус решения |
| 18 | `git_diff` | нет | несохранённый git diff |
| 19 | `web_fetch` | нет | HTTP GET по URL → текст |
| 20 | `generate_robot` | да | генерация робота (XAML/C#) из спецификации |
| 21 | `write_file` | да | запись файла (create/overwrite) |
| 22 | `apply_fix` | да | замена содержимого файла через вкладку VS (Undo) |
| 23 | `edit_open_document` | да | правка буфера открытой вкладки VS |
| 24 | `create_robot` | да | создание решения робота из шаблона |
| 25 | `build` | да | Release-сборка решения |
| 26 | `convert_uipath` | да | конвертация UiPath XAML → Workflow Foundation |
| 27 | `kb_import` | да | импорт базы знаний из каталога роботов |
| 28 | `kb_add` | да | добавление записи в базу знаний |
| 29 | `update_memory` | да | запись файла memory bank |
| 30 | `run_command` | да | произвольная PowerShell-команда |
| 31 | `download_file` | да | скачивание файла по URL на диск |

### 3.2 OpenCode (anomalyco/opencode) — встроенные инструменты

Регистрация: `packages/core/src/tool/plugin/*.ts` (каждый файл — один плагин инструмента).
Инструменты создаются через `Tool`/`Schema` (Effect), имена короткие.

| Инструмент | Файл | Назначение |
|---|---|---|
| `read` | `plugin/read.ts` | чтение файла или директории (текст, изображения, PDF; пагинация offset/limit) |
| `write` | `plugin/write.ts` | создание/перезапись файла |
| `edit` | `plugin/edit.ts` | точная замена `oldString` → `newString` (опц. replaceAll) |
| `patch` | `plugin/patch.ts` | применение unified patch (add/update/delete) |
| `glob` | `plugin/glob.ts` | поиск файлов по glob-паттерну |
| `grep` | `plugin/grep.ts` | поиск по содержимому (ripgrep, regex/literal) |
| `shell` | `plugin/shell.ts` | shell-команда (timeout, background-режим) |
| `webfetch` | `plugin/webfetch.ts` | HTTP(S) GET → text/markdown/html |
| `websearch` | `plugin/websearch.ts` | веб-поиск через выбранную интеграцию |
| `subagent` | `plugin/subagent.ts` | запуск субагента (с моделью, sessionID, background) |
| `skill` | `plugin/skill.ts` | загрузка инструкций skill по ID |
| `question` | `plugin/question.ts` | вопрос(ы) пользователю с вариантами |
| `mcp__*` / `mcp_resource` | `mcp.ts`, `plugin/mcp-resource.ts` | вызов MCP-инструментов и чтение MCP-ресурсов |
| `rename`, `move` | `plugin/opencode.ts` | управление сессиями (переименование, перенос) |
| `worktree_create`, `worktree_remove` | `plugin/opencode.ts` | управление git worktree |
| `models` | `plugin/opencode.ts` | список/поиск доступных моделей |

Архитектурная особенность: инструменты регистрируются через `Tool.Service.transform`,
имена с namespace получают префикс `<namespace>_<tool>`; список расширяется MCP-серверами
и сторонними плагинами.

### 3.3 Roo-Code (RooCodeInc/Roo-Code) — 24 имени инструментов

Канонический список: `packages/types/src/tool.ts` (`toolNames`), snake_case-стиль
(наследие Cline). Группы прав: `read` / `edit` / `command` / `mcp` / `modes`.

| Инструмент | Назначение |
|---|---|
| `execute_command` | выполнение shell-команды |
| `read_file` | чтение файла(ов) |
| `read_command_output` | чтение вывода выполненной команды |
| `write_to_file` | запись/создание файла |
| `apply_diff` | применение diff-изменений к файлу |
| `edit` | точечное редактирование |
| `search_and_replace` / `search_replace` / `edit_file` | варианты редактирования (исторические алиасы) |
| `apply_patch` | применение patch |
| `search_files` | поиск по файлам (regex) |
| `list_files` | список файлов (top-level / recursive) |
| `use_mcp_tool` | вызов MCP-инструмента |
| `access_mcp_resource` | чтение MCP-ресурса |
| `ask_followup_question` | уточняющий вопрос |
| `attempt_completion` | финальный ответ и завершение |
| `switch_mode` | переключение режима агента |
| `new_task` | запуск новой задачи/субагента |
| `codebase_search` | семантический поиск по кодовой базе |
| `update_todo_list` | обновление todo-списка |
| `run_slash_command` | выполнение slash-команды |
| `skill` | загрузка skill |
| `generate_image` | генерация изображения |
| `custom_tool` | вызов пользовательского (custom) инструмента |

Примечание: часть имён (`search_and_replace`, `search_replace`, `edit_file`,
`apply_patch`) — исторические/синонимичные; группа `browser` помечена как
deprecated и удалена из текущих групп.

### 3.4 Cline (cline/cline) — два поколения инструментов

#### 3.4.1 SDK-набор (актуальный, `@cline/core`)

Собирается фабрикой `createDefaultTools(...)`; исполнители инжектируются хостом.
Универсальные инструменты с длинными описаниями и Zod-схемами:

`read_files`, `search_codebase`, `run_commands`, `fetch_web_content`, `editor`,
`apply_patch`, `skills`, `ask_question`, `submit_and_exit`, `ask_followup_question`,
`web_search`, `spawn_agent` + MCP-инструменты (`use_mcp_tool`, `access_mcp_resource`).

#### 3.4.2 Legacy VS Code-набор (`apps/vscode/src/shared/tools.ts`, enum `ClineDefaultTool`, 25)

`ask_followup_question`, `attempt_completion`, `execute_command`, `replace_in_file`,
`read_file`, `write_to_file`, `search_files`, `list_files`,
`list_code_definition_names`, `browser_action`, `use_mcp_tool`, `access_mcp_resource`,
`load_mcp_documentation`, `new_task`, `plan_mode_respond`, `act_mode_respond`,
`web_fetch`, `web_search`, `condense`, `summarize_task`, `report_bug`, `new_rule`,
`apply_patch`, `use_skill`, `use_subagents`.

## 4. Сравнение архитектуры инструментов

| Аспект | RPAWriter | OpenCode | Roo-Code | Cline (SDK) |
|---|---|---|---|---|
| Протокол вызова | JSON-over-chat + native function calling (переключается в настройках) | native function calling (AI SDK / Effect) | native function calling | native function calling |
| Описание инструмента | `IAgentTool`: `Name`, `Description`, `ParametersSchema` (неформальный JSON), `Mutates`, `Permission` | `Tool.Info`: `name`, `description`, `input`/`output` (Schema), `options.namespace` | Zod-схемы + имя/описание, `ToolGroup` | `AgentToolDefinition`: `name`, `description`, `inputSchema` (Zod→JSON Schema), `timeoutMs`, `retryable` |
| Гейтинг прав | Plan/Act (`Mutates`) + категории `ReadFiles`/`EditFiles`/`ExecuteCommand`/`WebFetch` | `Permission.Service` (per-action), `codemode` | группы `read`/`edit`/`command`/`mcp`/`modes` + авто-одобрение | флаги `enableXxx` + `ToolPolicy` (autoApprove) |
| Исполнители | сервисы VS/ядра (`DTE2`, `RobotTemplateService`, `IAiProvider`, …) | Effect-сервисы Location (`FileAccess`, `Shell`, `WebSearch`, …) | VS Code extension host | инжектируются хостом (`executors.*`) |
| Расширяемость | фиксированный набор в `AgentToolFactory` | плагины (`Tool.Service.transform`) + MCP + плагины | custom tools + MCP | MCP + кастомные исполнители хоста |

## 5. Выводы

1. **Фокус и количество.** RPAWriter — 31 узкоспециализированный инструмент,
   завязанный на Visual Studio (`DTE2`, Error List, Output, буфер вкладки) и RPA-домен
   (спеки, роботы, база знаний, UiPath, memory bank). OpenCode — ~16 компактных
   универсальных инструментов на плагин-архитектуре. Roo-Code — 24 имени в стиле
   классического Cline (snake_case) с расширенной Roo-функциональностью (режимы,
   custom tools, codebase_search). Cline (SDK) — 9–12 универсальных инструментов;
   legacy VS Code-набор — 25.

2. **Стиль имён.** OpenCode — короткие имена (`read`, `write`, `edit`, `grep`, `glob`,
   `shell`). Cline/Roo — snake_case (`read_file`, `execute_command`, `write_to_file`).
   Cline SDK и RPAWriter — «плоские» snake_case с группирующими словами
   (`read_files`, `search_codebase`, `run_commands` / `run_command`). RPAWriter повторяет
   контракты Cline SDK (`read_files`, `search_codebase`, `list_files`, `apply_fix`),
   но в RPA-домене добавляет уникальные `*_spec`, `*_kb`, `*_robot`, `convert_uipath`.

3. **Редактирование файлов.** OpenCode — самый «гибкий»: `edit` (точная замена),
   `patch` (unified patch), `write` (полная запись). Cline — `editor` + `apply_patch`.
   Roo — `apply_diff` + `edit` + `apply_patch`. RPAWriter — три раздельных
   (`write_file`, `apply_fix`, `edit_open_document`), отражающих работу с
   несохранёнными буферами VS.

4. **Диалог и завершение.** Cline имеет явные `ask_question`/`submit_and_exit`
   (SDK) и `ask_followup_question`/`attempt_completion` (legacy). Roo —
   `ask_followup_question`/`attempt_completion`. OpenCode — `question` (несколько
   вопросов, multiple). RPAWriter — `ask_question` (один вопрос, 2–5 вариантов),
   финал через JSON `action:"final"` без отдельного инструмента.

5. **MCP.** OpenCode, Roo-Code и Cline поддерживают MCP-инструменты первоклассно
   (`mcp__*`, `use_mcp_tool`, `access_mcp_resource`). RPAWriter MCP не использует —
   вместо этого у него готовые git/сборка/логи/ошибки, завязанные на VS.

6. **Что есть только у одного:**
   - RPAWriter: спецификация/анализ, база знаний, логи, memory bank как инструменты,
     генерация/сборка/конвертация роботов, `download_file`.
   - OpenCode: `glob`, `webfetch` в форматах text/markdown/html, `worktree_*`, `models`.
   - Roo-Code: `switch_mode`, `new_task`, `codebase_search`, `update_todo_list`,
     `generate_image`, `custom_tool`, `read_command_output`.
   - Cline (legacy): `browser_action`, `list_code_definition_names`,
     `load_mcp_documentation`, `plan_mode_respond`/`act_mode_respond`, `report_bug`,
     `new_rule`.




