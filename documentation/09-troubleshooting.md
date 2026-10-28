# 9. Диагностика и FAQ

## Частые проблемы

### Агент не отвечает / ошибка запроса

1. Проверьте **Base URL**, **API key** и **Model** на вкладке Settings.
2. Убедитесь, что провайдер доступен (интернет/локальный сервер).
3. Проверьте таймауты: **Request timeout (s)** и бюджет повторов **Retry budget (min)**.
4. Убедитесь, что провайдер совместим с OpenAI API (`POST /chat/completions`).

### «Spec import is only available when a solution is open»

Импорт ТЗ доступен только при открытом решении Visual Studio. Откройте решение и повторите.

### «The spec file is open in another application»

Файл ТЗ открыт в Word/Excel. Закройте его там и повторите импорт.

### «Commit is only available on the 'dev' branch»

Коммит разрешён только на ветке `dev`. Переключитесь на ветку `dev`.

### «No RPA- robot project found» / «Publish configuration is not configured» (deploy)

- Deploy требует наличия RPA-проекта в решении.
- Требуется конфигурация решения **Publish**. Создайте её: `Build → Configuration Manager`.

### Окно «file modified outside the editor»

Расширение записывает файлы (XAML, C#, спецификации) внешним процессом. Чтобы Visual Studio не показывал предупреждение, расширение автоматически включает **Reload modified files**. Подробности: [`Docs/ReloadModifiedFiles.md`](../Docs/ReloadModifiedFiles.md).

### Контекст быстро переполняется / ответы обрезаются

- Включите **Auto-compact (summarize)**.
- Уменьшите **Compact threshold**.
- Используйте кнопку **Compact now** для принудительного сжатия.
- Увеличьте **Context window (tokens)**, если провайдер поддерживает.

Подробнее: [Управление контекстом и память](10-context.md).

### Импорт PIX / WF активностей не находит сборки

- **Import PIX activities (rewrite library)** требует установленный PIX (`Program Files\PIX` или `Program Files (x86)\PIX`); если каталог не найден, укажите его вручную.
- **Get All MWF Activities** требует папку с WF-активностями (C#-исходники). Если выбран каталог с исходниками, он парсится как код; встроенные активности из `System.Activities.dll` при этом импортируются только выбранные в фильтре **Select System Activities** (а если список ещё не собирался — все). Сборки `Activities.*.dll` не сканируются.

### «Conversion aborted …» при конвертации PIX

Конвертер **Convert PIX to MWF** прерывается, если встречает активность без маппинга и без встроенного сниппета WF. Откройте таблицу **PIX to MWF map** и назначьте соответствие для указанной активности (вручную или кнопкой **AI Map**), затем повторите конвертацию.

### «Checkpoints are disabled because a nested git repository was detected»

Чекпоинты отключаются, если внутри открытого решения найден вложенный git-репозиторий (`.git` в подкаталоге, отличном от корня решения). Ровно один git-репозиторий в корне решения допустим — это обычный репозиторий робота. Подробнее об условиях отсутствия конфликтов см. [Чекпоинты](12-checkpoints.md).

## Где хранятся данные

Данные хранятся раздельно по версии Visual Studio: `17` — VS 2022 (17.x), `18` — VS 2026 (18.x).

| Данные | Расположение |
|---|---|
| Настройки | `%AppData%\RPAWriter2\<17|18>\settings.json` |
| Состояние UI | `%AppData%\RPAWriter2\<17|18>\ui_state.json` |
| Промпты | папка `Prompts` рядом со сборкой расширения (или `%AppData%\RPAWriter2\<17|18>\Prompts`) |
| Документация (Help) | папка `Docs` рядом со сборкой расширения (или `%AppData%\RPAWriter2\<17|18>\Docs`) |
| База знаний | каталог из настройки **KB directory** (`index.json`) |
| История чата | локальная SQLite-база (`%AppData%\RPAWriter2\<17|18>\chat_history.db`) |
| Маппинг PIX → MWF | `%AppData%\RPAWriter2\<17|18>\pix-mwf-map.json` |
| Системные активности WF (список и фильтр) | `%AppData%\RPAWriter2\<17|18>\system-activities.json` |
| Папки конвертера PIX → MWF | `%AppData%\RPAWriter2\<17|18>\pix-robot-conversion.json` |
| Чекпоинты (теневые git-репозитории) | `%LocalAppData%\RPAWriter2\<17|18>\tasks\<TaskId>\checkpoints` |

## Полезные команды чата

- **`Do it!!!`** — быстрая команда агенту выполнить действие (режим Act).
- **`send errors`** — отправить ошибки компиляции в чат.
- **`update memory bank: …`** — обновить банк памяти проекта (кнопка **Memory bank**).

## Сброс настроек

Чтобы вернуть настройки по умолчанию, закройте Visual Studio и удалите файл:

```
%AppData%\RPAWriter2\<17|18>\settings.json
```

При следующем запуске расширение создаст его заново со значениями по умолчанию.
