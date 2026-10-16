# Agent Plan / Act Modes

This document describes the design for giving the RPA Writer agent two operating
modes — **Plan** and **Act** — with a mode switch under the chat input box.

> Status: **implemented**. Variant C — full agentic tool layer with per-mode gating.

---

## 1. Goal

The agent gets a mode toggle under the chat input:

- **Plan** — the agent only reads, analyzes, and proposes a plan. It must not
  change anything.
- **Act** — the agent can perform real changes and run actions.

The toggle must actually gate the agent's capabilities, not just reword a prompt.

---

## 2. Current state

- `IAiProvider.ChatAsync(string)` sends a single request
  `[{system: ChatSystemPrompt}, {user: message}]`. Dialog history lives only in
  the UI (`_messages`); it is **not** sent back to the model.
- The provider is OpenAI-compatible and must stay compatible with
  OpenAI / Azure OpenAI / Ollama / LM Studio / vLLM, so the design must not rely
  on native function calling.
- The rest of the project already follows a "model returns JSON" pattern
  (`AnalyzeSpec`, `GenerateRobot`, etc.).
- All real operations are wired into `ToolWindow1Control` button handlers
  (`OnImportSpec`, `OnGenerateRobot`, `OnBuildRelease`, `OnCreateRobot`,
  `OnFormKnowledgeBase`, `OnConvertUiPath`, …).
- `RPAWriter.Core` is `netstandard2.0` with no VS dependencies; `RPAWriter2`
  holds the `EnvDTE` / `Microsoft.VisualStudio.Shell` logic.

---

## 3. What the agent can do per mode

### 3.1 Plan mode (read-only)

The agent reads and analyzes but never mutates:

1. Answer questions, explain specs and code.
2. Analyze a spec and decompose it into tasks/steps.
3. Reconstruct a spec from robot sources.
4. Find similar tasks in the knowledge base and propose reusable solutions.
5. Read and explain logs and compiler errors (Error List / Output window).
6. Compose a work plan: step order, effort estimate, risks.
7. Propose concrete edits (diff/fragments) **without applying them**.

Forbidden: writing files, changing code, creating a robot, running a build,
applying fixes, importing the knowledge base.

### 3.2 Act mode (mutating)

The agent performs real changes and runs actions:

