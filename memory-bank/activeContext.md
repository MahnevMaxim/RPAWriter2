# Active Context

## Current focus
Завершена реализация модели управления контекстом (по архитектуре Cline) и Memory Bank.

## Recent changes
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
- Протокол инструментов — «JSON через обычный чат» (без нативного function calling).
