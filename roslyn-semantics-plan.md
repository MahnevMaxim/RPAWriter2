# План разработки: внедрение Roslyn-семантики в RPAWriter2

Детальный план внедрения Roslyn-семантики в RPAWriter2, основанный на фактическом состоянии кода.

## 1. Целевая архитектура

Не кладём Roslyn-логику ни в `RPAWriter2` (net472 VSIX), ни в `RPAWriter.Core` (netstandard2.0, не должен зависеть от VS). Вводим новый слой, повторяющий уже существующий в проекте паттерн «чистая логика в netstandard2.0, тестируется из net8.0 без Visual Studio»:

```
RPAWriter2 (net472, VSIX)
   └─ RoslynWorkspaceService        — тонкий VS-слой: VisualStudioWorkspace из MEF,
                                      resolve Document по пути, вызовы чистого слоя
   └─ AgentToolFactory              — регистрирует 4 новых инструмента

RPAWriter.Roslyn (новый, netstandard2.0)   ← чистая логика, тестируется
   └─ DTO: OutlineEntry, SymbolUsage, InheritanceInfo, CodeDiagnostic
   └─ Форматтеры (сжатый текст для модели)
   └─ RoslynAnalysis (принимает Document/Solution/Compilation)

RPAWriter.Tests (net8.0)
   └─ юнит-тесты форматтеров через AdhocWorkspace + CSharpSyntaxTree
```

Это принципиально: `RPAWriter.Tests` ссылается только на netstandard2.0-проекты и не может ссылаться на net472-сборку `RPAWriter2`. Если бы форматтеры жили в VSIX, они остались бы без юнит-тестов.

## 2. Зависимости

- **`RPAWriter.Roslyn` (новый проект, netstandard2.0):** `PackageReference` на `Microsoft.CodeAnalysis.CSharp` и `Microsoft.CodeAnalysis.Workspaces.Common` (runtime, версия 4.x, совместимая с netstandard2.0). `Workspaces.Common` нужен для `Document`, `Solution`, `SymbolFinder`, `AdhocWorkspace`.
- **`RPAWriter2` (VSIX):**
  - `ProjectReference` на `RPAWriter.Roslyn`.
  - Roslyn-типы (`VisualStudioWorkspace` и др.) берём из установленной Visual Studio и не упаковываем их в VSIX (как уже сделано с `Microsoft.VisualStudio.SDK`). Сборки резолвим **от самой старой поддерживаемой VS — VS 2022 (17.x, Roslyn 4.x)**, а не от `vswhere -latest` (иначе получим VS 2026 / Roslyn 5.x, и VSIX перестанет грузиться в VS 2022).
  - Совместимость обеспечивают биндинг-редиректы `devenv.exe.config`: VS 2022 редиректит Roslyn `0.0.0.0–4.13.0.0` → `4.13.0.0`, VS 2026 — `0.0.0.0–5.10.0.0` → `5.10.0.0`. Ссылка на Roslyn 4.x проходит в обеих версиях; ссылка на 5.x в VS 2022 падает с `FileNotFoundException`.

## 3. Новые файлы

**`RPAWriter.Roslyn/`**
- `RPAWriter.Roslyn.csproj`
- `RoslynDtos.cs` — `CodeOutlineEntry` (Name, Kind, Signature, StartLine, EndLine, Children), `SymbolUsage` (FilePath, Line, Column, CodeLine), `InheritanceInfo` (BaseTypes, Interfaces, DerivedTypes, Implementations), `CodeDiagnostic` (Severity, Id, Message, File, Line, Column).
- `RoslynOutlineFormatter.cs` — форматирует `Document` → сжатый текст.
- `RoslynUsageFormatter.cs` — форматирует `ReferencedSymbol`/`ReferenceLocation` → текст.
- `RoslynInheritanceFormatter.cs` — форматирует `INamedTypeSymbol` + `SymbolFinder` → текст.
- `RoslynDiagnosticsFormatter.cs` — форматирует `ImmutableArray<Diagnostic>` → текст.
- `RoslynAnalysis.cs` — асинхронные чистые методы:
  - `BuildOutlineAsync(Document, CancellationToken)`
  - `FindSymbolUsagesAsync(Solution, string symbol, Document? scope, int maxResults, CancellationToken)`
  - `GetInheritanceAsync(Solution, string symbol, Document? scope, CancellationToken)`
  - `GetDiagnosticsAsync(Document, CancellationToken)`

