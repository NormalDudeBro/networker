# Networker Copilot

A modern WinUI 3 desktop app for network engineers — deterministic tools + AI-powered chat.

## Features

### Deterministic Tools (run locally, no LLM)
- **IP Calculator** — v4/v6 subnet math (network, netmask, wildcard, usable hosts, RFC 5952 formatting)
- **Config Generator** — JSON DeviceSpec → Cisco IOS-XE, Juniper Junos, Arista EOS, VyOS
- **Config Audit** — 16 security/best-practice rules with line numbers
- **Config Diff** — Myers LCS unified diff
- **Log Analyzer** — RFC 3164/5424 syslog parsing + 10 anomaly detectors
- **Playbooks** — 6 troubleshooting/deployment scenarios (markdown)
- **Topology** — graph from subnets/BGP/static routes → Mermaid diagram
- **Translator** — Cisco IOS-XE ↔ Juniper Junos

### Network Config (migrated from NetworkConfigPro)
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
- **Keyboard first** — `Ctrl+K` command palette, `Ctrl+1..5` page shortcuts, `Ctrl+Enter` on the
  Generate / Parse / Diff forms, `F5` provider health check

## Architecture

```
networker/                 # WinUI 3 app (net8.0-windows10.0.19041)
├── Controls/              # CodeBlockView, MessageTemplateSelector, CommandPalette
├── Models/                # ChatMessage, ChatRole, ActivityItem
├── Services/              # ChatService, LlmRuntime, RecentActivity, Toaster, ConfigSyntaxHighlighter
├── Styles/                # Colors, Fonts, Styles (design tokens, theme switching)
├── Views/DashboardPage.xaml  # Dashboard landing (quick actions, activity feed, AI status)
├── MainPage.xaml          # Assistant workspace (chat, history panel, quick tools)
├── ToolsPage.xaml         # 8-tab deterministic toolkit
├── SettingsPg.xaml        # Provider/model/prompt/theme, Network Config defaults
├── NetworkConfig/         # Network Config feature (5 tabs, ported from NetworkConfigPro)
└── App.xaml               # DI container, theme at Application level

Networker.Core/               # Deterministic net logic (net8.0, no UI deps)
├── Llm/                   # Provider layer (config, router, retry, SSE)
├── Prompting/             # PromptBuilder
├── NetTools/
│   ├── Ip/                # IpToolkit, IpSubnetInfo
│   ├── Config/            # ConfigAuditor, TextDiff, DeviceSpec, ConfigGenerator, ConfigTranslator
│   ├── Logs/              # LogAnalyzer
│   ├── Playbooks/         # PlaybookGenerator
│   └── Topology/          # TopologyBuilder
├── Models/NetworkConfig/  # Device + feature models (Vendor, Interface, VLAN, ACL, routing, STP)
├── Services/NetworkConfig/# Generator dispatcher, parser factory, validator, vault, template library
└── Tests/                 # 253 xUnit tests (no external deps)
```

## Getting Started

```powershell
# Prereqs: .NET 8 SDK, Windows 10 19041+
git clone https://github.com/your/repo networker
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

## Tests

```powershell
dotnet test Networker.Core.Tests\Networker.Core.Tests.csproj
# 253 tests passing (unit + golden-parity + performance smoke, no external deps)
```

## Key Design Decisions

- **Deterministic first** — All net logic in `Networker.Core`, unit-testable, no LLM
- **LLM for explanation only** — Summary, troubleshooting, translation, doc generation
- **Zero-warning build** — CI enforces clean output
- **Design system** — Tokens for colors/fonts/radius, single-theme Application resource
- **Dependency Injection** — `Microsoft.Extensions.DependencyInjection` in `App.xaml.cs`

## License

MIT