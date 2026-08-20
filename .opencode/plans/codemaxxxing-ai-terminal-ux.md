# Plan: Codemaxxxing-style AI + Terminal UX in Networker

## Context

**Goal:** Make Networker's Assist experience feel like codemaxxxing for (1) structured AI turn presentation and (2) real terminal interaction integrated with the agent workflow — as a native WinUI GUI, not a TUI clone.

**User decisions (locked):**

1. **Terminal scope:** Full interactive Terminal panel **plus** live agent command streaming in turns **plus** specialized `$ cmd` renderer.
2. **Composer:** AI vs Terminal mode toggle + `!` shortcut (cmx-style); Enter submits to active mode; Shift+Enter newline; Esc leaves terminal mode.
3. **Output depth:** **Full parity push** — markdown, colored diffs, plan/todo blocks, prompt history, quiet-tool coalescing polish, ANSI handling, jump-to-latest, smart scroll, etc.

**Trigger:** Networker already has `AssistantTurn` + `ActivityBlock` + Codex routing modeled on codemaxxxing, but lacks interactive terminal, shell mode, plan blocks, specialized terminal/diff/markdown renderers, local agent command streaming, and smart scroll.

---

## Codebase Analysis

### Reference: Codemaxxxing (`C:\codebase\cmx-fork`)

#### Architecture

```text
User Input (normal | shell via !)
  -> session.prompt / session.shell / tools
  -> Message + ordered Parts (message-v2)
  -> TUI process-tool / packages/ui PART_MAPPING
  -> reasoning | markdown | tool | bash/process | diff | todo
```

#### Critical source files

| Area | Path |
|------|------|
| Parts | `cmx-fork\packages\opencode\src\session\message-v2.ts` |
| Todos | `cmx-fork\packages\opencode\src\session\todo.ts` |
| PTY | `cmx-fork\packages\opencode\src\pty\index.ts` |
| Shell tool | `cmx-fork\packages\opencode\src\tool\shell.ts` |
| In-turn terminal UX | `cmx-fork\packages\opencode\src\cli\cmd\tui\routes\session\process-tool.tsx` |
| Session TUI | `cmx-fork\packages\opencode\src\cli\cmd\tui\routes\session\index.tsx` |
| Prompt modes | `cmx-fork\packages\opencode\src\cli\cmd\tui\component\prompt\index.tsx` |
| Web parts | `cmx-fork\packages\ui\src\components\message-part.tsx` |
| Session turn / autoscroll | `cmx-fork\packages\ui\src\components\session-turn.tsx` |
| Diff badge | `cmx-fork\packages\ui\src\components\diff-changes.tsx` |
| Markdown | `cmx-fork\packages\ui\src\components\markdown.tsx` |
| Todo row | `cmx-fork\packages\opencode\src\cli\cmd\tui\component\todo-item.tsx` |

#### UX contracts from process-tool + prompt

1. One AI turn = one unit (parts then final text).
2. Tool row: action+detail left; verdict whisper right (`done · 1.2s` / `exit 2`).
3. Commands in transcript = loud `$ cmd` run objects; interactive PTY is separate.
4. Strip ANSI in transcript; collapse long output (~10 lines) + "n more".
5. Modes `normal` | `shell`; `!` at offset 0 enters shell; Esc/backspace-at-0 leaves.
6. Enter submits; paste must not be stolen from textarea.
7. Todos = status temperature list (in_progress bold, completed sunk, pending muted).
8. Auto-scroll only while user is near bottom; jump-to-latest when pinned away.

#### Do not port literally

- OpenTUI/Solid, multi-agent wave tools, full multi-PTY WebSocket desktop protocol.

### Target: Networker (`C:\Users\Kenny\source\repos\networker`)

#### Stack

WinUI 3 app + `Networker.Core`; code-behind views (ROADMAP intentional).

#### Already implemented (REUSE)