**`RPAWriter2/Services/`**
- `RoslynWorkspaceService.cs` — получение `VisualStudioWorkspace`, резолв документа по пути, обёртки над `RoslynAnalysis`.

## 4. Получение VisualStudioWorkspace (ключевой фрагмент)

Через MEF, а не через создание собственного `MSBuildWorkspace` — именно это даёт «живую» картину с несохранёнными буферами:

```csharp
// VS 2022 (17.x) / VS 2026 (18.x), из AsyncPackage:
var componentModel = await _package.GetServiceAsync<SComponentModel, IComponentModel>();
var workspace = componentModel.GetService<VisualStudioWorkspace>();
```

Разрешение документа по пути:

```csharp
var ids = workspace.CurrentSolution.GetDocumentIdsWithFilePath(fullPath);
var doc = ids.Length > 0 ? await workspace.CurrentSolution.GetDocumentAsync(ids[0]) : null;
```

`CurrentSolution` уже содержит несохранённые изменения редактора, поэтому семантика актуальна — в отличие от `MSBuildWorkspace`, который читает диск.

## 5. Детализация четырёх инструментов

Все четыре — read-only, `permission: AgentPermissions.ReadFiles` (гейтятся уже существующим флажком «Read files»), регистрируются в read-only секции `CreateRegistry()`.

### 5.1 `code_outline`
- **Схема:** `{"path":"string"}`
- **Алгоритм:** resolve path → `RoslynWorkspaceService.GetDocumentAsync(path)` → `BuildOutlineAsync`:
  - обойти `SyntaxTree` (`CSharpSyntaxWalker` / `DescendantNodes`), собрать: namespaces (обычные и file-scoped), типы (`Class/Struct/Interface/Enum/Record/Delegate`), члены (`Method/Constructor/Destructor/Operator/Property/Indexer/Field/Event/EventField`).
  - Сигнатуру получать через `CSharpSyntaxRewriter`, который удаляет `BlockSyntax`-тела, `ArrowExpressionClause` и тела accessor-ов, затем `node.ToString()` — получаем сигнатуру без тел.
  - Для каждого узла — 1-based диапазон строк из `node.GetLocation().GetLineSpan()`.
- **Формат вывода (пример):**

```
namespace RPA.Crm
  class OrderService : IOrderService   (lines 12-180)
    string _connection                  (line 14)
    OrderService(string connection)     (line 16)
    Task<Order> GetOrderAsync(int id)   (lines 21-40)
    void Dispose()                      (lines 45-48)
```

- **Эффект:** для файла ~9k токенов вывод ~1k токенов (тела методов не попадают в контекст).

### 5.2 `find_symbol_usages`
- **Схема:** `{"symbol":"string","file?":"string","maxResults?":"integer"}` (default 100)
- **Алгоритм:**
  - если задан `file` — найти `Document`, через `RoslynAnalysis` найти объявление символа по имени в этом документе (по `SyntaxNode`-декларациям через `SemanticModel.GetDeclaredSymbol`, fallback `GetSymbolInfo`).
  - если `file` не задан — `SymbolFinder.FindDeclarationsAsync(project, symbol, ignoreCase:false)` по проекту.
  - затем `SymbolFinder.FindReferencesAsync(symbol, solution)`, взять `ReferenceLocation`s, сгруппировать по файлу, вывести строку/колонку + текст строки.
- **Формат вывода:**

