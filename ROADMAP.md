# Networker Roadmap

## Product vision

Networker is a Windows troubleshooting workspace for network engineers. It combines deterministic local tools, persisted incident evidence, guided multi-vendor workflows, and optional AI assistance without making network operations depend on an external model.

## Nine-stage workflow

1. **Start** - capture the incident, symptoms, environment, and constraints.
2. **Inspect** - import configurations and logs.
3. **Diagnose** - identify configuration risks and log anomalies.
4. **Map** - calculate addressing and infer topology.
5. **Compare** - compare baseline and candidate configurations.
6. **Plan** - produce operational playbooks.
7. **Resolve** - generate or translate corrective configuration.
8. **Assist** - investigate with optional AI and explicitly selected evidence.
9. **Settings** - manage providers, templates, the vault, appearance, and updates.

The stages form one durable workspace: evidence, progress, activity, and chat can survive restarts, while settings and secrets remain separate from incident data.

## Architecture

The solution contains three projects:

- `networker.csproj` - the WinUI 3 application, shell, views, controls, and application services.
- `Networker.Core/Networker.Core.csproj` - UI-independent networking, workflow, LLM, configuration, and update logic.
- `Networker.Core.Tests/Networker.Core.Tests.csproj` - xUnit behavior, golden-output, performance-smoke, and architecture tests.

The primary app views are `Views/AssistantPage.xaml`, `Views/WorkflowPage.xaml`, `Views/SettingsPage.xaml`, and `Views/DashboardPage.xaml`. Reusable guided configuration controls remain under `NetworkConfig/Views/Tabs/`.

## Milestones

- Ported and verified multi-vendor configuration generation, parsing, validation, templates, and encrypted vault services.
- Consolidated troubleshooting into the nine-stage workflow with persistent workspace evidence and keyboard navigation.
- Added deterministic IP, audit, diff, log, playbook, topology, translation, and configuration tools.
- Integrated optional Ollama, Grok, and Gemini assistance with credential scrubbing, retry, fallback, and streaming.
- Established signed MSIX delivery, in-app update policy, release automation, and x64 production publishing.
- Centralized shared behavior in `Networker.Core` and protected it with unit, golden, architecture, and smoke tests.

## Architectural decisions

- **Deterministic first.** Networking operations belong in `Networker.Core`; AI may explain or assist but is not required for core workflows.
- **Three-project boundary.** The WinUI app owns presentation, Core owns reusable behavior, and the test project verifies Core and repository architecture.
- **Two configuration generators remain distinct.** `Networker.Core/NetTools/Config/ConfigGenerator.cs` with `DeviceSpec` supports automation-friendly JSON generation for four platforms. `Networker.Core/Services/NetworkConfig/NetworkConfigGenerator.cs` with `NetworkDeviceConfig` powers the guided six-vendor Resolve workflow and golden-compatible templates. Their different inputs and use cases do not justify merging them.
- **Code-behind is intentional.** Views follow the existing WinUI code-behind design; view models are introduced only when behavior warrants the additional layer.
- **Compatibility matters.** Persisted workspace, settings, templates, vault data, and legacy route aliases must remain usable through cleanup.
- **Production is x64.** Debug solution mappings may support other architectures, but local and automated production publishing produces the signed, self-contained x64 MSIX only. Release trimming is disabled because WinUI 3 and reflection-driven application paths are not trim-safe enough for this package.

## Quality status

The cleanup verification includes 481 passing tests. Treat this as a point-in-time count rather than a permanent total: the current authoritative count is the result of `dotnet test Networker.Core.Tests/Networker.Core.Tests.csproj`. CI also restores the solution, builds Debug x64, and publishes Release x64.

The quality target is a clean build and publish, stable golden configuration output, no required external services for tests, and no regressions in persistence, navigation, networking behavior, or update packaging.

## Future work

- Add UI automation for the nine stages, keyboard navigation, responsive layouts, and theme coverage.
- Expand the platform and feature compatibility matrix where real workflows require it.
- Evaluate true unsigned 32-bit ASN support as a deliberate model and serialization change.
- Extract view models only for presentation logic that becomes difficult to test or maintain in code-behind.
- Continue release validation across clean installations, upgrades, signing, and recovery paths.

## Update log

- **2026-08-12 - Repository cleanup:** replaced the obsolete migration journal with this product roadmap; documented the current workflow, three-project architecture, distinct generators, x64 release policy, quality baseline, and planned view locations; refreshed README, environment branding, and CI release wording.
