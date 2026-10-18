# Active Context

## Current focus
Завершена поддержка DeepSeek context caching: парсинг cache-hit/miss токенов и расчёт стоимости по настраиваемым ценам (как у Cline).

## Recent changes
- **DeepSeek cache tokens / cost**: `TokenUsage.CacheHitTokens`/`CacheMissTokens`/`EstimatedCostUsd`; `OpenAiCompatibleProvider` парсит `prompt_cache_hit_tokens`/`prompt_cache_miss_tokens` и OpenAI `prompt_tokens_details.cached_tokens`, считает стоимость по ценам за 1M токенов. Настройки: Input/Cache-hit/Output price ($/1M). UI показывает cache hit и стоимость.
- **Native tool calling**: `ToolCallingMode` (Native | JsonChat), `ToolDefinition`/`NativeToolCall`, `ToolSchemaConverter`, `tools`+`tool_choice` в запросе, парсинг `tool_calls` (в т.ч. SSE-аккумуляция аргументов), native-промты (`agent-*-native.txt`), настройка «Tool calling». JsonChat — дефолт.
- Контекст: transcript передаётся модели, оценка токенов, `ContextAssembler` (prepareTurn), `ContextCondenser` (суммаризация + fallback-усечение).
- Восстановление: overflow recovery (`context_window_exceeded` → компакт + retry) и max-tokens recovery (`finish_reason=="length"` → nudge + retry).
- Роль `"tool"` для результатов инструментов (с фолбэком через настройку `UseToolRole`).
- UI: кнопки «Compact now» и «Memory bank», настройки порогов, прогресс-бар заполнения окна.
- Memory Bank: сервис `MemoryBankService`, инструменты `read_memory`/`update_memory`.

## Next steps
- При необходимости — полировка и дополнительные тесты.
- Валидация в реальном VS с реальным провайдером.

## Important patterns
- Вся контекст-логика — в `RPAWriter.Core` (netstandard2.0), UI — в `RPAWriter2`.
- Протокол инструментов: `Native` (нативные `tool_calls`) или `JsonChat` (JSON `{"action":"final"|"tool_call",...}`) — выбирается настройкой.
