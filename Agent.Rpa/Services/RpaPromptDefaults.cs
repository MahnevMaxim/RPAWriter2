using Agent.Rpa.Models;
namespace Agent.Rpa.Services
{
    /// <summary>
    /// Built-in fallback RPA prompt templates used when the deployed prompt files are missing
    /// (e.g. during unit tests). Kept identical to the shipped .txt files so behaviour
    /// is unchanged when files are absent.
    /// </summary>
    public static class RpaPromptDefaults
    {
        public const string AnalyzeSpec =
            "Analyze the technical specification and return JSON of the form " +
            "{\"summary\":\"...\",\"tasks\":[{\"action\":\"...\",\"target\":\"...\",\"description\":\"...\",\"activityType\":\"...\",\"sapTransactionCode\":\"...\",\"sapActionKind\":\"...\"}]}.\n\n" +
            "For SAP transaction tasks, split the work per transaction and set the SAP fields:\n" +
            "- \"sapTransactionCode\": the SAP transaction name/code (e.g. \"VA01\", \"SE16N\").\n" +
            "- \"sapActionKind\": one of \"setup\" (настройка и запуск транзакции), \"work\" (работа с транзакцией), \"gettable\" (получение таблицы/данных из транзакции).\n" +
            "Omit both sapTransactionCode and sapActionKind for tasks that are not SAP transaction tasks.\n\n" +
            "Spec:\n{spec}";

        public const string GenerateRobot =
            "Construct a workflow plan. Return JSON of the form {\"name\":\"...\",\"steps\":[" +
            "{\"kind\":\"InvokeMethod\",\"className\":\"RpaActions\",\"methodName\":\"...\"}," +
            "{\"kind\":\"Assign\",\"variable\":\"...\",\"expression\":\"...\"}]}. " +
            "kind: Sequence|InvokeMethod|Assign|WriteLine|Delay. Source analysis: {analysis}";

        public const string DecomposeTasks =
            "Decompose the document into small atomic tasks. Return a JSON array: " +
            "[{\"action\":\"...\",\"target\":\"...\",\"description\":\"...\",\"activityType\":\"...\",\"sapTransactionCode\":\"...\",\"sapActionKind\":\"...\",\"estimatedHours\":0.5}]. " +
            "Write the action, target and description of each task in Russian.\n" +
            "Set \"estimatedHours\" for every task: the estimated effort in hours as a number (fractions allowed, e.g. 0.5).\n\n" +
            "For SAP transaction tasks, split the work per transaction and set the SAP fields:\n" +
            "- \"sapTransactionCode\": the SAP transaction name/code (e.g. \"VA01\", \"SE16N\").\n" +
            "- \"sapActionKind\": one of \"setup\" (настройка и запуск транзакции), \"work\" (работа с транзакцией), \"gettable\" (получение таблицы/данных из транзакции).\n" +
            "Omit both sapTransactionCode and sapActionKind for tasks that are not SAP transaction tasks.\n\n" +
            "Document:\n{document}";

        public const string ReconstructSpec =
            "Reconstruct the technical specification of an RPA robot from its source code. " +
            "Start from the entry point (main.xaml) and follow the files it invokes; the provided list is the reachable set. " +
            "Return JSON of the form {\"name\":\"...\",\"description\":\"...\",\"steps\":[\"...\"],\"inputs\":[\"...\"],\"outputs\":[\"...\"]}. " +
            "Write the description, steps, inputs and outputs in Russian.\n\n" +
            "Robot name: {robotName}\n\nRobot files (relativePath + content):\n{files}";

        public const string MapTaskToSolution =
            "Locate the solution for the task in the robot source files. Return JSON " +
            "{\"xamlFragment\":\"...\",\"csharpCode\":\"...\",\"solutionPaths\":[\"relative/path/File.xaml\",\"relative/path/File.cs\"]}. " +
            "solutionPaths must list the file(s) (relative path) where the task is implemented.\n\n" +
            "Task: {task}\n\nRobot files (relativePath + content):\n{files}";

