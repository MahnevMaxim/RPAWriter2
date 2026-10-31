# System Patterns

## Architecture
- **Слои**: `RPAWriter.Core` (netstandard2.0, без VS-зависимостей) + `RPAWriter2` (VSIX, net472, EnvDTE/VSSDK) + `RPAWriter.Tests` (net8.0, NUnit).
- **Провайдер**: `OpenAiCompatibleProvider` (POST /chat/completions), совместим с OpenAI/Azure/Ollama/LM Studio/vLLM; `StubAiProvider` — офлайн-заглушка.
- **Агент**: `AgentRunner` (агентный цикл) + `AgentToolRegistry` (per-mode gating: Plan = только чтение) + `AgentPromptBuilder`.

## Key decisions
- **Tool calling — 2 режима** (`ToolCallingMode`):
  - `JsonChat` — модель возвращает `{"action":"final"|"tool_call", ...}` (работает без нативного function calling; дефолт для Ollama/LM Studio/vLLM).
  - `Native` — OpenAI-style `tools` + `tool_choice:"auto"`, нативные `tool_calls`, роль `tool` + `tool_call_id`.
- **ToolSchemaConverter** — конвертирует неформальную схему `IAgentTool.ParametersSchema` (`{"path":"string"}`) в OpenAI JSON Schema.
- **Токены/стоимость** (`TokenUsage`): накапливает `PromptTokens`/`CompletionTokens`/`TotalTokens` плюс `CacheHitTokens`/`CacheMissTokens`/`EstimatedCostUsd`. `OpenAiCompatibleProvider` парсит DeepSeek `prompt_cache_hit_tokens`/`prompt_cache_miss_tokens` и OpenAI `prompt_tokens_details.cached_tokens`; стоимость — по настраиваемым ценам за 1M токенов (input/cache-hit/output).
- **Контекст-менеджмент** (по архитектуре Cline):
  - `ContextBudget` — пороги (reserve 12%, compact 85%, keepRecent 6, toolResult 12000).
  - `TokenEstimator` — оценка `⌈chars/4⌉`.
  - `ContextAssembler` — аналог `prepareTurn`.
  - `ContextCondenser` — суммаризация той же моделью + fallback-усечение.
  - `AgentRunner` — overflow recovery (компакт + retry) и max-tokens recovery (nudge + retry).
- **Memory Bank**: `MemoryBankService` + инструменты `read_memory`/`update_memory` — долговременный контекст между сессиями.

## Component relationships
- `ToolWindow1Control` → `AgentToolFactory` (реестр инструментов) → `AgentRunner` → `IAiProvider`.
- `AgentRunner` → `ContextAssembler` → `ContextCondenser` (та же модель для суммаризации).
- UI хранит беседы в `ChatHistoryStore` (SQLite); история передаётся модели как transcript.

## Model layer (MVVM)
- **Сборки**: `RPAWriter.Mvvm` (каркас: `BaseEntity`, `BaseModel`, `BaseViewModel`, `IView`, `RelayCommand`, `Notify`, `NotifyLevel`, `ViewMode`) и `RPAWriter.Models` (`ChatModel`, `LogAnalyzer`) — обе `netstandard2.0` без UI-зависимостей. `RPAWriter2/Mvvm/MvvmFactory.cs` — единственный WPF-адаптер (`Attach` для контролов, `CreateWindow` для окон).
- **Правила**: [documentation/13-model-layer.md](../documentation/13-model-layer.md). Ключевые: модель стартует без ViewModel и View; модель полностью покрыта тестами; состояние — `public`-свойства с `OnPropertyChanged`; команды — `public`-методы без перегрузок; UI только через события `ShowDialog`/`Notify`; layout state (`Visibility`) — во View, в модели только бизнес-условия.
- **Жизненный цикл**: `Run()` → `AfterLoaded()` (после `DataContext`) → `Loaded()` (view loaded) → `Closing()` (sync flush).
- **Дочерние модели**: `MvvmFactory.RegisterModel(parent, child)` не требует ViewModel у родителя; сообщения ребёнка транслируются в родителя.
- **Граница обеспечивается тестом**: `ModelArchitectureTests` падает, если в `RPAWriter.Mvvm`/`RPAWriter.Models` появится WPF, Material.Icons, SQLite, DTE или ссылка на `RPAWriter2`.
- **Стек тестов**: NUnit 3.14 + NUnit constraint model + ручные фейки (`InMemoryChatHistoryStore`, `FakeAiProvider`, `StubProdLogProvider`); NSubstitute — только при необходимости; coverlet, порог 90 % строк для `RPAWriter.Models`.
