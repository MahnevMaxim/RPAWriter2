# 20 — Вынос контрола вкладок в ядро (AgentShared)

Goal: главный контрол вкладок (TabControl + хром + вкладки Chat/Settings) должен жить в ядре
(`Agent.Toolkit.Wpf`), а не в хосте `RPAWriter2`. Ядро владеет вкладками **Chat** и **Settings**;
доменные вкладки расширяемого приложения (**Spec / Knowledge base / Packages / Controls**)
хостятся в контрол при загрузке. `MainWindowControl` становится чистым шеллом: каждая доменная
вкладка выносится в отдельный `UserControl`.

## Status (текущее состояние)

- Хост `RPAWriter2/RPAWriter2/MainWindowControl.xaml` целиком владеет `TabControl MainTabs`
  и 6 вкладками: Chat → `toolkit:ChatControl`, Settings → `toolkit:SettingsControl`,
  а также доменные Spec / Knowledge base / Packages / Controls (у Controls вложенный `TabControl`).
- Стили вкладок `MainTabItemStyle`, `MainTabIconOutlineStyle`, `MainTabIconFilledStyle` +
  имплицитные `TabControl`/`TabItem` + шаблон `TabControl` определены локально в
  `MainWindowControl.xaml` (`UserControl.Resources`, строки ~530–658).
- Код-бехайнд `MainWindowControl.xaml.cs` (~3680 строк) завязан на `x:Name`-поля вкладок:
  `MainTabs`, `ChatPanel`, `SettingsPanel`, `SpecTab`, `KnowledgeBaseTab`, `PackagesTab`,
  `ControlsTab`, `ControlsInnerTabs` и пр.
- Ядро уже содержит `ChatControl` и `SettingsControl` (оба внутри мержат `Styles/AgentTheme.xaml`),
  и уже есть паттерн иконок-вкладок `SettingsNavItemStyle`/`SettingsNavIconOutlineStyle`/
  `SettingsNavIconFilledStyle` (на `Theme.*` DynamicResource). Есть готовый паттерн расширения
  `SettingsControl.InsertHostSection(...)`.
- `Agent.Core.Models.UiState.cs` содержит enum `MainTab` (Chat/Spec/KnowledgeBase/Settings/
  Packages/Controls) и `UiStateData`; `ChatModel.PersistUiState(MainTab)` принимает активную
  вкладку «как view-концерн» извне.
