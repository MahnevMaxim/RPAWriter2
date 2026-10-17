# Tech Context

## Technologies
- .NET: Core = `netstandard2.0`, расширение = `net472` (VSIX), тесты = `net8.0`.
- WPF (окно расширения), EnvDTE/VSSDK (интеграция с VS).
- `Newtonsoft.Json` (сериализация), `System.Data.SQLite` (история чата).
- `NUnit` (тесты), `DocumentFormat.OpenXml` + `NPOI` (чтение Word/Excel).

## Development setup
- Решение: `RPAWriter2.slnx`.
- Сборка: `dotnet build <csproj>`; тесты: `dotnet test RPAWriter.Tests`.
- Промпты: `RPAWriter2/Prompts/**` (копируются в VSIX через `Content Include="Prompts\**\*"`).

## Constraints
- Core без VS-зависимостей (тестируется изолированно).
- OpenAI-совместимость без нативного function calling.
- Провайдер должен поддерживать роль `"tool"` (с фолбэком на `"user"`).

## Dependencies
- `RPAWriter.Core` → `RPAWriter.Tests`, `RPAWriter2`.
- `RPAWriter2` → `RPAWriter.Core`.