        public const string GenerateAlgorithmDraft =
            "Сгенерируй алгоритм по приведённому техническому заданию. Верни ТОЛЬКО Markdown, без пояснений вокруг.\n\n" +
            "Обязательно включи три раздела:\n" +
            "1. Пошаговый алгоритм — нумерованный список шагов с условиями и ветвлениями.\n" +
            "2. Диаграмма алгоритма — блок Mermaid (flowchart или graph), отражающий все переходы и ветвления.\n" +
            "3. Древовидная структура алгоритма — иерархия шагов отступами с указанием переходов (успех/ошибка/повтор).\n\n" +
            "Техническое задание:\n{spec}";

        public const string ConsolidateAlgorithm =
            "Дано техническое задание и несколько независимо сгенерированных черновиков алгоритма.\n\n" +
            "Выполни:\n" +
            "1. Найди и перечисли противоречия и расхождения между черновиками и с техническим заданием.\n" +
            "2. Сгенерируй итоговый правильный алгоритм с учётом всех неточностей (Markdown: пошаговый текст, диаграмма Mermaid, древовидная структура с переходами).\n" +
            "3. Составь список вопросов по техническому заданию, требующих уточнения. Для каждого вопроса приведи именно ту цитату из технического задания, которая вызывает вопрос, и укажи номер страницы, на которой находится эта цитата (страницы в ТЗ помечены маркерами вида `> **[Page N]**`). Оформляй каждый вопрос так:\n\n**Вопрос:** <текст вопроса>\n**Цитата (страница N):** «<точная цитата из ТЗ>»\n\n" +
            "Верни СТРОГО JSON без текста вокруг: {\"algorithm\":\"<итоговый алгоритм в Markdown>\",\"questions\":\"<список вопросов в Markdown>\"}.\n\n" +
            "Техническое задание:\n{spec}\n\n{drafts}";

        public const string AnalyzeOutputWindow =
            "Analyze the Visual Studio Output window content below. " +
            "Identify errors, warnings and their likely root causes, and suggest concrete fixes. " +
            "When an error references a source file (for example a stack trace entry like \"... in path\\file.cs:line N\" or a compiler error line like \"file.cs(12,34): error ...\"), open that file in Visual Studio using the open_file_in_visual_studio tool so the user can inspect it:";

        public const string AnalyzeProdLogs =
            "Analyze the production robot logs below. " +
            "Summarize the robot run, list errors and warnings with their likely root causes, and suggest fixes. " +
            "When an error references a source file (for example a stack trace entry like \"... in path\\file.cs:line N\"), open that file in Visual Studio using the open_file_in_visual_studio tool so the user can inspect it:";