1. Generate a robot and write artifacts (XAML / C#).
2. Create / overwrite `spec.md`, `tasks.md`.
3. Apply fixes (full overwrite or partial diff fix).
4. Create a new robot project from the template.
5. Modify solution files via DTE.
6. Run a build / publish.
7. Import / update the knowledge base.
8. Convert UiPath → WF and write the result.
9. (Optional) roll back changes.

---

## 4. Architectural decisions

1. **Tool protocol = JSON over plain chat** (not native tools): the agent
   returns either a final text or a strict JSON tool call:

   ```json
   {"action":"final","text":"..."}
   {"action":"tool_call","tool":"<name>","arguments":{...}}
   ```

   The app executes the call, appends the result to the history, and repeats
   (agentic loop). This works with every OpenAI-compatible provider and the
   stub.

2. **Layer split:**
   - `RPAWriter.Core` only gains history support in `ChatAsync`.
   - The tool registry, gating, and the agentic loop live in `RPAWriter2`
     (access to both Core services and `DTE`).

3. **Gating via `Mutates` flag:** Plan exposes only read-only tools; Act exposes
   all tools. Mutating tools are not listed in the Plan system prompt, and a
   mutating call in Plan mode is rejected.

---

## 5. Implementation plan

### Step 1 — Core: models and contract
- `RPAWriter.Core/Models/Models.cs`: add `ChatMessageData { Role, Content }`
  (roles `system` / `user` / `assistant` / `tool`).
- `RPAWriter.Core/Contracts/Contracts.cs`: add an overload
  `Task<ChatResponse> ChatAsync(IReadOnlyList<ChatMessageData> messages, string systemPrompt, CancellationToken ct = default);`
  Keep `ChatAsync(string, …)` for backward compatibility.

### Step 2 — Core: providers
- `OpenAiCompatibleProvider`: refactor `BuildRequest` into a variant that builds
  the `messages` array from history + `systemPrompt`; the old signature
  delegates. Implement the new `ChatAsync` overload.
- `StubAiProvider`: implement the new overload (stub answer).

### Step 3 — Core: agent prompts
- `PromptDefaults.cs`: add `AgentPlanSystemPrompt`, `AgentActSystemPrompt`.
- `PromptSet.cs`: load them as `agent-plan-system.txt` / `agent-act-system.txt`.
- Create `RPAWriter2/Prompts/System/agent-plan-system.txt` and
  `agent-act-system.txt` (role, available tools, `final` / `tool_call` JSON
  protocol).

### Step 4 — RPAWriter2: mode and message model
- New `AgentMode` enum (`Plan` / `Act`).
- Extend `ChatMessage.cs` with a `Mode` field (for the UI label).

### Step 5 — RPAWriter2: tool registry
- `Services/AgentTools/IAgentTool.cs`:
  `{ Name, Description, ParametersSchema, Mutates, Task<string> ExecuteAsync(JObject args, CancellationToken ct) }`.
- `Services/AgentTools/AgentToolRegistry.cs`: registration, lookup by name,
  filter by mode.

**Read-only tools (Plan + Act):**

| Tool | Purpose | Backed by |
|---|---|---|
| `read_spec` | current `SpecModel` | `_currentSpec` |
| `read_file` | read a solution file | `DTE` / `File` |
| `read_robot_sources` | robot source files | existing read logic |
| `analyze_spec` | spec analysis | `_ai.AnalyzeSpecAsync` |
| `decompose_tasks` | task decomposition | `_ai.DecomposeTasksAsync` |
| `reconstruct_spec` | spec reconstruction | `_ai.ReconstructSpecAsync` |
| `map_task_to_solution` | map task to solution | `_ai.MapTaskToSolutionAsync` |
| `find_similar` | similar KB tasks | `IKnowledgeBase.FindSimilarTasksAsync` |
| `list_kb` | KB records | `IKnowledgeBase.GetAllAsync` |
| `read_errors` | compiler errors | `BuildFeedbackService.GetCompilationErrorsAsync` |
| `read_output` | Output window | `BuildFeedbackService.GetOutputWindowTextAsync` |
| `analyze_logs` | log analysis | `LogAnalyzer` |

**Mutating tools (Act only):**

| Tool | Purpose | Backed by |
|---|---|---|
| `generate_robot` | plan + XAML/C# | `_ai.GenerateRobotAsync` + `RobotGenerator` |
| `write_file` | write `spec.md` / `tasks.md` / artifacts | existing `WriteMarkdownWithConfirm` |
| `apply_fix` | overwrite / diff edit via DTE | `DTE` |
| `create_robot` | create robot from template | `RobotTemplateService` |
| `build` | Release build | `BuildFeedbackService.BuildReleaseAsync` |
| `convert_uipath` | UiPath → WF and write | `UiPathToWfConverter` |
| `kb_import` / `kb_add` | form / update KB | `KnowledgeBaseFormationService` / `RobotKnowledgeBaseImportService` |


### Step 6 — RPAWriter2: agentic loop
- `Services/AgentRunner.cs`:
  1. build history (system prompt by mode + user/assistant/tool messages);
  2. call `ChatAsync(history, systemPrompt, ct)`;
  3. parse the response (`final` → return text; `tool_call` → gate by `Mutates`,
     execute, append a `tool` result message, repeat);
  4. cap iterations (e.g. 8) and honor cancellation via the existing
     `CancelButton`.

### Step 7 — UI: XAML
- Under `InputBox` (and the Send/Errors buttons) add a **Plan / Act** switch
  (two `RadioButton`s or a `ToggleButton`), bound to `_mode`.

### Step 8 — UI: code-behind
- Replace `SendChatTextAsync` with a call to `AgentRunner`; show tool-call
  status and results in the chat; handle Stop.
- On mode switch, select the matching system prompt and print a notice in the
  chat.

### Step 9 — Tests (`RPAWriter.Tests`)
- `AgentResponseParserTests` — parse `final` / `tool_call`, invalid JSON.
- `AgentToolRegistryTests` — gating: Plan rejects `Mutates`, Act allows it.
- `ChatHistoryTests` / `BuildRequestTests` — building `messages` from history +
  system prompt.

### Step 10 — Verification
- Build the solution and run the tests (`dotnet build` / `dotnet test`).

---

## 6. Open items / notes

- JSON tool protocol assumes a model that can follow a strict schema; for weaker
  local models the system prompt must be very explicit about the exact JSON
  shape.
- Tool results should be truncated before being fed back into history to avoid
  exhausting the context window.
- Max loop iterations and a per-step timeout should be configurable in settings.

