# Networker

A focused WinUI 3 troubleshooting workspace for network engineers: deterministic tools, persisted evidence, and optional AI escalation.

## Documentation

- [Project roadmap](ROADMAP.md)
- [Updates and release operations](docs/UPDATES.md)

## Troubleshooting Workflow

Networker is organized as nine numbered stages across the top of the window:

1. **Start** — capture the incident, symptoms, environment, and constraints.
2. **Inspect** — import or paste configurations and logs.
3. **Diagnose** — audit configuration risks and analyze log anomalies.
4. **Map** — calculate addressing and infer topology.
5. **Compare** — compare baseline and candidate configurations.
6. **Plan** — generate deterministic or AI-assisted operational playbooks.
7. **Resolve** — generate or translate corrective multi-vendor configuration.
8. **Assist** — escalate unresolved findings with explicitly attached workspace evidence.
9. **Settings** — configure AI, templates, the encrypted vault, appearance, and updates.

Plain number keys `1` through `9` switch stages when focus is outside a text-entry control. Previous and Next actions remain visible throughout the sequence. The latest non-secret workspace and current chat restore after restart; **Clear workspace** removes evidence, progress, activity, and chat without touching settings, templates, or vault data.

## Features

### Workflow tools

The Workflow stages combine focused one-shot utilities with guided configuration work. Deterministic
operations run locally; selected workflows can optionally ask the configured model to explain results.

#### Focused tools
- **IP Calculator** — v4/v6 subnet math (network, netmask, wildcard, usable hosts, RFC 5952 formatting)
- **JSON Generator** — JSON DeviceSpec → Cisco IOS-XE, Juniper Junos, Arista EOS, VyOS
- **Config Audit** — 16 security/best-practice rules with line numbers
- **Config Diff** — Myers LCS unified diff
- **Log Analyzer** — RFC 3164/5424 syslog parsing + 10 anomaly detectors
- **Playbooks** — 6 troubleshooting/deployment scenarios (markdown)
- **Topology** — graph from subnets/BGP/static routes → Mermaid diagram
- **Translator** — Cisco IOS-XE ↔ Juniper Junos

#### Guided configuration
- **Generate** — device form for 6 vendors (Cisco IOS, Cisco NX-OS, Arista EOS, Juniper Junos,
  SONiC, Fortinet FortiGate) with interfaces, VLANs, ACLs, OSPF/BGP/EIGRP, STP, and predefined
  templates; output validated against the ported rule set
- **Import / Analyze** — paste a Cisco IOS / Juniper Junos / SONiC config (or a syslog file) for
  auto-detected parsing, structured results, and validation issues
- **Diff** — unified config comparison (additions/deletions stats)
- **Vault** — password-protected credential and variable store (PBKDF2-SHA256 + AES-256-GCM,
  `%LOCALAPPDATA%\Networker\vault.dat`)
- **Templates** — built-in template gallery with preview; custom templates are deletable and persist
  to `%LOCALAPPDATA%\Networker\custom_templates.json`

### AI Chat
- Provider abstraction: **Ollama** (local), **Grok** (x.ai), **Gemini** (Google)
- Fallback chain, retry with exponential backoff, streaming tokens
- Credential scrubbing before send
- Tool cards: deterministic results render inline (CodeBlockView) + Assistant panel history

### Workspace
- **Dashboard** — landing page with quick actions, a recent-activity feed (30 most recent events with
  relative timestamps), and live AI connection status
- **Activity log** — every tool run, config generation/parse/diff, and vault change is recorded
- **Keyboard first** — `Ctrl+K` command palette, `1..9` stage shortcuts, `Ctrl+Enter` on the
  Generate / Parse / Diff forms, `F5` provider health check
- **Responsive workspace** — adaptive shell/status priority, Start and workflow layouts, Assistant panel,
  side-by-side editors, vault forms, and template detail views; bounded ultrawide page hosts,
  short-window editor caps, and shared horizontal scrolling for dense tables and code

## Architecture

