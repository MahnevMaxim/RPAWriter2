# План: перенос вкладки Converter в отдельное окно инструментов Visual Studio

## Цель

Вынести функционал вкладки **Converter** из общего окна «RPA Writer» (`ToolWindow1`) в
отдельное окно инструментов Visual Studio. Новое окно должно открываться **в области
документов** (посередине, где отображаются вкладки открытых файлов) — `VsDockStyle.MDI`.

## Контекст

- `RPAWriter2` — VSIX-расширение (in-proc, `net472`), модель `AsyncPackage` + `ToolWindowPane`
  + `[ProvideToolWindow]`, команды через `.vsct`.
- Сейчас окно `ToolWindow1` («RPA Writer») содержит `ToolWindow1Control` с `TabControl MainTabs`
  и вкладкой **Converter** (`ToolWindow1Control.xaml`, строки 1239–1280).
- Логика конвертера сосредоточена в `ToolWindow1Control.xaml.cs` (методы `OnConvertUiPath`,
  `OnConvertPix`, `OnGetAllActivitiesFromPix`, `OnImportPixActivities`, `OnGetAllMwfActivities`
  и вспомогательные; вывод — `ChatMessagesControl ConverterMessages`; маппинг —
  `PixMwfMappingControl`).
- Важная связка: нативные инструменты чата (`list_pix_mwf_activities`, `map_pix_mwf_arguments`)
  получают хранилище маппинга через `factory.PixMwfStore = PixMwfMapControl?.Store`
  (`RebuildAgentTools`). После переноса чат и окно конвертера должны использовать **один**
  экземпляр `PixMwfMappingStore`.

## Решения (принятые)

1. Вкладка **Converter** полностью удаляется из главного окна.
2. Новое окно: класс `ConverterToolWindow`, заголовок **«RPA Writer Converter»**, команда меню
   **View → Other Windows → RPA Writer Converter**.
3. Позиция окна: `[ProvideToolWindow(typeof(ConverterToolWindow), Style = VsDockStyle.MDI)]` —
   окно открывается в области документов (как вкладка редактора).
4. Хранилище маппинга: добавлен статический общий экземпляр `PixMwfMappingStore.Shared`;
   `PixMwfMappingControl` использует его по умолчанию (параметр конструктора); чат передаёт
   агент-инструментам `PixMwfMappingStore.Shared`.
5. `GetOpenSolutionDirectory()` остаётся в `ToolWindow1Control` (используется широко);
   в конвертер-контроле добавлена локальная копия. `GetHostWindowHandle()` перенесён в
   конвертер-контрол (использовался только конвертером).

## Шаги реализации

1. `RPAWriter.Core/Services/PixMwfMappingStore.cs` — добавить `Shared`.
2. `RPAWriter2/Controls/PixMwfMappingControl.xaml.cs` — хранилище назначается через конструктор,
   по умолчанию `Shared`.
3. Новые файлы:
   - `RPAWriter2/ConverterToolWindow.cs` — панель окна (`ToolWindowPane`, GUID, Caption, Content).
   - `RPAWriter2/ConverterToolWindowCommand.cs` — команда показа окна (CommandId 4134).
   - `RPAWriter2/ConverterToolWindowControl.xaml` + `.xaml.cs` — перенесённый UI и логика.
4. `RPAWriter2/ToolWindow1Control.xaml` — удалить `<TabItem Header="Converter">…</TabItem>`.
5. `RPAWriter2/ToolWindow1Control.xaml.cs` — удалить поле `_converterMessages`, методы
   конвертера/вывода/маппинга и их подключение в конструкторе; в `RebuildAgentTools`
   использовать `PixMwfMappingStore.Shared`.
6. `RPAWriter2/RPAWriter2Package.cs` — `[ProvideToolWindow(typeof(ConverterToolWindow), Style = VsDockStyle.MDI)]`
   и инициализация `ConverterToolWindowCommand`.
7. `RPAWriter2/RPAWriter2Package.vsct` — кнопка `cmdidConverterToolWindowCommand` (4134) в
   `IDG_VS_TOOLS_EXT_CUST`, текст «RPA Writer Converter».
8. Документация: `documentation/11-converter.md` (вкладка → окно), `documentation/00-index.md`,
   `documentation/07-controls.md`, `RPAWriter.md`.

## Проверка

- `dotnet build RPAWriter.Core` и `dotnet test RPAWriter.Tests` (355 тестов должны остаться зелёными).
- Сборка VSIX: `msbuild RPAWriter2/RPAWriter2.csproj /restore` (или F5).
- Ручная проверка: View → Other Windows → RPA Writer Converter; окно открывается в области
  документов; конвертация UiPath/PIX, импорт библиотек, «PIX to MWF map»; в чате (Act)
  AI-инструменты маппинга видят общий store.