        public const string SecurityAuditSystemPrompt =
            "You are a senior application security specialist performing a STRICTLY " +
            "read-only static code review of an RPA robot built on Windows Workflow " +
            "Foundation / .NET Framework (C# + XAML). You are an expert in OWASP ASVS, " +
            "OWASP Top 10, and typical secrets-handling and injection risks in automation " +
            "code.\n\n" +
            "Hard rules:\n" +
            "- You only READ the source text provided below. Never suggest running, " +
            "building, compiling or executing the code, and never ask to do so.\n" +
            "- Report only findings that are evidenced in the provided code. For every " +
            "finding give the exact relative file path and, when identifiable, the " +
            "relevant code fragment. Do NOT invent findings; if there is nothing to " +
            "report in a category, do not mention that category.\n" +
            "- Write the report body in Russian. Headings and severity labels may stay " +
            "in English.\n\n" +
            "You MUST cover at least these areas:\n" +
            "1. Secrets & credentials: hardcoded passwords, API keys, tokens, connection " +
            "strings, certificates, private keys, credentials in logs/config.\n" +
            "2. Injection: SQL, OS command, path traversal, XPath/LDAP, XML External " +
            "Entities (XXE), formula injection.\n" +
            "3. Insecure deserialization and dynamic code/activity loading.\n" +
            "4. Unsafe XAML/workflow usage: InvokeMethod, InvokeWorkflow, CodeActivity, " +
            "custom activities, reflection, unchecked argument types or expressions.\n" +
            "5. Network & filesystem: insecure protocols (HTTP/plain FTP), unbounded file " +
            "access, weak permissions, sensitive data written to temp.\n" +
            "6. Logging: secrets or personal data (PII) written to logs or output.\n" +
            "7. Cryptography: weak/broken algorithms (MD5, SHA1, DES, RC4), hardcoded " +
            "keys/IVs, weak random number generation.\n" +
            "8. Dependencies: outdated or known-vulnerable NuGet packages referenced in " +
            ".csproj/packages.config.\n" +
            "9. Authorization & impersonation: excessive privileges, hardcoded service " +
            "accounts, missing input validation.\n" +
            "10. Data protection & PII: personal data processed without safeguards.\n\n" +
            "For each finding use EXACTLY this Markdown structure:\n\n" +
            "### [Severity] Short title\n" +
            "- **Location**: `relative/path/file.cs` (line or activity name if identifiable)\n" +
            "- **Evidence**: fenced code snippet or the exact string\n" +
            "- **Risk**: what an attacker could do / business impact\n" +
            "- **Recommendation**: concrete fix\n\n" +
            "Severity is one of: Critical, High, Medium, Low, Info.\n\n" +
            "After all findings, add a `## Summary` section with:\n" +
            "- counts per severity (Critical/High/Medium/Low/Info),\n" +
            "- a 1-2 sentence overall risk assessment for this robot.\n\n" +
            "Be concise and specific. Do not add generic boilerplate. The robot solution " +
            "source is provided below; files are separated by headers with their relative " +
            "path.";

        public const string SecurityAuditCredentialsOnlySystemPrompt =
            "You are a senior application security specialist performing a STRICTLY " +
            "read-only static code review of an RPA robot built on Windows Workflow " +
            "Foundation / .NET Framework (C# + XAML). This review is focused EXCLUSIVELY " +
            "on hardcoded credentials: passwords and logins. You are an expert in OWASP " +
            "ASVS and typical secrets-handling risks in automation code.\n\n" +
            "Scope — report ONLY the following, nothing else:\n" +
            "1. Hardcoded passwords: any quoted string value that is assigned to a " +
            "variable, field, property, argument or activity named password/pwd/passphrase/" +
            "pass, or passed into a method/constructor/activity whose parameter is named " +
            "that way.\n" +
            "2. Hardcoded logins/usernames/accounts: the same data-flow analysis for names " +
            "like login, username, user id, userid, account.\n" +
            "3. Credentials in configuration: connection strings (SQL/Oracle/etc.) with " +
            "Password=/Pwd=/User Id=, entries in .config/.json/.settings/.resx/.csproj " +
            "with credential keys, tokens, API keys, certificates, private keys.\n" +
            "4. Secrets written to logs or output (WriteLine/Log/Debug/Trace) in plaintext.\n\n" +
            "How to investigate (data-flow analysis):\n" +
            "- Inspect EVERY quoted string value in the provided code and trace where it " +
            "flows. Report a value only when it actually ends up in a credential position: " +
            "a variable/field/argument named like a password or login, a method/activity " +
            "parameter named that way, or a connection string / credential config entry.\n" +
            "- Check configuration files (.config/.json/.settings/.resx/.csproj) for " +
            "suspicious credential values.\n\n" +
            "Hard rules:\n" +
            "- You only READ the source text provided below. Never suggest running, building, " +
            "compiling or executing the code, and never ask to do so.\n" +
            "- Report only findings that are evidenced in the provided code. For every " +
            "finding give the exact relative file path and, when identifiable, the relevant " +
            "code fragment. Do NOT invent findings; if there is nothing to report, say so.\n" +
            "- Do NOT report empty placeholders, environment-variable lookups (e.g. " +
            "Environment.GetEnvironmentVariable, ConfigurationManager.AppSettings) or " +
            "values that are not actually credentials.\n" +
            "- Write the report body in Russian. Headings and severity labels may stay in " +
            "English.\n\n" +
            "Context safety: the source is chunked, so analyze only the text provided in " +
            "this request. Keep your answer concise and specific — output ONLY findings and " +
            "a short summary, with no generic boilerplate, no empty categories and no " +
            "repetition, so the combined report does not overflow the context window.\n\n" +
            "For each finding use EXACTLY this Markdown structure:\n\n" +
            "### [Severity] Short title\n" +
            "- **Location**: `relative/path/file.cs` (line or activity name if identifiable)\n" +
            "- **Evidence**: fenced code snippet or the exact string\n" +
            "- **Risk**: what an attacker could do / business impact\n" +
            "- **Recommendation**: concrete fix\n\n" +
            "Severity is one of: Critical, High, Medium, Low, Info.\n\n" +
            "After all findings, add a `## Summary` section with:\n" +
            "- counts per severity (Critical/High/Medium/Low/Info),\n" +
            "- a 1-2 sentence overall risk assessment for this robot.\n\n" +
            "Be concise and specific. Do not add generic boilerplate. The robot solution " +
            "source is provided below; files are separated by headers with their relative " +
            "path.";

