# Networker

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

### AI Chat
- Provider abstraction: **Ollama** (local), **Grok** (x.ai), **Gemini** (Google)
- Fallback chain, retry with exponential backoff, streaming tokens
- Credential scrubbing before send
- Tool cards: deterministic results render inline (CodeBlockView) + Assistant panel history

## Architecture

```
networker/                 # WinUI 3 app (net8.0-windows10.0.19041)
├── Controls/              # CodeBlockView, MessageTemplateSelector, CommandPalette
├── Models/                # ChatMessage, ChatRole
├── Services/              # ChatService, LlmRuntime, Toaster, ConfigSyntaxHighlighter
├── Styles/                # Colors, Fonts, Styles (design tokens, theme switching)
├── MainPage.xaml          # Chat workspace, sidebar, input
├── ToolsPage.xaml         # 8-tab deterministic toolkit
├── SettingsPg.xaml        # Provider/model/prompt/theme
└── App.xaml               # DI container, theme at Application level

NetOps.Core/               # Deterministic net logic (net8.0, no UI deps)
├── Llm/                   # Provider layer (config, router, retry, SSE)
├── Prompting/             # PromptBuilder
├── NetTools/
│   ├── Ip/                # IpToolkit, IpSubnetInfo
│   ├── Config/            # ConfigAuditor, TextDiff, DeviceSpec, ConfigGenerator, ConfigTranslator
│   ├── Logs/              # LogAnalyzer
│   ├── Playbooks/         # PlaybookGenerator
│   └── Topology/          # TopologyBuilder
└── Tests/                 # 112 xUnit tests (Stubs, no external deps)
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
dotnet test NetOps.Core.Tests\NetOps.Core.Tests.csproj
# 112 tests passing
```

## Key Design Decisions

- **Deterministic first** — All net logic in `NetOps.Core`, unit-testable, no LLM
- **LLM for explanation only** — Summary, troubleshooting, translation, doc generation
- **Zero-warning build** — CI enforces clean output
- **Design system** — Tokens for colors/fonts/radius, single-theme Application resource
- **Dependency Injection** — `Microsoft.Extensions.DependencyInjection` in `App.xaml.cs`

## License

MIT
