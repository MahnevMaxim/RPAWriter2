# План реализации: маппинг аргументов PIX ↔ MWF в конвертере

Цель: в таблице **PIX → MWF map** добавить в RowDetails (кроме просмотра аргументов)
кнопку, по которой вместо текстового списка аргументов открывается контрол для
сопоставления аргументов друг с другом. Сопоставление учитывает типы и позволяет
задавать приведение (конверсию). Для этого нужно получать полные описания
активностей и всех их аргументов у PIX и у MWF, а данные для анализа отправлять
через нативные инструменты (AgentToolRegistry).

## Задачи

### 1. Модели данных (Core/Models/Models.cs + Core/Models/PixMwfMapping.cs)
- [ ] Расширить `PixActivityParameter`: `DisplayName`, `Description`, `IsRequired`, `DefaultValue` (сохранить `Pix`, `Target`, `Type`).
- [ ] Расширить `WfActivityParameter`: `DisplayName`, `Description`, `IsRequired`, `DefaultValue`, `Direction`.
- [ ] Добавить `PixMwfArgumentMapping` + `ConversionKind` (None/ToString/Parse/DateTimeFormat/Custom).
- [ ] Расширить `PixMwfMappingDocument`: поле `ArgumentMappings` (аддитивно, обратная совместимость).

### 2. Импортёры — полные описания аргументов
- [ ] `PixActivityLibraryImporter.ExtractParameters`: атрибуты свойства + XML-doc fallback → DisplayName/Description/IsRequired/DefaultValue.
- [ ] `MwfActivityLibraryImporter.ExtractParameters`: то же + явное `Direction` из `InArgument<T>/OutArgument<T>/InOutArgument<T>`.

### 3. Хранилище карты аргументов (Core/Services/PixMwfMappingStore.cs)
- [ ] `GetArgumentMappings`, `SetArgumentMapping`, `RemoveArgumentMapping`, `ClearArgumentMappings`.
- [ ] `Save`/`Load` пишут/читают `ArgumentMappings` в тот же JSON без ломки старых файлов.

### 4. UI маппинга аргументов в RowDetails (RPAWriter2/Controls)
- [ ] `PixMwfMappingRow`: `ArgumentMappingRowVm`-коллекция, синхронизация со Store, `IsArgumentMappingOpen`.
- [ ] `PixMwfMappingControl.xaml`: кнопка «Map arguments»/«Back to list»; два состояния RowDetails.
- [ ] Контрол маппинга: список PIX-аргументов + ComboBox MWF-аргументов + приведение (conversion/expression).
- [ ] Кнопка «Auto-match by name/type».

### 5. AI-режим: нативные инструменты (RPAWriter2/Services/AgentTools/AgentToolFactory.cs)
- [ ] `list_pix_mwf_activities` (read-only) — полные описания активностей и аргументов PIX/MWF.
- [ ] `map_pix_mwf_arguments` (mutating) — применить сопоставление аргументов.
- [ ] Связать с общим `PixMwfMappingStore`; кнопка «AI Map arguments» в UI.

### 6. Применение карты в конвертере (Core/Services/PixToWfConverter.cs)
- [ ] Принять аргументные сопоставления и подставлять значения с приведением (Conversion/ConversionExpression).
- [ ] Предупреждения о несопоставленных/несовместимых аргументах.

### 7. Тесты и документация
- [ ] Unit-тесты импортёров, хранилища, конвертера (RPAWriter.Tests).
- [ ] Обновить documentation/11-converter.md.

## Решения (принятые)
- Направление маппинга: **PIX → MWF** (каждая PIX-переменная выбирает MWF-аргумент).
- Приведение: `ConversionKind` enum + свободное C#-выражение `ConversionExpression`.
- JSON: расширяем существующий `pix-mwf-map.json` аддитивно.
- AI-маппинг аргументов: через общий `AgentToolRegistry` (нативные инструменты).

## Статус реализации
- ✅ Модели (`Models.cs`, `PixMwfMapping.cs`) — расширены аргументы + `ConversionKind`/`PixMwfArgumentMapping`/`ArgumentMappings`.
- ✅ Импортёры (`ActivityMetadata.cs` + оба импортёра) — описания/display-name/required/default + `Direction` для MWF.
- ✅ Хранилище (`PixMwfMappingStore`) — аргументная карта + сохранение/загрузка (обратная совместимость).
- ✅ UI (`PixMwfMappingRow` + `PixMwfMappingControl.xaml/.cs`) — кнопка «Map arguments», редактор сопоставления, «Auto-match».
- ✅ Нативные инструменты (`AgentToolFactory`) — `list_pix_mwf_activities` и `map_pix_mwf_arguments`; store подключён в `BuildToolRegistry`. Добавлена отдельная кнопка **AI Map arguments** в панель маппинга (маппит аргументы всех сопоставленных активностей); те же инструменты доступны и в чате (Act).
- ✅ Конвертер (`PixToWfConverter`) — приём карты аргументов, переименование + приведение; неизвестные активности тоже читают аргументы.
- ✅ Тесты (`PixMwfMappingStoreTests`, `PixToWfConverterTests`) — добавлены; весь набор 355 тестов зелёный.
- ✅ Документация (`documentation/11-converter.md`) — описан маппинг аргументов и нативные инструменты.
- ✅ Сборки: `RPAWriter.Core`, `RPAWriter.Tests` (dotnet) и `RPAWriter2` (VSIX, dotnet 10) — без ошибок.

