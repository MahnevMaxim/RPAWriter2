# План: сохранять полный транскрипт агента как источник контекста (аналог Cline)

> Документ самодостаточен: его можно выполнять в «чистом контексте» без текущей истории
> чата. Здесь зафиксированы проблема, текущее устройство, целевое решение и конкретные шаги.

## 1. Цель

Устранить реальную потерю контекста между ходами агента: сейчас результаты инструментов и
промежуточные ответы-вызовы инструментов **не попадают** в транскрипт следующего запроса.
Дополнительно — сделать, чтобы индикатор «Context window» после завершения хода отражал
фактическое заполнение окна, а не застывал на значении последнего отправленного промпта.

Целевая модель повторяет подход Cline: полный «канонический» транскрипт
(`List<ChatMessageData>`, включая `assistant` tool-call и `tool`/`user` результаты) является
источником каждого следующего запроса, а видимый список `ChatMessage` остаётся только для UI.

## 2. Как устроено сейчас (факты)

### 2.1. Агентный цикл
`RPAWriter.Core/AgentTools/AgentRunner.cs`

- `RunAsync(...)` создаёт **локальную** переменную `messages` (`List<ChatMessageData>`) и
  дополняет её: `history + new user + assistant tool-call + tool result` (строки ~186-198).
- `RunLoopAsync(...)` мутирует этот `messages` по ходу цикла и возвращает только
  `AgentRunResult { FinalText, Reasoning, ToolBlocked, BlockedTool }` — сам `messages` **теряется**
  после `return`.
- При финальном ответе (`action.IsFinal`, строка ~286-296) цикл сразу возвращает результат и
  **не добавляет** финальный `assistant`-ответ в `messages`. В транскрипт попадают только
  `assistant`-сообщения с tool-call (строка ~299), а финальный текст — нет.
- Результаты инструментов добавляются в `messages`:
  - JsonChat: `Role = "tool"|"user"`, `Content = "Tool \"<name>\" returned:\n<result>"` (`ExecuteJsonToolCallAsync`).
  - Native: `Role = "tool"`, `ToolCallId = call.Id` (`ExecuteNativeToolCallAsync`).

### 2.2. Хост строит транскрипт следующего хода
`RPAWriter2/MainWindowControl.xaml.cs`

- `RunSingleTurnAsync` (строка ~2546) вызывает `var history = BuildTranscript();` и передаёт его в
  `_agentRunner.RunAsync(history, promptText, images, ...)`.
- `BuildTranscript()` (строки ~2785-2807) мапит в `ChatMessageData` **только**:
  `User` → `user`, `AgentText`/`ResultAnswerPlan`/`ResultAnswerAct` → `assistant`.
  Комментарий в коде прямо говорит: «Other kinds (tool calls, notices, errors) are UI-only and
  not part of the model conversation».
- `OnAgentToolExecuted` (строка ~3217) добавляет tool-вызовы в видимый список как
  `ChatMessageKind.ToolCall`/`ToolBlocked` (`AppendToolCall`), но эти сообщения не участвуют в
  `BuildTranscript`.
- Финальный ответ добавляется в UI как `ResultAnswerPlan`/`ResultAnswerAct`
  (`RunAgentTurnAsync`, строки ~2636-2646).

**Вывод:** внутри одного хода контекст полный (локальный `messages`), но после финального
ответа он выбрасывается, а следующий ход пересобирается только из видимых сообщений без
tool-результатов. Отсюда и «потеря контекста».

### 2.3. Индикатор контекстного окна
`RPAWriter2/MainWindowControl.xaml.cs`

- `ContextIndicator.UsedTokens` берётся из `_lastEstimatedInputTokens`
  (`UpdateTokenUsageDisplay`, строка ~895).
- `_lastEstimatedInputTokens` обновляется только в `OnPromptAssembled` (событие
  `IPromptNotifier.PromptAssembled`, строки ~818-824), то есть при сборке каждого запроса.
- После финального ответа новых сборок нет, поэтому цифра «замерзает» и не пересчитывается по
  фактическому транскрипту.

### 2.4. Персистентность (уже работает)
`RPAWriter.Models/ChatModel.cs` + `RPAWriter.Core/Services/ChatMessageMapper.cs`