```
networker/                 # WinUI 3 app (net8.0-windows10.0.19041)
├── Controls/              # CodeBlockView, MessageTemplateSelector, CommandPalette
├── Models/                # ChatMessage, ChatRole, ActivityItem
├── Services/              # ChatService, LlmRuntime, RecentActivity, Toaster, ConfigSyntaxHighlighter
│   └── Updates/           # InstalledVersion, cache/storage, MSIX installer, scheduler, logger, restart
├── Styles/                # Colors, Fonts, Styles (design tokens, theme switching)
├── Views/DashboardPage.xaml  # Dashboard landing (quick actions, activity feed, AI status)
├── Views/AssistantPage.xaml  # Stage 8 Assist workspace and persisted chat
├── Views/WorkflowPage.xaml   # Stages 2-7 deterministic troubleshooting workflows
├── Views/SettingsPage.xaml   # Stage 9 AI, templates, vault, appearance, and updates
├── NetworkConfig/Views/Tabs/ # Reusable Generate/Import/Diff/Vault/Templates controls
└── App.xaml               # DI container, theme at Application level

Networker.Core/               # Deterministic net logic (net8.0, no UI deps)
├── Llm/                   # Provider layer (config, router, retry, SSE)
├── Prompting/             # PromptBuilder
├── Updates/               # Version policy, release client, downloader, verifier, coordinator
├── NetTools/
│   ├── Ip/                # IpToolkit, IpSubnetInfo
│   ├── Config/            # ConfigAuditor, TextDiff, DeviceSpec, ConfigGenerator, ConfigTranslator
│   ├── Logs/              # LogAnalyzer
│   ├── Playbooks/         # PlaybookGenerator
│   └── Topology/          # TopologyBuilder
├── Models/NetworkConfig/  # Device + feature models (Vendor, Interface, VLAN, ACL, routing, STP)
├── Services/NetworkConfig/# Generator dispatcher, parser factory, validator, vault, template library
Networker.Core.Tests/      # xUnit behavior, golden-output, and architecture tests
```

## Getting Started

```powershell
# Prereqs: .NET 8 SDK, Windows 10 19041+
git clone https://github.com/NormalDudeBro/networker
cd networker

# Build & run
dotnet build networker.csproj -c Debug -p:Platform=x64
dotnet run --project networker.csproj -c Debug -p:Platform=x64
```

### AI Provider Setup
Copy `.env.example` to `.env` and fill in keys:

```bash
# .env (gitignored)
LLM_PROVIDER=ollama
OLLAMA_HOST=http://localhost:11434
OLLAMA_MODEL=llama3.1
# XAI_API_KEY=...
# GEMINI_API_KEY=...
```

Or configure in-app via **Settings → Provider**.

## Distribution & Updates

Production packages are distributed as a **trusted-certificate-signed x64 MSIX** through
GitHub Releases. When trusted signing is not configured, a release contains source archives
only; Networker never publishes an unsigned installable package. Signed package releases
ship the MSIX, its SHA-256 checksum sidecar, an App Installer manifest, and the public
certificate required for one-time trust installation.
See [docs/UPDATES.md](docs/UPDATES.md) for the full version, asset, and release contract.

In-app, **Settings → APPLICATION UPDATES** shows the installed version and provides the
update channel toggles (stable by default, previews opt-in), a manual check, release
notes, download/install progress, and Restart / Later controls. Automatic checks run in
the background on a 24-hour cadence with failure backoff and never block startup or
offline use. Updates never touch your settings, prompts, vault, templates, or
configuration files.

## Tests

```powershell
dotnet test Networker.Core.Tests\Networker.Core.Tests.csproj
# Current: 481 tests (unit + golden-parity + architecture + performance smoke, no external deps)
```

## Key Design Decisions

- **Deterministic first** — All net logic in `Networker.Core`, unit-testable, no LLM
- **LLM for explanation only** — Summary, troubleshooting, translation, doc generation
- **Zero-warning build** — CI enforces clean output
- **Design system** — Tokens for colors/fonts/radius, single-theme Application resource
- **Adaptive layout** — Shared page geometry, width/height visual states, wrapping action surfaces,
  and bounded content preserve access across snapped, resized, ultrawide, high-DPI, and short-window
  layouts without sacrificing the dense desktop workflow
- **Dependency Injection** — `Microsoft.Extensions.DependencyInjection` in `App.xaml.cs`

## License

MIT
