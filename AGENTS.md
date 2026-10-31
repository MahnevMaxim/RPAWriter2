# Agent Instructions

## Терминология (глоссарий)

- **контролы сообщений** — контрол-класс `ChatMessage` (`RPAWriter2/ChatMessage.cs`): одна запись в чате с агентом (текст пользователя/агента, tool call, вопрос, субагент, блоки кода). Именно этот класс подразумевается под термином «контролы сообщений».

## Уточнения

- Визуальный список сообщений рендерится контролом `ChatMessagesControl` (`RPAWriter2/Controls/ChatMessagesControl.xaml`), который принимает коллекцию `ObservableCollection<ChatMessage>`.
- Общение с пользователем — на русском; код, комментарии и интерфейс — на английском.

## Правила

- Запрещено добавлять в чат какие-либо сообщения (статусы, ошибки, предупреждения), если пользователь явно об этом не попросил. Для ошибок и статусов используй уведомления (`NotificationBanner` / `ShowNotice`), а не сообщения чата.

## Модели и MVVM

- Бизнес-логика вкладок живёт в моделях: `RPAWriter.Models` (`ChatModel`, `LogAnalyzer`, …) на базе `RPAWriter.Mvvm`. Полные правила — [documentation/13-model-layer.md](documentation/13-model-layer.md).
- Модель обязана создаваться и работать **без ViewModel и без View**: всё внешнее — интерфейсы в конструкторе, никакого WPF (`System.Windows`), `Dispatcher`, `MessageBox`, `Material.Icons`, SQLite и DTE внутри модели.
- Модель полностью покрывается тестами в `RPAWriter.Tests` (net8.0, NUnit). Нетестируемая логика выносится в `RPAWriter.Core` и тестируется там.
- Команды — `public`-методы без перегрузок (`Cmd` + `CommandParameter`). Сообщения пользователю — через `ShowDialogMessage` / `NotifyMessage`.
- Видимость панелей, фокус и прочее оформление — только во View/code-behind, в модель не попадают.
- `RPAWriter.Mvvm` и `RPAWriter.Models` не должны ссылаться на WPF, SQLite, DTE и на сборку `RPAWriter2` — это проверяет `ModelArchitectureTests`.