```
Symbol: OrderService.GetOrderAsync
  RPA.Crm/OrderService.cs:34  await _repo.GetOrderAsync(id);
  RPA.Crm/OrderController.cs:52  var order = await _svc.GetOrderAsync(orderId);
  (2 references, truncated: false)
```

- Это компиляторно проверенные ссылки, а не grep.

### 5.3 `get_inheritance`
- **Схема:** `{"symbol":"string","file?":"string"}`
- **Алгоритм:** resolve `INamedTypeSymbol`:
  - вверх: `BaseType`, `Interfaces`, `AllInterfaces`;
  - вниз: `SymbolFinder.FindDerivedClassesAsync(type, solution)` для классов; для интерфейсов — `SymbolFinder.FindImplementationsAsync(interface, solution)`.
- **Формат вывода:**

```
Type: RPA.Crm.OrderService
  Implements: IOrderService, IDisposable
  Base: ServiceBase
  Derived (2): PremiumOrderService (RPA.Crm.Premium/...), CachedOrderService (...)
```

### 5.4 `get_diagnostics`
- **Схема:** `{"path?":"string"}` (пусто = активный документ через DTE; путь вне решения вернёт ошибку)
- **Алгоритм:** `SemanticModel.GetDiagnostics()` по документу (это фоновый Roslyn-анализ, без запуска сборки) → отфильтровать `Severity >= Warning`, ограничить (default 200), отформатировать.
- **Формат вывода:**

```
[RPA.Crm/OrderService.cs]
[CS8618] Warning: Non-nullable field '_repo' must contain a non-null value... (line 14, col 8)
[CS1998] Info: This async method lacks 'await'... (line 21, col 5)
(2 diagnostics)
```

- Дополняет, а не заменяет `read_errors` (тот остаётся для полного Error List + Output window).

## 6. Точки правок (по файлам)

1. **`RPAWriter2.slnx`** — добавить `<Project Path="RPAWriter.Roslyn/RPAWriter.Roslyn.csproj" />`.
2. **`RPAWriter.Roslyn/RPAWriter.Roslyn.csproj`** — новый проект (netstandard2.0, `Nullable disable`, Roslyn-пакеты).
3. **`RPAWriter.Roslyn/*.cs`** — DTO, форматтеры, `RoslynAnalysis`.
4. **`RPAWriter2/RPAWriter2.csproj`** — `ProjectReference` на `RPAWriter.Roslyn` + `Microsoft.VisualStudio.LanguageServices` (`ExcludeAssets="runtime"`). При необходимости — `VSIXSourceItem` для Roslyn-сборок (только если решим не брать их из VS).
5. **`RPAWriter2/Services/RoslynWorkspaceService.cs`** — новый сервис (MEF-импорт, resolve документа).
6. **`RPAWriter2/Services/AgentTools/AgentToolFactory.cs`**:
   - добавить поле и параметр конструктора `RoslynWorkspaceService roslyn`;
   - в `CreateRegistry()` зарегистрировать 4 инструмента с описаниями и схемами (в read-only секции);
   - добавить методы `CodeOutlineAsync`, `FindSymbolUsagesAsync`, `GetInheritanceAsync`, `GetDiagnosticsAsync` (+ короткие `*DisplayAsync` для строки прогресса: путь/символ).
7. **`RPAWriter2/MainWindowControl.xaml.cs`** (метод `BuildToolRegistry`, строки ~3507) — создать/передать `RoslynWorkspaceService`.
8. **Промпты** (`Prompts/System/agent-system.txt` и native-варианты) — вписать правило: «для навигации по C# сначала `code_outline`/`find_symbol_usages`/`get_inheritance`/`get_diagnostics`; полный текст через `read_files` запрашивать только перед редактированием файла».
9. **`RPAWriter.Tests/RPAWriter.Tests.csproj`** — `ProjectReference` на `RPAWriter.Roslyn`.
10. **Тесты** — новые `RoslynOutlineFormatterTests.cs`, `RoslynUsageFormatterTests.cs`, `RoslynInheritanceFormatterTests.cs`, `RoslynDiagnosticsFormatterTests.cs` (через `AdhocWorkspace` + `CSharpSyntaxTree.ParseText`, без VS).

