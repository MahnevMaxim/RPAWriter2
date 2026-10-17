# План реализации: модель управления контекстом в RPA Writer

> Основан на архитектуре Cline (`ClineContextModel.md`) и текущем коде RPA Writer.
> Статус: **план, согласован**.

## 0. Зафиксированные решения

1. **Суммаризацию делает та же модель** (отдельный системный промпт `agent-compact.txt`).
2. **Пороги по умолчанию** (всё конфигурируется):
   - `ReserveFraction = 0.12` — резерв под ответ модели (12% окна);
   - `CompactThresholdFraction = 0.85` — компакт при 85% заполнении;
   - `KeepRecentMessages = 6` — сколько последних сообщений не сжимать;
   - `ToolResultMaxChars = 12000` — обрезка результата инструмента;
   - `MaxIterations = 8` — лимит шагов агентного цикла.
3. **Роль `"tool"`** для результатов инструментов — по умолчанию `true`; настройка `UseToolRole` для отката на `"user"` (совместимость с Ollama/vLLM без нативного tool calling).
4. **Checkpoints — не делаем** (в VS есть Undo/git; `apply_fix` уже использует `TextDiff`).
5. **Memory Bank — отдельный план** (раздел 7).

## 1. Сопоставление Cline → RPA Writer (gap)

| Концепция Cline | Сейчас в RPA Writer | Действие |
|---|---|---|
| Task/session = единица контекста | `ChatHistoryStore` (SQLite) хранит беседы, но transcript не уходит в модель | Передавать полный transcript |
| system prompt + transcript + prepared request | Только system prompt (`AgentPromptBuilder`) | `ContextAssembler` |
| `prepareTurn` (конденсация) | Нет | `ContextAssembler.PrepareAsync` |
| Auto Compact (суммаризация) | Нет | `ContextCondenser` |
| Fallback rule-based truncation | Нет (жёсткий cap 12000) | `ContextCondenser` (усечение) |
| Context overflow recovery | Нет (ретраи без классификации) | `ProviderErrorException` + компакт + retry |
| Max-tokens output recovery | Нет (`finish_reason` не читается) | `finish_reason=="length"` + nudge + retry |
| Подсчёт токенов | Только отчёт провайдера (`ITokenUsageProvider`) | `TokenEstimator` (вход) |

## 2. Архитектурные принципы

- Вся контекст-логика — в `RPAWriter.Core` (netstandard2.0, без VS); UI-проводка — в `RPAWriter2`.
- Протокол инструментов остаётся «JSON через обычный чат» (без нативного function calling).
- `AgentRunner` принимает transcript и вызывает конденсацию **перед каждым** обращением к модели (аналог `prepareTurn`), не меняя сохранённую историю.

## 3. Новые/изменяемые файлы

### 3.1 Core — модели и контракты

`RPAWriter.Core/Models/Models.cs`:
- `ChatResponse` + `string FinishReason` (`"stop" | "length" | …`).
- `ChatMessageData.Role` поддерживает `"tool"`.
- `enum ProviderErrorKind { None, Auth, RateLimit, ContextWindowExceeded, ContentFilter, Transient, Other }`.

`RPAWriter.Core/Contracts/Contracts.cs`:
- `class ProviderErrorException : Exception { ProviderErrorKind Kind; int? StatusCode; string Body; }`.
- `interface ITokenEstimator { int Estimate(string systemPrompt, IReadOnlyList<ChatMessageData> messages); }`.
- `interface IContextCondenser { Task<IReadOnlyList<ChatMessageData>> CondenseAsync(IReadOnlyList<ChatMessageData> transcript, ContextBudget budget, CancellationToken ct); }`.

### 3.2 Core — контекст (`RPAWriter.Core/Context/`)

`ContextBudget.cs`:

```csharp
public sealed class ContextBudget
{
    public int ContextWindowSize { get; set; } = 409600;
    public double ReserveFraction { get; set; } = 0.12;
    public double CompactThresholdFraction { get; set; } = 0.85;
    public int KeepRecentMessages { get; set; } = 6;
    public int ToolResultMaxChars { get; set; } = 12000;
    public bool UseToolRole { get; set; } = true;
    public int MaxIterations { get; set; } = 8;

    public int ReserveTokens => (int)(ContextWindowSize * ReserveFraction);
    public int CompactThresholdTokens => (int)(ContextWindowSize * CompactThresholdFraction);
    public int MaxInputTokens => ContextWindowSize - ReserveTokens;
}
```

