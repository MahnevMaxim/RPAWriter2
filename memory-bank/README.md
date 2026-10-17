# Memory Bank

Долговременный контекст проекта RPA Writer между сессиями. Модель «забывает» всё между запусками, поэтому перед каждой задачей нужно читать ВСЕ core-файлы.

## Structure
- `projectbrief.md` — базовый документ (требования, цели).
- `productContext.md` — зачем проект, проблемы, UX-цели.
- `activeContext.md` — текущий фокус, недавние изменения, следующие шаги (обновляется чаще всего).
- `systemPatterns.md` — архитектура, ключевые решения, паттерны.
- `techContext.md` — стек, настройка, ограничения.
- `progress.md` — что работает, что осталось, известные проблемы.

## Commands
- **initialize memory bank** — создать/проверить структуру (`MemoryBankService.EnsureInitialized`).
- **update memory bank** — перечитать и обновить файлы (через инструмент `update_memory`).
- **follow your custom instructions** — прочитать Memory Bank и продолжить с того места.

## Custom instructions (для AI-агента)
> При работе над проектом RPA Writer:
> 1. В начале каждой задачи прочитай ВСЕ файлы `memory-bank/*.md`.
> 2. После значимых изменений обнови `activeContext.md` и `progress.md` через `update_memory`.
> 3. Комментарии, код и интерфейс — на английском; общение с пользователем — на русском.