## 7. Тестирование

- **Юнит (net8.0, без VS):** подаём в `RoslynAnalysis` синтетические `Document` из `AdhocWorkspace`; проверяем, что outline не содержит тел методов, usages возвращает корректные строки, inheritance/диагностики форматируются стабильно.
- **Интеграция (вручную через F5 в VS 2026):** открыть C#-файл с ошибкой и несохранёнными изменениями → вызвать `code_outline` (должен видеть несохранённый текст), `find_symbol_usages`, `get_inheritance`, `get_diagnostics`; убедиться, что `read_files` по-прежнему работает как fallback.

## 8. Риски и ограничения

1. **Версия Roslyn-сборок.** Один VSIX должен работать и в VS 2022, и в VS 2026, поэтому компилироваться нужно против Roslyn из **VS 2022 (17.x, Roslyn 4.x)** — самой старой поддерживаемой версии. Биндинг-редиректы `devenv.exe.config` подтягивают старые версии к актуальной: VS 2022 — `0.0.0.0–4.13.0.0`, VS 2026 — `0.0.0.0–5.10.0.0`. Ссылка на 5.x (из `vswhere -latest` = VS 2026) в VS 2022 не загрузится. Проверить первым шагом. Свойство резолва называть **не `VsInstallRoot`** (его `MSBuild.exe` из VS задаёт глобально и оно всегда указывает на «свой» VS), а уникальным именем, например `RoslynVsInstallRoot`.
2. **`SymbolFinder.FindReferencesAsync` / `FindDerivedClassesAsync`** работают по всему решению и могут быть медленными — обязательно ограничивать `maxResults` и использовать `CancellationToken`.
3. **Roslyn покрывает только `.cs`.** XAML-роботы (основной формат RPA) семантикой не анализируются — для них остаётся `read_files`/`read_robot_sources`. Инструменты сразу должны честно отвечать «not a C# document» для `.xaml`.
4. **`CurrentSolution` даёт документы открытого решения** — файлы вне solution (`robots root`, KB) через Roslyn недоступны; для них остаются существующие файловые инструменты. Поэтому это оптимизация именно для C#-кода внутри открытого решения.
5. **Упаковка VSIX (`CreatePkgDef`).** Пакет `System.Reflection.MetadataLoadContext 8.0` кладёт в выходную папку `System.Reflection.Metadata`/`System.Collections.Immutable`/`System.Reflection.MetadataLoadContext` 8.x, а `CreatePkgDef` из VSSDK 18.x собран против `System.Reflection.Metadata` 10.x — это даёт `TypeLoadException ... GetGenericInstantiation`. Лечится target'ом, который исключает эти три сборки из `ReferenceCopyLocalPaths`/`ReferencePath` перед `GeneratePkgDef` (сами dll для runtime VSIX остаются на месте).

## 9. Рекомендуемый порядок реализации

1. Проверить/зафиксировать резолв Roslyn под VS 2022 (17.x, Roslyn 4.x) + VS 2026 (18.x, Roslyn 5.x) на net472: в `ResolveRoslynReferences` выбирать VS 2022 (`-version [17.0,18.0)`), а не `-latest`, с fallback на последнюю.
2. Создать `RPAWriter.Roslyn`, подключить к решению и тестам.
3. Реализовать `code_outline` (наибольшая экономия токенов) + `RoslynWorkspaceService` + регистрацию в фабрике.
4. Реализовать `get_diagnostics`, затем `find_symbol_usages`, затем `get_inheritance`.
5. Юнит-тесты на форматтеры.
6. Обновить системные промпты и проверить сценарий «outline → точечный read_files → edit» вживую.

Если хотите, следующим шагом я могу детализировать любой из этих этапов до уровня конкретных сигнатур методов и текста описаний инструментов для `AgentToolFactory` — например, начать с `RoslynWorkspaceService` + `code_outline`.