`TokenEstimator.cs`: консервативная оценка `⌈chars/4⌉` + фикс. оверхед на сообщение.

`ContextCondenser.cs` (реализует `IContextCondenser`):
1. **Суммаризация**: «старые» сообщения (всё, кроме последних `KeepRecentMessages`) → сводка через ту же модель (системный промпт `agent-compact.txt`, сохранить техдетали/решения/изменения кода); старые заменяются одним `assistant`-сообщением-сводкой.
2. **Fallback-усечение**: если суммаризация недоступна/ошиблась — отбрасывать самые старые сообщения, пока оценка не войдёт в `MaxInputTokens` (system prompt и последние `KeepRecentMessages` всегда сохраняются).

`ContextAssembler.cs` (аналог `prepareTurn`):

```csharp
public sealed class PreparedContext
{
    public string SystemPrompt;
    public IReadOnlyList<ChatMessageData> Messages;
    public bool WasCondensed;
    public int EstimatedInputTokens;
}

public sealed class ContextAssembler
{
    Task<PreparedContext> PrepareAsync(IReadOnlyList<ChatMessageData> transcript,
                                       string systemPrompt, ContextBudget budget,
                                       CancellationToken ct);
}
```

Логика: оценить вход → если `estimate > MaxInputTokens` или `estimate > CompactThresholdTokens` → `CondenseAsync` → вернуть подготовленный запрос.

### 3.3 Core — провайдер (`OpenAiCompatibleProvider.cs`)

- Классифицировать ошибки по статусу/телу и бросать `ProviderErrorException` (401/403 → `Auth`, 429 → `RateLimit`, «context length / too many tokens / maximum context / window» → `ContextWindowExceeded`, …).
- В SSE (`ReadSseStreamAsync`) и в не-SSE-ветке захватывать `choices[0].finish_reason` → `ChatResponse.FinishReason`.
- `StubAiProvider`: заглушки `FinishReason`, корректный `ContextWindowSize`, эмуляция переполнения для тестов.

### 3.4 Core — агентный цикл (`AgentRunner.cs`)

Новая сигнатура:

```csharp
Task<AgentRunResult> RunAsync(IReadOnlyList<ChatMessageData> history, string userText,
                              AgentMode mode, ContextBudget budget,
                              CancellationToken ct, IProgress<ChatStreamDelta> progress = null);
```

Поведение:
1. Собрать transcript = history + новое `user`-сообщение.
2. Перед каждым вызовом модели → `ContextAssembler.PrepareAsync`.
3. Результаты инструментов → роль `"tool"` (или `"user"`, если `UseToolRole=false`), обрезка `ToolResultMaxChars`.
4. **Overflow recovery**: `ProviderErrorException(Kind==ContextWindowExceeded)` → принудительный `CondenseAsync` → повтор **1 раз**; при повторном провале — понятная ошибка.
5. **Max-tokens recovery**: `FinishReason=="length"` и нет tool call → добавить «nudge» (лаконичнее) → повтор до `MAX_TOKENS_RECOVERY_LIMIT=3`; сброс при прогрессе.
6. Лимит шагов = `budget.MaxIterations`.

### 3.5 RPAWriter2 — UI и настройки

- `ToolWindow1Control.xaml.cs`:
  - конвертация `_messages` → `ChatMessageData` (роль `"agent"` → `"assistant"`, `"user"` → `"user"`, `"system"`/уведомления — пропускать);
  - передача transcript в `AgentRunner.RunAsync`;
  - уведомления в чат: «Context window exceeded — compacting…», «Context compacted»;
  - кнопка **«Compact now»** (ручной вызов конденсации);
  - прогресс-бар заполнения окна вместо текстовой строки.
- `Services/SettingsStore.cs`: + `AutoCompact`, `CompactThreshold`, `ReserveFraction`, `KeepRecentMessages`, `ToolResultMaxChars`, `UseToolRole`, `MaxIterations`.
- `ToolWindow1Control.xaml`: кнопка «Compact now», настройки порогов.
- `RPAWriter2/Prompts/System/agent-compact.txt` + `PromptDefaults.AgentCompactSystemPrompt`.

### 3.6 Тесты (`RPAWriter.Tests`)

