# План исправления: фриз интерфейса при выполнении инструментов (RPAWriter2)

Статус: **план / задокументировано, код не менялся**.
Целевая версия пакетов: **1.20** (итоговая сборка `1.20.0.1` — см. раздел «Версия и упаковка»).

---

## 1. Симптомы

- Во время выполнения агентских инструментов интерфейс RPAWriter2 (VSIX tool window) замерзает.
- Кнопка **Cancel** перестаёт отзываться; срабатывает только после «отмерзания».
- **Steering** (подпитка/перехват очереди сообщений) тоже замерзает.
- **TextBox** ввода сообщения кажется зависшим.
- Замерзание повторяется «при выполнении любых инструментов», а не только тяжёлых.

Важное уточнение из диагностики: **отрисовка здесь ни при чём** — в момент фриза новые сообщения не рендерятся. Блокируется сам UI-поток VS, а не разметка/подсветка.

---

## 2. Корневая причина

Агентский цикл (`AgentRunner`) выполняется на фоновом потоке и внутри себя использует `ConfigureAwait(false)`.
Но инструменты, которым нужен доступ к состоянию Visual Studio (DTE, буферы редактора, Error List, Output window, Roslyn workspace), вызывают:

```csharp
await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);
```

и после этого выполняют **всё тело инструмента** на главном (UI/STA) потоке Visual Studio. В VSIX главный поток один на весь IDE, поэтому любая синхронная работа на нём блокирует и чат-тул-окно: Cancel, steering и TextBox.

Дополнительно `Package.GetGlobalService(...)` / работа с DTE-коллекциями принципиально должны выполняться на главном потоке, но в текущем коде тяжёлая часть (чтение файлов, обход Error List, чтение Output window, форматирование/обрезание строк) выполняется там же, где и «тонкий» доступ к VS.

### Ключевые нарушители (файл: `RPAWriter2/Services/AgentTools/AgentToolFactory.cs`)

| Инструмент | Метод | Что делает на UI-потоке |
|---|---|---|
| `read_files` | `ReadFilesAsync` | После `SwitchToMainThreadAsync` весь цикл: резолв путей, проверки доступа, чтение буфера редактора **и** чтение с диска (`File.ReadAllText`), форматирование, обрезка. Агент обычно передаёт сразу несколько файлов → длительный фриз. |
| `read_errors` | `ReadErrorsAsync` → `BuildFeedbackService.GetCompilationErrorsAsync` | Полный обход `DTE.ToolWindows.ErrorList.ErrorItems` на главном потоке. |
| `read_output` | `ReadOutputAsync` → `BuildFeedbackService.GetOutputWindowTextAsync` | Читает **весь** текст панели Output на главном потоке (`editPoint.GetText(...)`), потенциально десятки МБ. |
| `open_file_in_visual_studio` | `OpenFileAsync` | DTE-доступ и открытие документа на главном потоке. |
| `read_open_document` | `ReadOpenDocumentAsync` / `ReadOpenDocumentDisplayAsync` | DTE + чтение буфера на главном потоке. |
| `editor` | `EditorAsync` | DTE + чтение/запись буфера + дисковый фолбэк + строковая обработка на главном потоке. |
| `get_diagnostics` | `GetDiagnosticsAsync` (путь «активный документ») | DTE-доступ к `ActiveDocument` на главном потоке. |
| `build` | `BuildAsync` → `BuildFeedbackService.BuildAsync` | Синхронный `SolutionBuild.Build(true)` на главном потоке (ожидаемо блокирует — отдельный пункт). |

### Ключевой нарушитель (файл: `RPAWriter2/Services/RoslynWorkspaceService.cs`)

- `Workspace` (getter) лениво вызывает `Package.GetGlobalService(typeof(SComponentModel))` **с фонового потока** при первом обращении из Roslyn-инструментов (`code_outline`, `find_symbol_usages`, `get_inheritance`, `get_diagnostics`).
- `GetGlobalService` должен вызываться на главном потоке; текущее ленивое разрешение — это и источник потенциального зависания/ошибок, и нарушение контракта VS.

---

## 3. Принцип исправления

**На главном потоке выполнять только минимально необходимый доступ к VS, а всё тяжёлое (файловый ввод/вывод, строковую обработку, форматирование, обрезку, анализ) уводить обратно на фоновый поток.**

Общая схема для каждого инструмента:

```csharp
// 1. Фон: подготовить/провалидировать вход.
// 2. SwitchToMainThreadAsync — получить МИНИМАЛЬНЫЙ снимок из VS (буфер, список ошибок, текст панели).
// 3. Вернуться на фон (Task.Run / ConfigureAwait(false)) и выполнить тяжёлую обработку снимка.
```

Дополнительно:
- Резолвить `VisualStudioWorkspace` **один раз на главном потоке** при инициализации, дальше читать только thread-safe снимок `CurrentSolution`.
- Тяжёлые главнопоточные чтения (Output window, Error List) ограничивать объёмом (последние N строк / первые N записей) или читать чанками с уступкой диспетчеру.

---

## 4. Задачи исправления