- `ChatModel._fullHistory` — append-only буфер; `SaveHistoryNow()` пишет из него
  (`ToHistoryMessages`).
- `ChatMessageMapper.ToHistoryMessage` **уже сохраняет** tool-call: `ToolName` + `Result`
  (`ToolCallPayload`). Значит при перезагрузке беседы tool-результаты в UI восстанавливаются.
- Проблема не в хранении, а в том, что `BuildTranscript()` при пересборке транскрипта их
  игнорирует.

## 3. Целевое решение

1. `AgentRunner` возвращает наружу полный итоговый транскрипт (`AgentRunResult.Transcript`).
2. Хост хранит канонический транскрипт `_providerTranscript` (`List<ChatMessageData>`) и передаёт
   его в `RunAsync` вместо `BuildTranscript()`.
3. `BuildTranscript()` остаётся только как fallback для восстановления из сохранённой истории
   (и расширяется, чтобы включать tool-результаты).
4. Индикатор пересчитывается по каноническому транскрипту после завершения хода.
5. Ручной «Compact now» обновляет и канонический транскрипт, иначе компакт перестанет влиять на
   следующий ход.

### 3.1. Prompt cache (prefix caching)

План сознательно сохраняет **append-only** канонический транскрипт, чтобы максимизировать
cache-hit провайдера (DeepSeek/OpenAI кэшируют самый длинный общий префикс между запросами).

- Конец хода N: последний входной промпт был
  `[..., user_N, assistant(tool_call), tool_result]` (финальный `assistant(final_N)` — это выход
  модели, в промпт он не входил).
- Начало хода N+1: первый промпт — `[..., user_N, assistant(tool_call), tool_result, assistant(final_N), user_N+1]`.
- Общий префикс = весь предыдущий транскрипт → **полный cache-hit** на историю, cache-miss только
  на новый хвост `[assistant(final_N), user_N+1]`.

Для сравнения, текущая (до правок) схема `BuildTranscript()` на ходе N+1 даёт
`[user_1..user_N, final_N, user_N+1]`, а последний промпт хода N заканчивался на `tool_result`.
Общий префикс обрывается уже после `user_N` — то есть tool-результаты «выпадают» из кэша, и
кэш между ходами фактически ломается. Append-only канонический транскрипт это чинит.

Инварианты, которые нужно соблюсти при реализации:
- порядок и содержимое `_providerTranscript` между ходами не меняются (только дозапись);
- `RunAsync` делает `AddRange(history)` (shallow copy) — элементы `history` не должны мутироваться
  в цикле, иначе префикс изменится;
- сжатие (авто или ручное) по определению меняет префикс (старые сообщения → summary) и
  «сбрасывает» кэш до точки после summary — это ожидаемо и неизбежно.

## 4. Шаги

### Шаг 1 — `AgentRunner` возвращает полный транскрипт

Файл: `RPAWriter.Core/AgentTools/AgentRunner.cs`

1. В класс `AgentRunResult` добавить поле:
   ```csharp
   /// <summary>Full provider transcript after the run (user + assistant + tool results + final answer).</summary>
   public IReadOnlyList<ChatMessageData> Transcript { get; set; } = new List<ChatMessageData>();
   ```

2. В `RunLoopAsync` во **всех** точках возврата подставлять итоговый `messages`:
   - JSON final (`action.IsFinal`): перед `return` добавить финальный assistant-ответ:
     ```csharp
     messages.Add(new ChatMessageData { Role = "assistant", Content = action.Text });
     ```
   - Native final (ответ без `tool_calls`, строка ~262-264): аналогично добавить
     `response.Answer` как `assistant`.
   - `tool.CompletesRun` (Json/Native), stagnant-loop stop, лимит итераций, overflow/max-tokens
     recovery — везде заполнять `Transcript = messages`.
   - Удобно ввести приватный хелпер:
     ```csharp
     private static AgentRunResult Result(string finalText, string reasoning, IReadOnlyList<ChatMessageData> messages)
         => new AgentRunResult { FinalText = finalText ?? "", Reasoning = reasoning ?? "", Transcript = messages };
     ```
     и заменить все `new AgentRunResult { ... }` на вызов хелпера.