- Хост потребляет ядро как NuGet-пакеты `AgentToolkit` + `AgentToolkit.Wpf` версии **1.15.1**
  из локального `feed\`.

## Ограничение TDD

`Agent.Tests` — `net8.0`, а `Agent.Toolkit.Wpf` — `net472` (WPF). net8.0-проект не может
ссылаться на net472-сборку, поэтому вся тестируемая логика выносится в UI-free сборку
(`Agent.Core`), а в `Agent.Toolkit.Wpf` остаётся только рендеринг.

## Целевая архитектура

- `Agent.Core.Models.TabDefinition` — UI-free дескриптор вкладки (ключ/заголовок/тултип/иконки).
- `Agent.Core.Models.CoreTabs` — «принадлежащие ядру» вкладки Chat/Settings.
- `Agent.Core.Models.TabDefinitionCollection` — реестр вкладок (проверка дубликатов, lookup).
- `Agent.Toolkit.Wpf.Controls.AgentTabControl` — WPF-контрол (`TabControl`-подкласс): владеет
  Chat/Settings, рендерит хром, хостит доменные вкладки через `AddHostTab(...)`.
- Хост: 4 новых `UserControl` (`SpecTabControl`, `KnowledgeBaseControl`, `PackagesControl`,
  `ControlsGalleryControl`) + `MainWindowControl` как чистый шелл.

---

## Этап 0 — UI-free модель вкладок в ядре (TDD: red → green)

Новый файл `Agent.Core/Models/TabDefinition.cs`:

- `TabDefinition` (immutable): `Key`, `Header`, `ToolTip`, `OutlineIconKey`, `FilledIconKey`
  (строковые имена `MaterialIconKind`). Конструктор валидирует `Key`/`Header` (не null/пусто).
- `CoreTabs` — статическая фабрика двух ядровых вкладок:
  - `Chat`      → Key `"Chat"`,      иконки `MessageOutline` / `Message`
  - `Settings`  → Key `"Settings"`,  иконки `CogOutline` / `Cog`
  - `All` → `IReadOnlyList<TabDefinition>` в порядке Chat, Settings.
- `TabDefinitionCollection : IReadOnlyList<TabDefinition>`:
  - `Add(TabDefinition)` — бросает `ArgumentException` при дубликате `Key` или `null`;
  - `TryGet(string key)`, `ContainsKey(string key)`, `IndexOf(string key)`.

Тесты: новый `Agent.Tests/TabDefinitionTests.cs` (net8.0, NUnit):
- ключи/иконки `CoreTabs.Chat` и `CoreTabs.Settings` корректны;
- `TabDefinitionCollection.Add` отклоняет `null` и дубликат `Key`;
- `TryGet`/`ContainsKey` находят по ключу;
- `All` содержит Chat и Settings в нужном порядке.

Проверка: `dotnet test Agent.Tests/Agent.Tests.csproj --filter "FullyQualifiedName~TabDefinitionTests"`.

- [x] `Agent.Core/Models/TabDefinition.cs`
- [x] `Agent.Tests/TabDefinitionTests.cs`
- [x] прогнать тесты (сначала падают → после реализации зелёные)

---

## Этап 1 — стили вкладок в ядро

В `Agent.Toolkit.Wpf/Styles/AgentTheme.xaml` добавить (перенос из `MainWindowControl.xaml`,
цвета `Dark.*` → `Theme.*` DynamicResource, как уже сделано для `SettingsNav*`):

- `MainTabControlStyle` — именованный стиль `TargetType="TabControl"` (шаблон с `TabPanel` +
  контент-бордером).
- `MainTabItemStyle` — именованный стиль `TargetType="TabItem"` (36×36, `AccentHighlight`
  на `Theme.Highlight`, hover на `Theme.ControlHover`, `Cursor=Hand`).
- `MainTabIconOutlineStyle` — `TargetType="Path"` (20×20, `Theme.Text`).
- `MainTabIconFilledStyle` — на базе outline, `Visibility=Collapsed`, показан при
  `IsSelected` родительского `TabItem`.

Легаси-ключи `Dark.*` в `AgentTheme.xaml` оставить (нужны старым контролам хоста).

- [ ] `Agent.Toolkit.Wpf/Styles/AgentTheme.xaml` — добавить 4 стиля

---

## Этап 2 — `AgentTabControl` в `Agent.Toolkit.Wpf`

Новые файлы `Agent.Toolkit.Wpf/Controls/AgentTabControl.xaml(.cs)`.

`AgentTabControl : TabControl`. XAML минимален: корень `<TabControl x:Class=...>` со
`TabControl.Resources` → merge `pack://application:,,,/Agent.Toolkit.Wpf;component/Styles/AgentTheme.xaml`.

Код-бехайнд:
- конструктор: `InitializeComponent()`, `AgentThemeManager.EnsureInitialized()`,
  `Style = (Style)FindResource("MainTabControlStyle")`, затем построить вкладки Chat/Settings
  из `CoreTabs` (один источник правды об иконках);
- публичные свойства:
  - `ChatControl ChatPanel` (создаётся в конструкторе, кладётся в Content вкладки Chat);
  - `SettingsControl SettingsPanel` (аналогично);
  - `string SelectedTabKey` (производное от `SelectedItem`);
- `TabItem AddHostTab(TabDefinition definition, object content)` — валидирует, строит
  заголовок (Grid из двух `Path` по `MainTabIconOutlineStyle`/`MainTabIconFilledStyle` +
  `MaterialIconToGeometryConverter` + `Enum.Parse<MaterialIconKind>`), применяет
  `MainTabItemStyle`, кладёт `content`, добавляет в `Items`, возвращает `TabItem`;
- `TabItem FindTab(string key)`, `bool TrySelectTab(string key)`.

Паттерн построения иконки — зеркало `SettingsControl.BuildNavIcon/BuildIconPath`.