| Piece | Path | Notes |
|-------|------|-------|
| Turn | `Models\AssistantTurn.cs` | Blocks + Text + BusyText/Footer — part-sequence shaped |
| Blocks | `Models\ActivityBlock.cs` | Thinking/Tool/Edit/ActivityLine/Error; collapse; verdicts; +N/-N |
| Selectors | `Controls\BlockTemplateSelector.cs`, `MessageTemplateSelector.cs` | |
| Assist UI | `Views\AssistantPage.xaml(.cs)` | Enter/Shift+Enter at PreviewKeyDown ~95-104 |
| Routing | `AssistantPage.xaml.cs` RouteActivity ~325-539 | command-output streaming already handled |
| Agent | `Services\AgentService.cs` | Codex vs local |
| Orchestrator | `Networker.Core\Agent\AgentOrchestrator.cs` | JSON tools |
| Commands | `Networker.Core\Agent\CommandRunner.cs` | Job Object + allowlist; **buffers until exit** |
| Codex | `Services\Codex\CodexAgentService.cs` | Streams reasoning/text/command-output/diffs |
| Activity DTO | `Networker.Core\Agent\AgentActivity.cs` | |
| Code block | `Controls\CodeBlockView.xaml` | |
| Diff engine | `Networker.Core\NetTools\Config\TextDiff.cs` | UI not colored yet |
| Persist | `Networker.Core\Workflow\TroubleshootingWorkspace.cs` | extend DTOs carefully |

#### Gaps (all in scope)

G1 interactive Terminal panel; G2 CommandRunner not streaming; G3 plain tool body; G4 uncolored diffs; G5 no Plan block; G6 plain final text; G7 no AI/Terminal composer mode; G8 chat unstructured; G9 always ScrollToBottom; G10 Codex complete may lack Output; G11 Ctrl+Enter tooltip; G12 no AnsiStripper; G13 no prompt history; G14 quiet-tool polish; G15 no stream batching.

#### Do not rewrite

LLM providers/settings/Codex auth, workflow/vault/generators, update stack, agent CommandPolicy allowlist.

---

## Approach

Extend turn/block architecture; add real `TerminalSession` + pane; specialized renderers; dual composer mode; full parity polish.

```text
Provider/Codex/Orchestrator -> AgentActivity
  -> AssistantTurn.Blocks + Text
  -> Thinking | Plan | Tool/Terminal | Diff | Markdown
  -> AssistantPage + TerminalPane + Composer(AI|Terminal)
```

### Locked choices

| Topic | Choice |
|-------|--------|
| Terminal | Full panel + in-turn live agent commands |
| Composer | Toggle + `!` -> Terminal; Esc/backspace-at-0 -> AI; single InputBox dual-mode |
| Shell | PowerShell `-NoLogo` default |
| Agent cmds | One-shot CommandRunner (allowlist+Job); stream into turn; optional "Run in Terminal" copy |
| User shell | Separate trust boundary; caption in UI |
| Markdown | Markdig preferred; else minimal custom + CodeBlockView fences |
| ANSI | Strip v1 (transcript + pane) |
| Plan | PlanBlock + orchestrator `plan` action + Codex if available |

---

## Changes

### Wave 0 — Models / DTOs / tokens / AnsiStripper

**Modify**

- `Models\ActivityBlock.cs`: add `PlanBlock`+`PlanItem`; enhance `ToolBlock` (`CommandLine`, `ExitCode`, `WorkingDirectory`, `IsTerminalStyle`); keep collapse ~10/6/3 lines
- `Models\AssistantTurn.cs`: BusyText for plan/terminal
- `Controls\BlockTemplateSelector.cs`: PlanTemplate
- `Networker.Core\Agent\AgentActivity.cs`: Kind plan / plan payload
- `Networker.Core\Workflow\TroubleshootingWorkspace.cs`: WorkspaceTurnBlockDto fields (plan items, exit, cwd) with backward-compatible defaults
- `Styles\Colors.xaml`, `Styles.xaml`: terminal + diff brushes
- `AssistantPage.xaml.cs`: ToBlockDto/FromBlockDto/RouteActivity for plan

