# System Patterns

## Architecture
- **Слои**: `RPAWriter.Core` (netstandard2.0, без VS-зависимостей) + `RPAWriter2` (VSIX, net472, EnvDTE/VSSDK) + `RPAWriter.Tests` (net8.0, NUnit).
- **Провайдер**: `OpenAiCompatibleProvider` (POST /chat/completions), совместим с OpenAI/Azure/Ollama/LM Studio/vLLM; `StubAiProvider` — офлайн-заглушка.
- **Агент**: `AgentRunner` (агентный цикл) + `AgentToolRegistry` (per-mode gating: Plan = только чтение) + `AgentPromptBuilder`.

## Key decisions
- **JSON tool protocol**: модель возвращает `{"action":"final"|"tool_call", ...}` — работает без нативного function calling.
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
