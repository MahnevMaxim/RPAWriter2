# Native tool calling (with JSON-over-chat fallback)

> Status: implemented. Two modes in Settings → "Tool calling".

## 1. Goal

Give the agent native OpenAI-style function calling (`tools` array in the request,
`tool_calls` in the response), while keeping the existing JSON-over-chat protocol
as a fallback for providers without tool support (Ollama / LM Studio / vLLM).

## 2. Modes

| Mode | Request | Response | Default |
|---|---|---|---|
| **JSON over chat** | no `tools`; `tool` role downgraded to `user` | JSON `{"action":"final"/"tool_call",...}` in plain text | **yes** |
| **Native function calling** | `tools` + `tool_choice:"auto"`; `tool` role + `tool_call_id` | native `tool_calls` | no |

The setting is `SettingsData.ToolCallingMode` (`JsonChat` | `Native`), persisted to
`settings.json` and edited via a `ComboBox` in the Settings tab.

## 3. How it works

- `AgentRunner` always builds a `ToolDefinition` list from the registry filtered by
  Plan/Act mode and passes it to the provider.
- The provider decides the protocol from `OpenAiOptions.ToolCallingMode`:
  - `Native` — emits `tools`/`tool_choice`, keeps the `tool` role with `tool_call_id`,
    and parses `message.tool_calls`.
  - `JsonChat` — current behavior (no `tools`, `tool` → `user`, parse JSON text).
- Tool schemas are converted from the informal `IAgentTool.ParametersSchema`
  (e.g. `{"path":"string"}`) to OpenAI JSON Schema by `ToolSchemaConverter`.

## 4. Data model

- `enum ToolCallingMode { JsonChat, Native }`.
- `class ToolDefinition { Name, Description, Parameters(JObject) }`.
- `class NativeToolCall { Id, Name, Arguments(JObject) }`.
- `ChatResponse.ToolCalls` — native tool calls returned by the model.
- `ChatMessageData.ToolCallId` — `tool_call_id` for native tool-result messages.

## 5. Native loop

1. Build system prompt (native variant: no JSON-protocol instructions) + transcript.
2. Send request with `tools`.
3. If the response has `tool_calls` → execute each (gated by `Mutates` in Plan mode),
   append `role="tool"` + `tool_call_id` messages, repeat (capped by `MaxIterations`).
4. If there are no `tool_calls` → `message.content` is the final answer.

Overflow recovery (compact + retry) and max-tokens recovery (nudge + retry) work
unchanged in both modes.

## 6. Streaming

Tool-call arguments are accumulated across SSE deltas by `index`: `function.name`
arrives first, then `function.arguments` fragments are concatenated and parsed to
JSON when the stream ends.

## 7. Prompts

- `agent-plan-system-native.txt` / `agent-act-system-native.txt` — same role and
  mode constraints, but no "output EXACTLY one JSON object" protocol; the tool list
  is provided as native function definitions.

## 8. Compatibility

| Provider | JsonChat | Native |
|---|---|---|
| OpenAI / Azure | yes | yes |
| Ollama (modern) | yes | yes (model-dependent) |
| LM Studio | yes | model-dependent |
| vLLM | yes | needs `--enable-auto-tool-choice` + `--tool-call-parser` |
| any without tools | yes | no — keep JsonChat |

`UseToolRole` only affects JsonChat; Native always uses the `tool` role (API requires it).

## 9. Files

Core: `Models/Models.cs`, `Contracts/Contracts.cs`,
`AgentTools/ToolSchemaConverter.cs` (new), `AgentTools/AgentPromptBuilder.cs`,
`AgentTools/AgentRunner.cs`, `Services/OpenAiCompatibleProvider.cs`,
`Services/PromptDefaults.cs`, `Services/PromptSet.cs`, `Services/StubAiProvider.cs`.

VSIX: `Services/SettingsStore.cs`, `Services/AiProviderFactory.cs`,
`ToolWindow1Control.xaml`, `ToolWindow1Control.xaml.cs`,
`Prompts/System/agent-plan-system-native.txt`, `Prompts/System/agent-act-system-native.txt`.

Tests: `ToolCallingTests.cs` (new), extensions in `AgentToolsTests.cs` / `ProviderTests.cs`.
