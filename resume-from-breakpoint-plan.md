# План: «Resume task» с места обрыва

## Цель

После нажатия **Cancel** в чате и завершения отмены показывать кнопку **«Resume task»** (стиль — точь-в-точь как у Cancel), которая продолжает ход агента **ровно с места обрыва**, а не запускает задачу заново.

## Контекст (как устроено сейчас)

- Кнопка Cancel: `ToolWindow1Control.xaml:785-790` (`Grid.Row="1"`, стиль `PrimaryButtonStyle` + `Background="Transparent" Foreground="White" BorderBrush="White" BorderThickness="1" Padding="10,2"`, `Click="OnCancelSend"`).
- Видимость: `SetPhase` (`ToolWindow1Control.xaml.cs:2621`) — `Visible` только при `AgentPhase.Streaming`.
- Отмена: `OnCancelSend` (`:2640`) → `_pendingMessages.Clear()` + `_chatCts?.Cancel()`.
- `RunSingleTurnAsync` (`:3181`): создаёт `cts`, `_chatCts = cts`, вызывает `_agentRunner.RunAsync(... cts.Token ...)`; ловит `OperationCanceledException` (`:3313`), пишет «Request cancelled.», `StopChatStatus()` → `SetPhase(Idle)`.
- **Проблема**: в `AgentRunner.RunAsync` транскрипт `messages` — локальная переменная (`:155-162`), растёт по ходу цикла (assistant + результаты tool call). При отмене выбрасывается. `BuildTranscript()` в UI tool call'ы не включает.

## Принцип выигрыша (токены + кэш)

Каждая итерация цикла отправляет заново весь транскрипт + system prompt. Провайдер кэширует по префиксу: неизменный префикс = cache-hit ($0.07/1M), новый хвост = cache-miss ($0.27/1M), вывод = $1.10/1M (не кэшируется).

- **С нуля**: повторная генерация всех assistant-сообщений (output-токены) + перевыполнение всех tool call'ов.
- **С места обрыва**: сохранённый префикс = cache-hit, генерируются только оставшиеся шаги; инструменты не перевыполняются.

## Шаги

### 1. XAML — кнопка Resume

В `ToolWindow1Control.xaml` добавить `Button x:Name="ResumeButton"` в ту же ячейку `Grid.Row="1"` (видимость взаимоисключающая с Cancel), `Content="Resume task"`, атрибуты стиля **идентичны** Cancel, `Click="OnResumeTask"`, `Visibility="Collapsed"`.

### 2. `AgentRunner` — сохраняемый транскрипт + возобновление

- Выделить цикл в возобновляемую форму: добавить `ResumeAsync(List<ChatMessageData> messages, AgentMode mode, ContextBudget budget, CancellationToken ct, IProgress<ChatStreamDelta> progress)` — продолжает с текущей итерации, не перестраивая историю.
- Отдавать наружу текущий `messages` + номер итерации: out-параметр/событие/`IProgress`-снапшот, который UI сохраняет до отмены.

### 3. UI — захват состояния и показ кнопки

- Поле `_resumableRun` (транскрипт + mode + budget) и флаг `_resumeAvailable`.
- Снапшот обновляется на каждой итерации (через колбэк из `AgentRunner`).
- `OnCancelSend`: выставить `_resumeAvailable = true` перед `_chatCts?.Cancel()`.
- В `catch (OperationCanceledException)`: если `_resumeAvailable`, показать `ResumeButton` (через `Dispatcher`).
- Сбрасывать кнопку/флаг при старте нового хода, `StartNewChat`, `OpenConversation`, `ResetCurrentConversation`.

### 4. Обработчик `OnResumeTask`

- Скрыть кнопку, снять флаг, убрать «Request cancelled.» из чата.
- Вызвать `_agentRunner.ResumeAsync(_resumableRun...)`, выводить результат/инструменты как обычно (и добавить новый ход в чат).

### 5. Нюансы

- Отмена во время стриминга → потерян только **один** частичный ответ модели; при resume он повторяется.
- Отмена во время tool call → этот tool call перезапускается (read-only безопасны; мутирующие — в основном идемпотентны, пометить в коде).
- Кнопка только после ручного Cancel; не при штатном завершении/ошибке.

### 6. Проверка

- `dotnet build RPAWriter2.slnx`.
- Ручной сценарий: Cancel в середине цепочки tool call'ов → Resume продолжает с той же итерации без повторения уже выполненных инструментов.
- По желанию: unit-тест на `AgentRunner.ResumeAsync` в `RPAWriter.Tests`.
