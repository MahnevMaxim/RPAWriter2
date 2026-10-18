# Progress

## What works
- Импорт и анализ ТЗ, декомпозиция на задачи.
- Генерация роботов (XAML + C#), база знаний, поиск похожих задач.
- UiPath → Workflow Foundation конвертация.
- Чат-агент с режимами Plan/Act, gating инструментов, историей (SQLite).
- Управление контекстным окном: transcript, оценка токенов, auto-compact, overflow/max-tokens recovery, «Compact now», прогресс-бар.
- Memory Bank: сервис + инструменты + кнопка.
- Tool calling: 2 режима — Native function calling и JSON over chat (настройка «Tool calling»).

## What's left
- Валидация на реальном VS с реальным провайдером.
- Опционально: больше интеграционных тестов.

## Known issues (см. todo.md)
- `SolutionTemplateEngine` не подключён.
- Удалённые логи (`RemoteLogService`) не подключены.
- ActivityLog/Event Log не читаются.
- `Flowchart` не поддерживается (понижается до Sequence).
- `InvokeMethod → Result → переменная` не реализовано.
- C#-методы генерируются без параметров (`// TODO: implement`).