        public const string CommitMessageSystemPrompt =
            "You are an expert at writing concise git commit messages. " +
            "Summarize the provided changes into a single-line commit message in the format \"type(scope): summary\". " +
            "Write the summary in Russian. " +
            "Respond with only the commit message text — no markdown, no quotes, no extra explanation.";

        public const string CommitRobotSummarySystemPrompt =
            "You are an expert at writing concise git commit messages. " +
            "Summarize the robot changes below into a single concise line in the imperative mood. " +
            "Write the summary in Russian. " +
            "Respond with only the summary text — no RPA-...: prefix, no markdown, no quotes, no extra explanation.";

        public const string CommitOtherSummarySystemPrompt =
            "You are an expert at writing concise git commit messages. " +
            "Summarize the changes below into a single concise line in the imperative mood. " +
            "Write the summary in Russian. " +
            "Respond with only the summary text — no markdown, no quotes, no extra explanation.";

        public const string PixMappingSystemPrompt =
            "You map PIX RPA activities to Microsoft Workflow Foundation (MWF) activities. " +
            "Your only tool is map_pix_to_mwf. " +
            "Process the PIX activities strictly in the order listed in the user message and call map_pix_to_mwf once per activity. " +
            "Choose the closest semantic match among the available MWF activities. " +
            "If no MWF activity fits an activity, clear it by passing an empty mwf_activity. " +
            "After all assignments, answer with a short summary. Do not invent activities that are not listed.";

        public const string PixArgumentMappingSystemPrompt =
            "You map arguments of already-mapped PIX RPA activities to arguments of their mapped Microsoft Workflow Foundation (MWF) activities. " +
            "Your only tool is map_pix_mwf_arguments. " +
            "For each PIX activity listed in the user message, process its arguments in the listed order and call map_pix_mwf_arguments once per argument. " +
            "Choose the closest semantic match among the available MWF arguments (match by name, then by type and direction). " +
            "Set conversion to None when the source and target types match, ToString when the target is a string, DateTimeFormat when parsing a date/time (expression = format string), " +
            "Parse when converting with ChangeType (expression = target type name), and Custom when a custom C# expression template is needed (expression uses {0} for the source value). " +
            "If no MWF argument fits an argument, unmap it by passing an empty mwf_argument. " +
            "After all assignments, answer with a short summary. Do not invent arguments that are not listed.";
    }
}
