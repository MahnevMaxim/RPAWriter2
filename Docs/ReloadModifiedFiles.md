# Required Visual Studio settings (applied automatically)

> Note: the RPA Writer extension applies the settings below automatically on startup
> (through `VsOptionsService`). "Reload modified files" is applied on every supported
> Visual Studio version; "Check for consistent line endings on load" is applied only
> when the option is present in the object model (it was removed in VS 2026). This
> page documents them for manual fallback and troubleshooting on machines where
> automation is not possible.

## 1. Reload modified files

Makes Visual Studio automatically reload files that were changed by an external
process, and stops the "file modified outside the editor" warning dialog.

- **Tools → Options → Environment → Documents** (Russian: **Сервис → Параметры → Среда → Документы**)
- Enable **Reload modified files unless there are unsaved changes**
  (Russian: **Перезагружать измененные файлы, если нет несохраненных изменений**).
- DTE property: `AutoloadExternalChanges = true`.

## 2. Check for consistent line endings on load

Stops the "Inconsistent Line Endings" modal dialog that appears when a file contains
mixed CRLF/LF line endings and blocks the whole UI/automation flow.

- **Tools → Options → Environment → Documents** (Russian: **Сервис → Параметры → Среда → Документы**)
- Disable **Check for consistent line endings on load**.
- DTE property: `CheckLineEndingsOnLoad = false`.

> **VS 2026:** this option was removed from the EnvDTE object model
> (`_EnvironmentDocuments` no longer exposes `CheckLineEndingsOnLoad`), so the
> extension treats it as unavailable and skips it silently. The checkbox may also be
> gone from the Options dialog in newer builds — configure it manually only if it is
> still present.

> The extension also normalizes line endings when it writes files (see
> `RPAWriter.Core.Services.LineEndings`), so generated files should never contain
> mixed endings in the first place.

## Why this is needed

The extension writes/generates files (XAML, C#, specs, etc.) from an external process
(not through the Visual Studio editor). Without the settings above, Visual Studio shows
modal warning dialogs that block the UI and may keep stale buffers in the editor.