**Create**

- `Networker.Core\Text\AnsiStripper.cs`
- `Networker.Core.Tests\Text\AnsiStripperTests.cs`

### Wave 1 — Streaming CommandRunner

**Modify** `Networker.Core\Agent\CommandRunner.cs`:

```csharp
public sealed record CommandOutputChunk(string Text, CommandOutputChannel Channel);
public enum CommandOutputChannel { StdOut, StdErr }

Task<AgentCommandResult> RunAsync(
    AgentCommand command,
    IProgress<CommandOutputChunk>? progress = null,
    CancellationToken cancellationToken = default);
```

Keep bounds 131072, Job Object, allowlist, no metacharacters.

**Modify** `AgentOrchestrator.cs` command case (~122-138): emit `command-output` streaming activities (same shape Codex already uses); complete with verdict + Output + DurationSeconds.

**Tests** `Networker.Core.Tests\Agent\CommandRunnerStreamingTests.cs`: progress before exit; cancel kills; allowlist.

### Wave 2 — Renderers

**Create controls**

| Control | Path | Contract |
|---------|------|----------|
| TerminalOutputView | `Controls\TerminalOutputView.xaml(.cs)` | `$ cmd`, mono body, verdict footer, collapse, copy, ANSI strip |
| DiffBlockView | `Controls\DiffBlockView.xaml(.cs)` | path +N/-N; TextDiff colored lines |
| PlanBlockView | `Controls\PlanBlockView.xaml(.cs)` | todo temperature list |
| MarkdownTextView | `Controls\MarkdownTextView.xaml(.cs)` | headings/lists/code fences->CodeBlockView/links |
| PromptHistory | `Services\PromptHistory.cs` | AI Up/Down history |

**Wire** `AssistantPage.xaml` templates: terminal-style tools, diffs, plan, final MarkdownTextView.

Optional Markdig PackageReference on `networker.csproj`.

### Wave 3 — TerminalSession + TerminalPane

**Create** `Networker.Core\Agent\TerminalSession.cs` (UI-free):

- Start PS in workspace cwd; stream OutputReceived/Exited
- WriteLine/Write; Interrupt/Kill via Job Object
- ~1 MiB head-tail buffer; Restart(); inherit user env (document vs agent env)

**Create** `Controls\TerminalPane.xaml(.cs)`: line list, kill/clear/copy, security caption, stick-to-bottom scroll.

**Layout** AssistantPage: collapsible bottom terminal (~220-320px), header toggle; cwd follows agent workspace.

**Bridge:** "Run in Terminal" on tool rows copies command; do not auto-feed terminal to model.

**DI** in `App.xaml.cs`.

ConPTY preferred; redirected pipes + PowerShell acceptable fallback if ConPTY slips.

### Wave 4 — Composer modes / keyboard / history

`ComposerMode { Ai, Terminal }` on AssistantPage.

| Behavior | Detail |
|----------|--------|
| Toggle + `!` at caret 0 | Enter Terminal mode |
| Esc / Backspace at 0 | AI mode |
| Enter | AI->SendAsync; Terminal->WriteLine (allowed even if AI busy) |
| Shift+Enter | newline (keep `_shiftDown`) |
| Placeholders/tooltips | Enter not Ctrl+Enter |
| History | Up/Down AI mode at ends |
| Paste | no page-level Ctrl+V intercept |

Keep PreviewKeyDown pattern; branch on mode only.

### Wave 5 — Plan protocol + Codex completeness

- Orchestrator: `action: "plan"` + todos array; emit Kind plan
- Codex: completed command include output if protocol has it; map todos if present
- Polish RouteQuietActivity coalescing / CallId dedupe

### Wave 6 — Scroll / batch / perf