3. `ResumeAsync` уже получает `messages` из `snapshot.Messages` и передаёт в `RunLoopAsync`, поэтому
   возврат транскрипта работает и для resume без изменений.

> Внимание: не менять механику `ExecuteJsonToolCallAsync`/`ExecuteNativeToolCallAsync` — они уже
> добавляют assistant + tool result в `messages`; нужно лишь донести этот список до результата.

### Шаг 2 — Хост хранит канонический транскрипт

Файл: `RPAWriter2/MainWindowControl.xaml.cs`

1. Добавить поле рядом с существующим `_resumableSnapshot`:
   ```csharp
   // Canonical provider transcript of the last completed turn (Cline "apiConversationHistory"
   // equivalent). It is the source for the next RunAsync, not the visible Messages collection.
   private List<ChatMessageData> _providerTranscript = new List<ChatMessageData>();
   ```

2. В `RunSingleTurnAsync` заменить построение истории:
   ```csharp
   // было: var history = BuildTranscript();
   var history = _providerTranscript;
   ```
   `RunAsync` сам скопирует `history` и добавит новое user-сообщение, поэтому дублирования не
   будет.
   Передавать `_providerTranscript` как неизменяемый префикс: `RunAsync` делает `AddRange`
   (shallow copy), но элементы `history` не должны мутироваться в ходе цикла — это условие
   стабильности префикса для prompt cache (см. 3.1).

3. В `RunAgentTurnAsync` после `var result = await run(...)` (строка ~2608) закрепить транскрипт:
   ```csharp
   _providerTranscript = (result.Transcript ?? new List<ChatMessageData>()).ToList();
   ```
   То же самое сделать в `RunResumeTurnAsync` после `ResumeAsync`.

4. Сбрасывать `_providerTranscript` везде, где открепляется беседа/транскрипт:
   - `StartNewChat` (хост) и/или модель `StartNewChat`/`ResetConversation`;
   - `OnCloseChat` (`CloseActiveChat`);
   - `OnClearHistory` / смена воркспейса (`SetWorkspaceScope`);
   - при открытии другой беседы (`OpenConversation`) — пересобрать из загруженных `Messages`
     через расширенный `BuildTranscript()` (шаг 3).

   Минимальный вариант: ввести приватный метод `ResetProviderTranscript()` и вызывать его в тех же
   точках, где сейчас вызывается `ResetResume()` / где очищается видимый транскрипт.

### Шаг 3 — Fallback: `BuildTranscript()` включает tool-результаты

Файл: `RPAWriter2/MainWindowControl.xaml.cs` (`BuildTranscript`, строки ~2785-2807)

Расширить `switch`, чтобы при восстановлении из истории (когда `_providerTranscript` ещё пуст) не
терялись tool-результаты:

- Для `ChatMessageKind.ToolCall` / `ToolBlocked`:
  - добавить `assistant`-сообщение, обозначающее вызов инструмента (для JsonChat достаточно
    плоской формы, для Native — через `ToolCalls` невозможно восстановить аргументы, поэтому
    тоже плоская форма);
  - затем добавить сообщение-результат:
    ```csharp
    transcript.Add(new ChatMessageData { Role = "tool", Content = "Tool \"" + payload.ToolName + "\" returned:\n" + payload.Result });
    ```
    (для `ToolBlocked` — `Role = "user"`, `Content = payload.Result`/«blocked in Plan mode»).

> Ограничение fallback: исходные аргументы вызова не восстанавливаются (в UI хранится только имя
> + результат). Это приемлемо как запасной путь для старых/перезагруженных бесед; для текущей
> сессии используется точный `_providerTranscript`.

### Шаг 4 — Индикатор отражает фактическое заполнение

Файл: `RPAWriter2/MainWindowControl.xaml.cs`

1. В `UpdateTokenUsageDisplay()` при отсутствии активного запроса (`_chatCts == null`) вычислять
   `UsedTokens` по каноническому транскрипту через `TokenEstimator`, а не брать
   `_lastEstimatedInputTokens`:
   ```csharp
   var usedTokens = _chatCts == null
       ? new TokenEstimator().Estimate("", _providerTranscript)
       : _lastEstimatedInputTokens;
   ChatContextIndicator.UsedTokens = usedTokens;
   ```
   (системный промпт можно не учитывать или учитывать опционально — оценка и так консервативна).