- [x] `Agent.Toolkit.Wpf/Controls/AgentTabControl.xaml`
- [x] `Agent.Toolkit.Wpf/Controls/AgentTabControl.xaml.cs`

---

## Этап 3 — версия → коммит → локальный пакет

1. `Directory.Build.props`: `1.15.1` → **`1.16.0`** (фича → минор).
2. Собрать и упаковать: `.\pack.ps1` → `feed\AgentToolkit.1.16.0.nupkg`,
   `feed\AgentToolkit.Wpf.1.16.0.nupkg`.
3. Коммит в стиле репозитория (русский, многострочный, по проектам; сообщение — через
   `git commit -F <file>` из-за кириллицы).
4. В хосте `RPAWriter2/RPAWriter2/RPAWriter2.csproj` поднять
   `AgentToolkit` и `AgentToolkit.Wpf` до `1.16.0`.

- [x] `Directory.Build.props` → 1.16.0
- [x] `.\pack.ps1`
- [x] коммит ядра
- [x] `RPAWriter2.csproj` → 1.16.0

---

## Этап 4 — вынос доменных вкладок в отдельные UserControl (хост)

Папка `RPAWriter2/RPAWriter2/Controls/` (рядом с `NotificationBanner`, `CommitEditorControl`,
`CreateRobotControl`). Каждый контрол переносит свою XAML-разметку, свои `x:Name` и свои
view-обработчики; публичные свойства — для элементов, к которым шеллу нужен доступ.

Иконки вкладок (переносятся как есть):

| Вкладка | Key | Outline / Filled |
|---|---|---|
| Spec | `"Spec"` | `FileDocumentOutline` / `FileDocument` |
| Knowledge base | `"KnowledgeBase"` | `DatabaseOutline` / `Database` |
| Packages | `"Packages"` | `PackageOutline` / `Package` |
| Controls | `"Controls"` | `PaletteOutline` / `Palette` |

### 4.1 `SpecTabControl.xaml(.cs)`
Переносится содержимое `SpecTab` (кнопки Deploy/Analyze logs/Import/Generate/Find similar/
Security audit/Convert/Clear, `NoticeBar`, deploy-тост, прогресс импорта, `SpecMessages`
и панель production-логов). `x:Name` → публичные свойства (`SpecMessages`, `NoticeBar`,
`MinLevelCombo`, `FromDatePicker`, `ToDatePicker`, `RobotNameBox`, `ProviderCombo`, …).
View-обработчики (поиск, выбор задачи, выбор дат/уровня, copy spec output) — внутрь контрола;
логика, требующая сервисов шелла (Deploy, Analyze logs, спецификационные нотификации) —
остаётся в шелле через события/колбэки.

### 4.2 `KnowledgeBaseControl.xaml(.cs)`
Содержимое `KnowledgeBaseTab` (`KbPauseButton`, `KbStopButton`, `KbStatusText`, `KbProgressBar`,
`KbAutoScrollCheckBox`, `KbMessagesBox`). `MvvmFactory.Attach` и нотификации модели — в шелле.

### 4.3 `PackagesControl.xaml(.cs)`
Содержимое `PackagesTab` (`PackageFilterBox`, `PackagesStatusText`, `PackagesList`,
`RestorePackagesPanel`). Фильтр (`OnPackageFilterChanged`) — внутрь контрола; refresh/install/
update/uninstall/restore — в шелле (нужен сервис пакетов).

### 4.4 `ControlsGalleryControl.xaml(.cs)`
Содержимое `ControlsTab` с вложенным `ControlsInnerTabs` (Notifications / Chat / SVG / Palette):
`NotificationAutoHideBox`, `ReplayNotificationsButton`, `ControlsMessagesList`, `ControlsChatList`,
`SvgSearchBox`, `ControlsSvgList`, `ControlsPaletteList`. `OnSvgSearchTextChanged`,
`OnCopySvgIdentifier`, `OnSendSvgIdentifier`, `OnReplayNotifications` — внутрь контрола;
`IsNotificationGalleryVisible` (зависит от выбранного таба) — пересмотреть как свойство контрола.