- MessagesList ScrollViewer ViewChanged -> `_stickToBottom`
- Scroll only if stuck; "Jump to latest" chip
- Batch command-output UI ~50ms
- AnsiStripper in TerminalOutputView + TerminalPane
- Cap displayed lines; full text retained for copy

### Wave 7 — QA + chat markdown

- Final answers always through MarkdownTextView
- No fake thinking in pure chat
- Full manual checklist + `dotnet test` + Debug x64 build

---

## File list

### Create

- `Networker.Core\Text\AnsiStripper.cs`
- `Networker.Core\Agent\TerminalSession.cs`
- `Controls\TerminalOutputView.xaml(.cs)`
- `Controls\TerminalPane.xaml(.cs)`
- `Controls\DiffBlockView.xaml(.cs)`
- `Controls\PlanBlockView.xaml(.cs)`
- `Controls\MarkdownTextView.xaml(.cs)`
- `Services\PromptHistory.cs`
- `Networker.Core.Tests\Text\AnsiStripperTests.cs`
- `Networker.Core.Tests\Agent\CommandRunnerStreamingTests.cs`
- `Networker.Core.Tests\Agent\TerminalSessionTests.cs`

### Modify

- `Networker.Core\Agent\CommandRunner.cs`
- `Networker.Core\Agent\AgentOrchestrator.cs`
- `Networker.Core\Agent\AgentActivity.cs`
- `Models\ActivityBlock.cs`, `Models\AssistantTurn.cs`
- `Controls\BlockTemplateSelector.cs`
- `Views\AssistantPage.xaml(.cs)`
- `Services\Codex\CodexAgentService.cs`
- `Networker.Core\Workflow\TroubleshootingWorkspace.cs`
- `Styles\Colors.xaml`, `Styles.xaml`
- `App.xaml.cs`, `networker.csproj`

---

## Dead Ends / Constraints

1. No OpenTUI/xterm-only primary terminal.
2. Do not weaken agent CommandPolicy.
3. Do not replace AssistantTurn with generic bubbles.
4. No fake process output.
5. Assist remains GUI with integrated terminal, not terminal-only app.
6. Old workspace JSON must deserialize (new DTO fields optional).
7. Agent cmds stay one-shot Job Object; user terminal is separate trust boundary.

---

## Verification

```powershell
dotnet test C:\Users\Kenny\source\repos\networker\Networker.Core.Tests\Networker.Core.Tests.csproj
dotnet build C:\Users\Kenny\source\repos\networker\networker.csproj -c Debug -p:Platform=x64
```

Manual: Enter/Shift+Enter/paste/`!`/Esc; thinking/plan/tools/live cmd/diff/markdown; terminal stream/kill/exit; agent+Codex integration; history restore; no freeze on large output.

---

## Dependencies

```text
W0 models/tokens/AnsiStripper
 -> W1 CommandRunner stream (parallel W2 renderers after W0)
 -> W2 renderers
 -> W3 TerminalSession+Pane
 -> W4 composer modes/history
 -> W5 plan+Codex polish
 -> W6 scroll/batch
 -> W7 QA
```

---

## Executor notes

**Enter (keep + branch):** `AssistantPage.xaml.cs` ~95-104

```csharp
if (e.Key != VirtualKey.Enter) return;
if (_shiftDown) return;
e.Handled = true;
if (_composerMode == ComposerMode.Terminal) { _ = SubmitTerminalAsync(); return; }
if (!_isBusy) _ = SendAsync();
```

**command-output routing already exists** at RouteTool ~393-404 — local agent must emit same events as Codex.

**Visual contract:**

```text
$ git status                         done · 0.4s
  On branch main
  ...
  3 more
```

**cmx refs:** process-tool.tsx (terminal-in-turn); prompt/index.tsx modes; message-v2 ToolPart states; todo-item.tsx plan temperature.

---

## Status

- [x] Inspected cmx-fork and Networker
- [x] User decisions locked (full panel+stream, mode+!, full parity)
- [x] Plan complete — ready for execution
- [x] Implementation (Waves 0-7)
