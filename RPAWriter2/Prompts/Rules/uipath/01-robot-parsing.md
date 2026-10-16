# Правила разбора робота UiPath

- Точка входа робота — `Main.xaml` внутри каталога проекта.
- Читай файлы `.xaml`; распознавай активности UiPath:
  - браузер/UI: `Open Browser`, `Type Into`, `Click`, `Get Text`, `Read Range`, `Write Range`, `Log Message`;
  - управляющие: `Sequence`, `Flowchart`, `If`, `While`, `Do While`, `For Each`;
  - данные: `Assign`.
- Извлекай атрибуты активностей (URL, селектор, текст, цель, имена переменных) как аргументы.
- `Flowchart` и `Do While` упрощай до `Sequence`/`While` (отмечай это как предупреждение).
- `Log Message` сопоставляй с `WriteLine`.