2. Вызывать `UpdateTokenUsageDisplay()` после завершения/отмены хода (в `RunAgentTurnAsync` после
   `StopChatStatus()` и в `finally`), чтобы индикатор перерисовывался по новому транскрипту.

### Шаг 5 — Ручной «Compact now» обновляет канонический транскрипт

Файл: `RPAWriter2/MainWindowControl.xaml.cs` (`OnCompactNow`, строки ~2996-3029)

После получения `condensed` (`IReadOnlyList<ChatMessageData>` от `_agentRunner.CompactAsync`) помимо
`_chatModel.ApplyCompactedTranscript(compactedMessages)` также присвоить:

```csharp
_providerTranscript = condensed.ToList();
```

Иначе видимый список сожмётся, а следующий ход всё равно стартует с несжатого
`_providerTranscript`, и выигрыш от компакта пропадёт.

> Автосжатие (`ContextAssembler.PrepareAsync`) продолжает работать как раньше: оно сжимает
> локальную копию внутри каждого запроса. После перехода на `_providerTranscript` оно не «откатывает»
> канонический транскрипт — просто следующий ход стартует с полного транскрипта и снова сожмётся при
> превышении бюджета. Это существующее поведение, в рамках этого плана не меняется.

## 5. Граничные случаи

- **Отмена (Cancel):** отменённый ход не закрепляет `_providerTranscript` (ход не завершён);
  `AgentResumeSnapshot.Messages` по-прежнему используется для `ResumeAsync`. После resume
  `_providerTranscript` обновляется из `result.Transcript`.
- **Финальный ответ:** теперь добавляется в канонический транскрипт как `assistant`, поэтому
  следующий ход «видит» предыдущий ответ модели.
- **Начало новой беседы / открытие другой / закрытие:** `_providerTranscript` сбрасывается или
  пересобирается из `Messages` (fallback).
- **Смена воркспейса / очистка истории:** сброс `_providerTranscript` вместе с остальным состоянием.

## 6. Тесты

- Новый/расширенный тест в `RPAWriter.Tests/AgentToolsTests.cs` (или `AgentRunnerTranscriptTests.cs`):
  - после хода с tool-call + final `AgentRunResult.Transcript` содержит: user, assistant(tool-call),
    tool result, финальный assistant; порядок совпадает с ожидаемым.
- Тест на `BuildTranscript` с `ToolCall`/`ToolBlocked` (если логика выносится в тестируемое место —
  иначе проверить через хост сложнее; при необходимости вынести маппинг в Core, например в
  `ChatMessageMapper` или новый `ProviderTranscriptBuilder`, и покрыть тестами).
- Существующие `ChatMessageMapperTests` (round-trip tool-call) должны остаться зелёными — mapper не
  меняется.

## 7. Проверка

```powershell
dotnet build RPAWriter2.slnx
dotnet test RPAWriter2.slnx
```

Ручной сценарий:
1. Задать вопрос, который вызывает 1+ tool call, дождаться финального ответа.
2. Задать уточняющий вопрос со ссылкой на результат инструмента — агент должен «помнить»
   результат (раньше — не помнил).
3. Проверить, что индикатор «Context window» после завершения хода пересчитался и вырос/не
   застыл на старом значении.

## 8. Риски / открытые вопросы

- **Native tool calling:** полная структура вызова (аргументы) в UI не персистится, поэтому
  fallback-восстановление будет «плоским». Канонический `_providerTranscript` в памяти решает это
  для текущей сессии; для перезагруженных бесед — известное ограничение.
- **Автосжатие не закрепляется между ходами** (каждый ход заново сжимает полный транскрипт). Это
  отдельная оптимизация (в Cline сжатое состояние персистится через `session-compaction`). В этот
  план не входит, но после внедрения канонического транскрипта её можно добавить: сохранять
  сжатый `PreparedContext.Messages` обратно в `_providerTranscript`.
- **Дубликаты при resume:** убедиться, что `ResumeAsync` не добавляет user-сообщение повторно
  (он берёт `snapshot.Messages`, где user уже есть) — проверить тестом.