- [x] `RPAWriter2/RPAWriter2/Controls/SpecTabControl.xaml(.cs)`
- [x] `RPAWriter2/RPAWriter2/Controls/KnowledgeBaseControl.xaml(.cs)`
- [x] `RPAWriter2/RPAWriter2/Controls/PackagesControl.xaml(.cs)`
- [x] `RPAWriter2/RPAWriter2/Controls/ControlsGalleryControl.xaml(.cs)`

---

## Этап 5 — `MainWindowControl` → чистый шелл

XAML: заменить `<TabControl x:Name="MainTabs">…6 TabItem…</TabControl>` на
`<toolkit:AgentTabControl x:Name="MainTabs" Margin="8,8,8,5"/>`; удалить локальные
`MainTab*`-стили и имплицитные `TabControl`/`TabItem`-стили, которые больше не нужны
(оставить те, что нужны вложенным контролам хоста). Chat/Settings — встроены в контрол.

Код-бехайнд шелла:
- создать модели/вьюмодели и 4 доменных контрола; `MvvmFactory.Attach(model, vm, control)`;
- `MainTabs.AddHostTab(new TabDefinition(...), specControl)` и т.д.; для `ControlsTab`
  сохранить `TabItem` для управления `Visibility`;
- `MainTabs.SelectedItem = SettingsTab` → `MainTabs.TrySelectTab("Settings")`;
- `SelectTabByHeader`/`HeaderToMainTab`/`MainTabToHeader` → маппинг `Key ↔ MainTab`
  (ключи: Chat/Settings/Spec/KnowledgeBase/Packages/Controls); персист по `SelectedTabKey`;
- `WireChatPanel()` / `MvvmFactory.Attach(..., SettingsPanel)` — без изменений (свойства
  `ChatPanel`/`SettingsPanel` остались доступны на `AgentTabControl`);
- `IsNotificationGalleryVisible` → читать выбранный таб из `ControlsGalleryControl`.

- [x] `MainWindowControl.xaml` — заменить на `AgentTabControl`
- [x] `MainWindowControl.xaml.cs` / `.ChatHost.cs` — переработать таб-логику

---

## Этап 6 — валидация

- [x] Ядро: `dotnet build Agent.Toolkit.Wpf/Agent.Toolkit.Wpf.csproj` — 0 ошибок.
- [x] Тесты: `dotnet test Agent.Tests/Agent.Tests.csproj` — 249/249 зелёные (включая `TabDefinitionTests`).
- [x] Пакет: `.\pack.ps1` — `AgentToolkit.1.16.0.nupkg` + `AgentToolkit.Wpf.1.16.0.nupkg` в `feed\`.
- [x] Хост: `dotnet build RPAWriter2.slnx` — 0 ошибок (пакет 1.16.0 восстановлен из `feed\`).
- [ ] Smoke: сравнить визуал вкладок с pre-change VSIX (хром, иконки outline/filled, hover,
   переключение Dark/Light, скрытие вкладки Controls, персист активной вкладки).

---

## Карта «ядро vs хост»

**Ядро (AgentShared):** `TabDefinition`, `CoreTabs`, `TabDefinitionCollection`,
`AgentTabControl`, стили вкладок в `AgentTheme.xaml`.

**Хост (RPAWriter2):** 4 доменных `UserControl` + `MainWindowControl`-шелл; модели
`SpecTabModel`/`KnowledgeBaseModel` и сервисы (Deploy, log analyzer, пакеты) остаются в хосте;
маппинг `Key ↔ MainTab` и персист активной вкладки — в шелле.

## Риски

- Имплицитный стиль `TabControl` в WPF не применяется к подклассам — поэтому `AgentTabControl`
  применяет именованный `MainTabControlStyle` к себе явно (иначе теряется шаблон с `TabPanel`).
- `MainTab` enum лежит в ядре, но содержит RPA-вкладки (Spec/KB/Packages/Controls) — это
  предсуществующая связность; в рамках этой задачи ядровый контрол оперирует строковым `Key`,
  а хост продолжает мапить `Key ↔ MainTab`. Полный вынос RPA-специфики из `MainTab` — отдельная задача.
- Вынос `ControlsTab` с вложенным `TabControl` — самый связанный кусок (`IsNotificationGalleryVisible`
  зависит от выбранных табов); выносится целиком как `ControlsGalleryControl`.
