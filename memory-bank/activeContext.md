# Active Context

## Current focus
Завершена поддержка DeepSeek context caching: парсинг cache-hit/miss токенов и расчёт стоимости по настраиваемым ценам (как у Cline).

## Recent changes
- **Кнопка «Do it!!!» читает поле ввода**: `DoItButton` переведён с `Command={Binding Cmd}` на `Click="OnDoItClick"` (как соседние `Compact now`/`Memory bank`/`Permissions`/`Docs`); обработчик в `MainWindowControl.xaml.cs` читает `InputBox.Text`, очищает поле и вызывает `ChatModel.DoItCommand(string)`. Модель переключает режим в Act и шлёт через `IChatPromptSink` промт `«<ввод>\nDo it!!!»` (пустой/пробельный ввод → просто `Do it!!!`). Сигнатура `DoItCommand()` → `public async Task DoItCommand(string)`; тесты `DoItCommand_*` обновлены + добавлены `DoItCommand_PrependsInputTextToTheCommand` и `DoItCommand_SendsBareCommandOnWhitespaceOnlyInput`.
- **Закрытие чата ≠ просмотр истории (ChatModel, §17.7)**: `ActiveChatConversation` — единственное определение активности, «закрыт» = `null`. `ResetToFreshConversation` заменён на `ClearConversationState` (только очистка транскрипта/очереди), добавлены `EnsureActiveConversation()` (идемпотентно активирует беседу и чистит остатки закрытого чата) и `CloseActiveChat()` (save → очистка → `null` → `ViewMode = History`). Конструктор больше не создаёт беседу; `ResetConversation()` (delete/clear активной) оставляет `null`; `ResolveConversationToOpen("")` больше не угадывает — `null`. Хост: ✕ → `OnCloseChat` закрывает беседу, «History» → `OnShowHistory` оставляет её активной, все внешние отправки («Send errors», «Send to RPA Writer», «Send exception to RPA Writer», баннеры) идут через `EnsureChatReadyToReceive()`; восстановление сессии с закрытым чатом показывает историю.
- **Футер чата на биндингах (фаза E)**: `Mode`/`Phase` больше не дублируются в хосте — `ModeToggle` связан двусторонне (`AgentModeToToggleValueConverter`), рамка `InputBox` следует `Chat.Mode` через `DataTrigger`, `Cancel`/`Do it!!!` — через `EnumMatchConverter` по `Chat.Phase`, `Resume task` — через проекцию `ChatModel.CanResume`. Запись «Mode: …» в транскрипт пишет модель (`ChatModel.SwitchMode`). Инварианты: имена режима — только `AgentModeNames` (Core); снимок возобновления остаётся в хосте, модель хранит лишь признак `HasResumableRun`.
- **Активность чата (одно определение)**: активная беседа — это ровно `ChatModel.ActiveChatConversation` (активна, пока не `null`); `ActiveConversationId`, `HasActiveConversation` и `ChatConversation.IsActive` — только производные проекции (не кэшировать, не персистить), `IsActive` пишет исключительно `UpdateActiveFlags()`. Инвариант подписан в XML-комментариях модели, `ChatConversation`, алиаса `_currentConversation` и `Ellipse` в карточке истории.
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
- **Одна активная беседа**: активность определяется только `ChatModel.ActiveChatConversation` (активна, пока не `null`); `ActiveConversationId`, `HasActiveConversation`, `ChatConversation.IsActive` и `UiStateData.LastConversationId` — производные/подсказки, второй источник состояния заводить нельзя.
- **Закрытие чата**: ✕ означает `ActiveChatConversation = null` (сначала `SaveHistoryNow()`), «History» — только смена `ViewMode`, беседа остаётся активной; внешние отправки сначала вызывают `EnsureActiveConversation()`. `null` — штатное состояние модели сразу после конструктора, поэтому транскрипт всегда принадлежит активной беседе.
