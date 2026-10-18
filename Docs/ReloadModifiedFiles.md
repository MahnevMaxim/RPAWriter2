# Required Visual Studio setting: reload modified files

> Note: for the RPA Writer extension to work correctly, the following Visual Studio
> setting must be enabled. It makes Visual Studio automatically reload files that were
> changed by an external process, and stops the "file modified outside the editor"
> warning dialog.

## Why this is needed

The extension writes/generates files (XAML, C#, specs, etc.) from an external process
(not through the Visual Studio editor). Without this setting, Visual Studio shows a
reload warning for every file changed on disk and may keep stale buffers in the editor.

## How to enable it

1. In the Visual Studio top menu, open **Tools** → **Options...**
   (Russian UI: **Сервис** → **Параметры...**).
2. In the left column, expand **Environment** and open the **Documents** section
   (Russian UI: **Среда** → **Документы**).
3. Check the option **Reload modified files unless there are unsaved changes**
   (Russian UI: **Перезагружать измененные файлы, если нет несохраненных изменений**).