### Задача 1 — `read_files`: разделить UI-доступ и файловую работу

Файл: `RPAWriter2/Services/AgentTools/AgentToolFactory.cs`, метод `ReadFilesAsync`.

1. На фоне: распарсить `files`, резолвить пути, проверить `IsReadAllowed`.
2. На главном потоке выполнить только: `FindOpenDocument` + чтение буферов открытых документов в лёгкий словарь `Dictionary<string, (string text, string source)>`.
3. Вернуться на фон: прочитать оставшиеся файлы с диска (`File.ReadAllText`), применить `FormatReadFilesResult`, обрезку.
4. Убедиться, что `ReadFromDiskOrNull` / `Truncate` не вызываются на главном потоке.

### Задача 2 — `read_errors`: быстрый снимок Error List

Файл: `RPAWriter2/Services/BuildFeedbackService.cs`, метод `GetCompilationErrorsAsync`.

1. На главном потоке собрать сырые `BuildError` из `ErrorItems`.
2. Форматирование/фильтрацию/сериализацию выполнить на фоне в `AgentToolFactory.ReadErrorsAsync`.
3. При большом числе ошибок — ограничить/пагинировать (например, первые N + примечание), чтобы главнопоточный обход не разрастался.

### Задача 3 — `read_output`: ограничить/разбить чтение Output window

Файл: `RPAWriter2/Services/BuildFeedbackService.cs`, метод `GetOutputWindowTextAsync`.

1. Не читать весь документ панели целиком за один `GetText`.
2. Читать последние N строк/КБ (или чанками) на главном потоке, с периодической уступкой диспетчеру.
3. Обрезку до лимита выполнять на фоне.

### Задача 4 — `editor` / `open_file` / `read_open_document`: минимальный UI-доступ

Файл: `RPAWriter2/Services/AgentTools/AgentToolFactory.cs`, методы `EditorAsync`, `OpenFileAsync`, `ReadOpenDocumentAsync`.

1. На главном потоке — только DTE-операции и чтение/запись буфера.
2. Дисковый ввод/вывод, резолв пути, форматирование, сравнение/запись — на фоне.
3. `EditorAsync` писать файл на фоне, а не на главном потоке.

### Задача 5 — Roslyn: резолвить workspace на главном потоке один раз

Файл: `RPAWriter2/Services/RoslynWorkspaceService.cs`.

1. Заменить ленивый `Workspace`-getter, вызывающий `Package.GetGlobalService` с фонового потока, на явную инициализацию на главном потоке (при старте tool window или при первом обращении через `SwitchToMainThreadAsync`).
2. Кэшировать `VisualStudioWorkspace`.
3. `GetDocumentAsync` / `GetSolution` оставить чтением только `CurrentSolution` (thread-safe snapshot), без повторного обращения к `Package.GetGlobalService`.
4. Проверить, что `RoslynAnalysis.*` (`BuildOutlineAsync`, `FindSymbolUsagesAsync`, `GetInheritanceAsync`, `GetDiagnosticsAsync`) работают с документом/решением, а не с workspace, и не заходят на главный поток повторно.

### Задача 6 — `build`: зафиксировать поведение отдельно

Файл: `RPAWriter2/Services/BuildFeedbackService.cs`, метод `BuildAsync`.

- Синхронный `DTE SolutionBuild.Build(true)` обязан выполняться на главном потоке и блокирует UI по своей природе.
- Решение: оставить блокирующим, но перед запуском выставить явный статус «building…», а при возможности перейти на `IVsSolutionBuildManager`/асинхронные события VS. Как минимум задокументировать, что `build` — известное исключение из «не блокировать UI».

---

## 5. Проверка (критерии приёмки)

Ручные сценарии в RPAWriter2:

1. `read_files` с 3–10 файлами (включая открытые в редакторе и файлы с диска) — UI отзывчив, Cancel реагирует сразу.
2. `read_output` после большой сборки — UI отзывчив.
3. `read_errors` при десятках/сотнях ошибок — UI отзывчив.
4. `code_outline` / `find_symbol_usages` / `get_inheritance` / `get_diagnostics` — отрабатывают без зависаний и без ошибок потока.
5. Во время выполнения любого инструмента: Cancel, steering и TextBox ввода остаются кликабельными.

Регрессионные проверки:

- Существующие тесты `RPAWriter.Tests` (ProviderTests, AgentToolsTests, ToolCallingTests) проходят.
- Новые юнит-тесты на чистые помощники форматирования/обрезки (если вынесены) и на фоновые пути чтения (при необходимости).

---

## 6. Версия и упаковка (цель — 1.20)

### 6.1. Объявление версии

Файл: `C:\Users\Maxxx\source\repos\AgentShared\Directory.Build.props`

```xml
<Version>1.20.0</Version>
```

(текущее значение — `1.19.1`).

### 6.2. Итоговая версия пакетов

Канонический путь упаковки — `PackTool`:

```powershell
cd C:\Users\Maxxx\source\repos\AgentShared
dotnet run --project PackTool\PackTool.csproj -c Release -- .
```