- `TokenEstimatorTests` — монотонность, консервативность.
- `ContextCondenserTests` — суммаризация (stub), усечение сохраняет system prompt + последние N, бюджет соблюдается.
- `ContextAssemblerTests` — конденсация вызывается только при превышении порога.
- `AgentRunnerTests` — transcript передаётся; overflow → компакт + retry (однократно); max-tokens → nudge + retry (≤3); роль `"tool"`/`"user"`.
- `ProviderTests` — классификация ошибок, захват `finish_reason`.

## 4. Фазы и порядок работ

| Фаза | Содержание | Готово = | Статус |
|---|---|---|---|
| **0** | Модели/контракты: `FinishReason`, роль `"tool"`, `ProviderErrorKind/Exception`, `ITokenEstimator`, `IContextCondenser`, `ContextBudget` | компиляция Core | ✅ |
| **1** | Передача transcript: `AgentRunner` + UI-конвертация `_messages` | история уходит в модель | ✅ |
| **2** | `TokenEstimator` + `ContextBudget` + индикатор входа | оценка токенов до запроса | ✅ |
| **3** | `ContextAssembler` (prepareTurn) | конденсация вызывается перед запросом | ✅ |
| **4** | `ContextCondenser` (суммаризация + fallback-усечение) | Auto Compact работает | ✅ |
| **5** | Overflow recovery (классификация + компакт + retry) | переполнение восстанавливается | ✅ |
| **6** | Max-tokens recovery (`finish_reason`, nudge, retry) | обрезанный ответ восстанавливается | ✅ |
| **7** | UI: «Compact now», настройки порогов, прогресс-бар | управление контекстом из UI | ✅ |
| **8** | Тесты + `dotnet build` / `dotnet test` | всё зелёное | ✅ (132/132) |

## 5. Критерии приёмки

- Длинная беседа автоматически сжимается при приближении к 85% окна (видно уведомление в чате), работа продолжается без потери ключевых решений.
- При `context_window_exceeded` происходит однократный компакт + повтор; при повторном провале — внятная ошибка.
- Обрезанный по `length` ответ с nudge восстанавливается (≤3 попыток).
- Все пороги настраиваются в UI и персистятся (`SettingsStore`).
- `dotnet build` и `dotnet test` проходят; совместимость со stub-провайдером сохранена.

## 6. Открытые детали (минорные, решаются в ходе работ)

- Точный список ключевых слов для классификации `ContextWindowExceeded` (по телу ошибки) — зафиксировать в `ProviderErrorClassifier` и покрыть тестами.
- Формат сводки (сводка хранит: цель задачи, принятые решения, изменения кода, незавершённое) — зафиксировать в `agent-compact.txt`.

## 7. Memory Bank — ✅ реализовано

**Цель:** долговременный контекст между сессиями (модель «забывает» всё между запусками VS).

Структура в корне решения:

```text
memory-bank/
├── projectbrief.md     # суть RPA Writer, требования, scope
├── productContext.md   # зачем проект, проблемы, UX-цели
├── activeContext.md    # текущий фокус, недавние изменения, следующие шаги
├── systemPatterns.md   # архитектура, слои Core/RPAWriter2, паттерны
├── techContext.md      # стек, настройка, ограничения
└── progress.md         # что работает, что осталось, известные проблемы
```

Механика (по аналогии с Cline):
1. **Правила** в `.clinerules/memory-bank.md` (или conditional rules): «перед каждой задачей прочитать ВСЕ файлы memory-bank».
2. **Команды** (реализуются как агент-инструменты в `AgentToolRegistry`):
   - `read_memory` — read-only, прочитать `memory-bank/*.md`;
   - `update_memory` — mutating (Act), обновить файлы memory-bank после значимых изменений.
3. **Инициализация**: команда «initialize memory bank» создаёт структуру (существующие `RPAWriter.md`, `todo.md`, `AgentPlanActModes.md`, `ClineContextModel.md` мигрируют в неё как источники).
4. **Связь с Auto Compact**: ручная «update memory bank» — для важных чекпоинтов; Auto Compact — для рутинного управления окном.

**Объём Memory Bank:** ✅ `memory-bank/*.md` (6 core-файлов + `README.md`), сервис `MemoryBankService`, инструменты `read_memory`/`update_memory`, UI-кнопка «Memory bank», тесты. Инструкции — в `memory-bank/README.md` (вместо `.clinerules/memory-bank.md`, т.к. `.clinerules` в проекте — файл, а не директория).