Логика `PackTool/VersionBumper.cs` берёт максимум из версий в `feed\` и объявленной версии, затем **инкрементирует trailing-компоненту**. При объявленной `1.20.0` и текущем максимуме `1.19.1.3` результатом будет:

- `AgentToolkit.1.20.0.1.nupkg`
- `AgentToolkit.Wpf.1.20.0.1.nupkg`

То есть **итоговая версия для потребителей — `1.20.0.1`**.

Альтернатива (если нужна строго `1.20.0` без `.1`): собрать напрямую:

```powershell
dotnet pack AgentToolkit\AgentToolkit.csproj -c Release -p:PackageVersion=1.20.0 -o feed
dotnet pack AgentToolkit.Wpf\AgentToolkit.Wpf.csproj -c Release -p:PackageVersion=1.20.0 -o feed
```

Рекомендуется основной путь через `PackTool` (`1.20.0.1`), так как он уже используется для `1.19.1.3`.

### 6.3. Сборка перед упаковкой

```powershell
cd C:\Users\Maxxx\source\repos\AgentShared
.\pack.ps1
```

(`pack.ps1` собирает проекты с `-p:AgentSkipReleasePack=true` и пакует; для строгой версии `1.20.0.1` предпочтительнее запустить `PackTool`, либо `pack.ps1` после правки `Directory.Build.props` — проверить фактически полученную версию в `feed\`.)

---

## 7. Обновление потребителей пакета

После публикации `1.20.0.1` в локальный feed (`AgentShared\feed\`):

| Проект | Файл | Что менять |
|---|---|---|
| RPAWriter2 (VSIX) | `RPAWriter2\RPAWriter2.csproj` | `AgentToolkit` + `AgentToolkit.Wpf` → `1.20.0.1` (сейчас `1.19.1.3`). |
| RPAWriter2 — RPA-слой | `RPAWriter2\Agent.Rpa\Agent.Rpa.csproj` | `AgentToolkit` → `1.20.0.1` (сейчас `1.18.0`). |
| RPAWriter2 — тесты | `RPAWriter2\RPAWriter.Tests\RPAWriter.Tests.csproj` | `AgentToolkit` → `1.20.0.1` (сейчас `1.18.0`). |
| AI-RPA — чат | `C:\Users\Maxxx\Desktop\AI-RPA\RPA.Chat\RPA.Chat.csproj` | `AgentToolkit` + `AgentToolkit.Wpf` → `1.20.0.1` (сейчас `1.19.1`). |
| AI-RPA — общий слой | `C:\Users\Maxxx\Desktop\AI-RPA\RPA.Shared\RPA.Shared.csproj` | `AgentToolkit` → `1.20.0.1` (сейчас `1.19.1`). |
| AI-RPA — студия | `C:\Users\Maxxx\Desktop\AI-RPA\AiRpaStudio\AiRpaStudio.csproj` | `HintPath` `..\packages\AgentToolkit.1.19.1.3\...` → `1.20.0.1`; то же для `AgentToolkit.Wpf`; положить/распаковать новые `.nupkg` в `..\packages\`. |
| VibeCoder (опционально) | `VibeCoder\VibeCoder\VibeCoder.csproj`, `VibeCoder.Core\VibeCoder.Core.csproj` | `AgentToolkit` → `1.20.0.1` (сейчас `1.14.0`). |
| smoke (опционально) | `AgentShared\smoke\smoke.csproj` | `AgentToolkit` → `1.20.0.1` (сейчас `1.8.0`). |

---

## 8. Порядок выполнения

1. Правки в `RPAWriter2` (Задачи 1–5, по необходимости 6) — код не менялся в рамках этого документа.
2. Правка `Directory.Build.props` → `1.20.0`.
3. Сборка + упаковка `AgentToolkit` / `AgentToolkit.Wpf` → `1.20.0.1` в `feed\`.
4. Обновление `PackageReference`/`HintPath` у потребителей (раздел 7).
5. Полная сборка RPAWriter2 + AI-RPA, ручная проверка сценариев (раздел 5).
6. Прогон тестов `RPAWriter.Tests`.

---

## 9. Риски и примечания

- `SwitchToMainThreadAsync` нельзя убирать совсем: DTE-объекты (буферы, Error List, Output window, SolutionBuild) обязаны жить на главном потоке VS. Меняется только **объём** работы на главном потоке.
- Roslyn-снимок (`CurrentSolution`) потокобезопасен для чтения/анализа, но любые вызовы `Package.GetGlobalService` и обращение к `VisualStudioWorkspace` должны быть на главном потоке.
- При чтении Output window чанками важно сохранить порядок и не «порвать» многострочные записи; проще читать последние N строк/КБ.
- `build` останется блокирующим (ограничение DTE); это выносится в отдельное осознанное поведение, а не в общий фикс отзывчивости.
- Данный план не отменяет два ранее выявленных смежных дефекта:
  - ошибку `CollectionView` при записи в `ObservableCollection` из фонового потока (маршалинг/`EnableCollectionSynchronization`);
  - троттлинг потокового reasoning.
