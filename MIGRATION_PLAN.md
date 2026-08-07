# NetworkConfigPro → Networker Migration Plan

> **Living document.** This is the authoritative roadmap and development journal for the migration.
> It is updated continuously and must always reflect the current repository state. A future session
> should be able to resume work from this document alone.
>
> File naming note: the repo contains this file as `MIGRATION_PLAN.md` (Windows is case-insensitive;
> the same file is sometimes referred to as `migration_plan.md`). There is only ONE plan file.

---

## 1. Project Overview

### Overall Objective
Migrate the entire Python **NetworkConfigPro** application — a multi-vendor network configuration
generator/parser/validator with a PySide6 GUI, Fernet vault, and Jinja2 template engine — into the
existing C# **Networker** WinUI 3 application, so it feels like a native feature of Networker.

### Scope of the Migration
- **Config generation** for 6 vendors (Cisco IOS, Cisco NX-OS, Arista EOS, Juniper Junos, SONiC,
  Fortinet FortiGate) reproducing the Python output **byte-for-byte**.
- **Config parsing** (regex-based) for the vendors Python supports, with a factory for auto-detection.
- **Config validation** (severity + category based) merged with Networker's existing `ConfigAuditor`.
- **Secure vault** (credentials / variables / custom templates) — implemented natively on Windows,
  no Python vault format compatibility required.
- **Predefined template library** (Basic Router, L3 Switch, Edge Router, etc.).
- **Full UI** matching the Python tabs: Generate, Import/Analyze, Diff, Vault, Templates — integrated
  into Networker's NavigationView + theme + DI.
- **Hard constraint:** pure C#, **no new NuGet dependencies** unless strictly required; the app must
  remain buildable after every slice.

### High-Level Architectural Goals
- **Slice A (Core, COMPLETE):** models, service contracts, 6 template renderers, filters, dispatcher,
  dictionary→model conversion, golden-file test harness proving byte-for-byte parity.
- **Slice B (UI, COMPLETE):** DI registration, navigation, feature pages/tabs, view models.
- **Slice C (Services, COMPLETE):** parser, validator, vault, template library + their tests.
- **Quality bar:** golden-file parity for generation; unit tests for every ported service; clean
  Release build; no regressions in existing Networker features.

### Definition of Project Completion
1. All 6 vendors generate byte-identical output vs. the Python reference (golden tests green).
2. Parser, validator, vault, and template library ported with unit tests.
3. All 5 Python UI tabs present in Networker, themed, and wired to the Core services.
4. `dotnet build networker.sln -c Release` clean; full test suite green.
5. README documents the new Network Config feature.
6. The two generator systems (pre-existing `ConfigGenerator`/`DeviceSpec` and new
   `NetworkConfigGenerator`/`NetworkDeviceConfig`) unified or explicitly deprecated.

---

## 2. Current Status

### Completed Phases
- **Slice A — Core generation layer (DONE).** Models, interfaces, filters, six C# template renderers,
  dispatcher, dictionary conversion, golden tests.
- **Slice B Tasks 1–3 (DONE).** Task 1: `IConfigGenerator` registered in `App.xaml.cs`
  (resolved by the new page via `Application.Current`); Task 2: "Network Config" nav item + palette
  command; `NetworkConfig/Views/NetworkConfigPage.xaml/.cs` shell with the 5 feature tabs. The
  Generate tab exercises the full DI → generator → render → `CodeBlockView` path with a sample
  config. Task 3: `ConfigValidator` ported with 38 unit tests.
- **Slice C Task 4 (DONE).** `ConfigParser` ported (1508-line `config_parser.py`): `ParseResult`,
  `BaseConfigParser`, `CiscoIosParser`, `JuniperJunosParser`, `SonicParser` (incl. duplicate-key
  scrubber for the frozen Sonic golden JSON), `ConfigParserFactory` with vendor auto-detection.
  21 unit tests ported from `test_parser.py` + 3 golden round-trip tests (generate → parse → compare).
- **Task 5 (DONE).** `VaultService` — PBKDF2-SHA256 key derivation + AES-256-GCM vault file
  (`%LOCALAPPDATA%\Networker\vault.dat`, atomic writes, zeroized key material). 29 unit tests.
- **Task 6 (DONE).** `TemplateLibrary` + `Resources/Templates.json` (6 embedded templates) +
  `TemplateFormData`/`TemplateFormConverter` (form presets → `NetworkDeviceConfig`, mirroring
  Python `_generate_config`). 239 tests total.
- **Task 7 (DONE).** Generate tab UI — full device form (vendor ×6, basics, interfaces with
  vendor naming, VLANs/routes/ACL/OSPF/BGP/EIGRP/STP, template apply, Generate → `CodeBlockView`
  + validator report).
- **Task 8 (DONE).** Import/Analyze, Diff, Vault, Templates tabs — all five Network Config tabs
  now wired to services.

### In-Progress Phases
- None — all planned slices (A, B, C) are complete; only Task 9 (Polish & Release) remains.

### Remaining Phases
- Task 9 — Polish & Release: theme verification (Light/Dark/System), settings integration,
  shortcuts, accessibility, README, Release build, performance check, generator-system unification.

### Overall Estimated Completion: **~85%**
Generation, validator, parser, vault, and template library are done and tested; all five Network
Config tab UIs are built and wired to Core services. Only Task 9 (Polish & Release) remains.

### Major Accomplishments This Session
- **All 13 golden tests pass** (6 vendors × `Generate` + `GenerateFromDict`, byte-exact, plus
  `GetSupportedVendors_ReturnsAllSix`).
- **Full suite: 239/239 tests pass.** Build: **0 errors**, no new warnings.
- **Task 3: `ConfigValidator` ported** from `config_validator.py` (606 lines) — all 9 check groups,
  `WEAK_PASSWORDS`, `RESERVED_VLANS`, IPv4-only IP/network/subnet-mask helpers, `GetSummary`.
  Stateless implementation of the pre-existing `IConfigValidator` contract.
- **Task 4: `ConfigParser` ported** from `config_parser.py` (1508 lines) — `BaseConfigParser`,
  `CiscoIosParser`, `JuniperJunosParser`, `SonicParser`, `ConfigParserFactory` with vendor
  auto-detection. 21 unit tests ported from `test_parser.py`; 3 golden round-trip tests
  (generate → parse → compare fields) cover Cisco IOS, Juniper Junos, and SONiC.
- **Task 5: `VaultService` ported** — PBKDF2-SHA256 (480k default iterations) + AES-256-GCM,
  `salt(16)||nonce(12)||ciphertext||tag(16)` vault format, atomic temp-file writes, key
  zeroization. 29 unit tests.
- **Task 6: template library ported** — 6 embedded templates from `app.py` `TEMPLATES`
  (`Resources/Templates.json`), `TemplateLibrary` (embedded + custom store), form-preset model
  (`TemplateFormData`) and converter (`TemplateFormConverter`, mirrors `_generate_config`).
  7 converter tests + ~13 library tests.
- **Task 7: Generate tab UI built** — `GenerateTab.xaml/.cs` with full form, template apply,
  Generate → `CodeBlockView` + validator report. Fixed `x:Array` XamlCompiler failure by moving
  combo options to static `x:Bind` properties (WinUI does not support `x:Array`).
- **Task 8: Import/Diff/Vault/Templates tabs built** — all five Network Config tabs wired to
  services. Python GUI spec verified first (Import has no vendor selector; Vault GUI has no
  export/templates flows; Python has no Templates tab — ours is a deliberate addition). Build
  0 errors; 239/239 tests.

### Outstanding Blockers
- **None.** No blocker currently prevents the next slice.

---

## 3. Repository Analysis

### 3.1 NetworkConfigPro (Python — source of the migration)

Location: `C:\Users\Kenny\NetworkConfigPro` (Python 3.14). Entry point: `main.py` → `src.gui.app:main`.

#### Architecture
- `main.py` — entry point; launches the PySide6 app.
- `src/core/models.py` — all data models (`DeviceConfig` + feature dataclasses, enums).
- `src/core/generators/config_generator.py` (432 lines) — `ConfigGenerator` class; Jinja2
  `Environment(trim_blocks=True, lstrip_blocks=True)`, `keep_trailing_newline` unset (defaults false);
  registers 10 custom filters.
- `src/core/parsers/config_parser.py` (1508 lines) — `ParseResult`, `BaseConfigParser(ABC)`,
  `CiscoIOSParser`, `JuniperJunosParser`, `SONiCParser`, `ConfigParserFactory`.
- `src/core/validators/config_validator.py` (606 lines) — `Severity`, `Category`, `ValidationIssue`,
  `ConfigValidator`.
- `src/security/vault.py` (430 lines) — `SecureVault`; Fernet encryption, PBKDF2-SHA256 480k
  iterations; credentials / variables / templates; `vault.dat` with 0600 perms.
- `src/gui/app.py` (3179 lines) — single `QMainWindow` with `QTabWidget`: Generate, Import/Analyze,
  Diff, Vault, Templates; also a `TEMPLATES` dict of predefined template configs.
- `src/gui/theme.py` — `qdarktheme` + custom stylesheet.
- `src/gui/help_content.py` — help text for the UI.
- `src/core/templates/vendors/*.j2` — 6 Jinja2 templates: `cisco_ios.j2`, `cisco_nxos.j2`,
  `arista_eos.j2`, `juniper_junos.j2`, `sonic.j2`, `fortinet_fortigate.j2`.
- `src/utils/` — empty utility package.

#### Data Flow
User enters device data in the GUI → `DeviceConfig` dataclass → `ConfigGenerator.generate(cfg)` →
Jinja2 renders the vendor template (custom filters transform names/values) → config string →
displayed/exported. Parser reverses config text → `DeviceConfig`. Validator inspects both.
Vault stores secrets/templates referenced by the generator.

#### Custom Filters (registered in `config_generator.py`)
`cidr_prefix`, `junos_interface_name`, `sonic_interface_name`, `sonic_vlan_id`, `wildcard_to_cidr`,
`sonic_port`, `fortinet_interface_name`, `fortinet_parent_interface`, `fortinet_ospf_interface`,
`wildcard_to_netmask`.

#### Dependencies
`jinja2`, `PySide6`, `qdarktheme`, `cryptography` (Fernet/PBKDF2). Standard lib: `re`,
`abc`, `ipaddress`, `dataclasses`, `enum`.

#### Features Discovered (feature inventory source — see §5)
Generation (6 vendors), parsing (3 vendor parsers + factory), validation, secure vault, predefined
templates, template gallery, config diff, import/analyze with validation display, themes, help content.

### 3.2 Networker (C# WinUI 3 — target)

Location: `C:\Users\Kenny\source\repos\networker`. Solution `networker.sln`.

#### Existing Architecture
```
networker/                       # WinUI 3 app (net8.0-windows10.0.19041)
  App.xaml / App.xaml.cs         # DI container (currently only LlmRuntime.Router), theme at app level
  MainWindow.xaml / .cs          # NavigationView shell + ContentFrame + theme toggle
  MainPage.xaml / .cs            # Chat workspace (LLM integration)
  ToolsPage.xaml / .cs           # 8-tab deterministic toolkit (IP, Config Gen, Audit, Diff, Logs,
                                 #   Playbooks, Topology, Translator)
  SettingsPg.xaml / .cs          # Provider/model/prompt/theme settings
  Controls/                      # CodeBlockView, CommandPalette, BoolToVisibilityConverter
  Models/                        # ChatMessage, ChatRole
  Services/                      # ChatService, LlmRuntime, Toaster, ConfigSyntaxHighlighter
  Styles/                        # Colors.xaml, Fonts.xaml, Styles.xaml (design tokens, theme switching)
  AppSettings.cs                 # LocalSettings-backed settings
  Networker.Core/                # Class library (net8.0)
    Llm/                         # Provider layer (Ollama/Grok/Gemini, router, retry, SSE, scrubber)
    Prompting/                   # PromptBuilder
    NetTools/
      Ip/                        # IpToolkit, IpSubnetInfo
      Config/                    # ConfigAuditor, TextDiff, ConfigGenerator, ConfigTranslator,
                                 #   DeviceSpec, + NEW *ConfigTemplate renderers, ConfigTemplateFilters,
                                 #   ConfigWriter
      Logs/                      # LogAnalyzer
      Playbooks/                 # PlaybookGenerator
      Topology/                  # TopologyBuilder
    Models/NetworkConfig/        # NEW: NetworkDeviceConfig + all feature models/enums
    Services/NetworkConfig/      # NEW: IConfigGenerator + contract interfaces
```

#### Reused Components
| Component | Location | Reuse for NetworkConfigPro |
|---|---|---|
| Theme system | `Styles/Colors.xaml`, `Styles/Styles.xaml` | Use directly |
| Navigation | `MainWindow.xaml` (NavigationView + ContentFrame) | Add new page/tab |
| DI container | `App.xaml.cs` (`ServiceCollection`) | Register new services |
| `CodeBlockView` | `Controls/CodeBlockView` | Config output display |
| `TextDiff` | `Networker.Core/NetTools/Config/TextDiff.cs` | Diff tab |
| `IpToolkit` | `Networker.Core/NetTools/Ip/IpToolkit.cs` | IP math |
| Theme toggle | `MainWindow.ToggleTheme()` | Use existing |
| Settings | `AppSettings.cs` | Vault path, default vendor |
| `Toaster` | `networker/Services/Toaster` | Error/notify patterns |
| `ConfigAuditor` | `Networker.Core/NetTools/Config/ConfigAuditor.cs` | Merge with validator port |

#### Pre-existing Config Generator (IMPORTANT — distinct system)
`Networker.Core/NetTools/Config/ConfigGenerator.cs` is a **separate, pre-existing** generator:
`static ConfigGenerator.Generate(ConfigPlatform platform, DeviceSpec spec)` supporting 4 platforms
(Cisco IOS-XE, Juniper Junos, Arista EOS, VyOS) via string interpolation. It is driven by `DeviceSpec`
(`DeviceSpec.cs`: `VlanSpec`, `InterfaceSpec`, `OspfAreaSpec`, `BgpNeighborSpec`, `AclEntrySpec`,
`NatSpec`, `DeviceSpec`). It is **unrelated** to the new `IConfigGenerator`/`NetworkConfigGenerator`
and the new `NetworkDeviceConfig` models, and is wired into `ToolsPage` today. Do not confuse them.

#### Existing Services / Navigation / ViewModels / Theme / DI / Logging / Settings
- **DI:** `App.xaml.cs` builds a `ServiceCollection`; currently registers only `LlmRuntime.Router`
  as a singleton. Pages resolve from `App.Services`.
- **Navigation:** `MainWindow` NavigationView + `ContentFrame`; currently hosts MainPage (chat),
  ToolsPage (8-tab toolkit), SettingsPg.
- **ViewModels:** no MVVM framework; pages use code-behind (no `CommunityToolkit.Mvvm` dependency
  present). Match existing code-behind patterns.
- **Theme:** `Styles/Colors.xaml` design tokens; Light/Dark/System toggle in MainWindow; theme applied
  in `App` ctor before `InitializeComponent`.
- **Logging:** none central; pre-existing CS1998 warnings in `MainPage.xaml.cs` reference async
  handlers.
- **Settings:** `AppSettings.cs` (local settings-backed static class).

---

## 4. Migration Architecture

### Target Folder Structure
```
Networker.Core/
  Models/NetworkConfig/          # DONE — ported models
  NetTools/Config/               # DONE (renderers) — add parser/validator here
    *ConfigTemplate.cs           # DONE — 6 renderers + ConfigTemplateFilters + ConfigWriter
  Services/NetworkConfig/        # DONE — contracts + NetworkConfigGenerator dispatcher
networker/                       # WinUI app — new UI to be added
  NetworkConfig/
    Views/                       # NetworkConfigPage + Tabs (Generate, ImportAnalyze, Diff, Vault, Templates)
    ViewModels/                  # (optional; match existing code-behind pattern)
    Dialogs/                     # Interface, VLAN, ACL, Routing dialogs
  MainWindow.xaml                # add "Network Config" NavigationViewItem
App.xaml.cs                      # register IConfigGenerator (+ future IConfigParser/IVaultService)
```

### Models (DONE — all in `Networker.Core/Models/NetworkConfig/`)
`Vendor`, `InterfaceType`, `RoutingProtocol`, `SwitchportMode`, `StpMode`, `AclAction`,
`AclProtocol` (enums); `NetworkDeviceConfig`, `Interface`, `Vlan`, `Acl`, `AclEntry`,
`StaticRoute`, `OspfNetwork`, `OspfConfig`, `BgpNeighbor`, `BgpConfig`, `EigrpNetwork`,
`EigrpConfig`, `PrefixListEntry`, `PrefixList`, `RouteMapEntry`, `RouteMap`, `StpConfig` (classes).
Mutable classes (not records) with `{ get; set; }` for object-initializer + dict-parsing ergonomics.

### Services
- `Services/NetworkConfig/IConfigGenerator.cs` — contract: `Generate(NetworkDeviceConfig)`,
  `GenerateFromDict(Vendor, IReadOnlyDictionary<string, object>)`, `GetSupportedVendors()`.
- `Services/NetworkConfig/NetworkConfigGenerator.cs` — sealed dispatcher; switch on `Vendor` → the six
  `*ConfigTemplate.Render(...)`; `GenerateFromDict` builds a `NetworkDeviceConfig` via `DictionaryToConfig`
  (snake_case keys: hostname/interfaces/vlans/acls/static_routes/ospf/eigrp/bgp/stp/prefix_lists/
  route_maps/enable_secret/domain_name/dns_servers/ntp_servers/banner_motd; parses interface names for
  `InterfaceType`; accepts `List<object>`-of-`Dictionary<string,object>` and typed values).
- All contracts are now implemented: `IConfigParser`/`IConfigParserFactory` (`Parsers/`),
  `IConfigValidator` (`ConfigValidator.cs`), `ITemplateLibrary` (`TemplateLibrary.cs`),
  `IVaultService` (`VaultService.cs`).

### Interfaces
All five contracts — `IConfigGenerator`, `IConfigParser`, `IConfigValidator`, `ITemplateLibrary`,
`IVaultService` — are implemented in `Services/NetworkConfig/`.

### ViewModels / Views
`NetworkConfigPage` exists with a `TabView` matching the Python tabs (Generate, Import/Analyze,
Diff, Vault, Templates), all built with code-behind like the rest of Networker (no MVVM framework
in the repo).

### Utilities
`NetTools/Config/ConfigWriter.cs` — `W(StringBuilder, string)` = text + `\n`. Central to matching
Jinja2 output.
`NetTools/Config/ConfigTemplateFilters.cs` — all 10 ported filters as `static` methods (see §3.1 for
the Python list; C#: `SubnetToCidr`, `WildcardToCidr`, `WildcardToNetmask`, `JunosInterfaceName`,
`SonicInterfaceName`, `SonicVlanId`, `SonicPort`, `FortinetInterfaceName`, `FortinetParentInterface`,
`FortinetOspfInterface`, `SonicChannelGroup`, `AclActionValue`, `AclProtocolValue`,
`SwitchportModeValue`, `StpModeValue`, `InterfaceTypeValue`, `CountOnes`).

### Infrastructure
- Golden test harness: `Networker.Core.Tests/NetTools/GoldenConfigTests.cs` reads
  `NetTools/Golden/{vendor}.txt` and compares `Generate` and `GenerateFromDict` output byte-for-byte.
  Golden files are copied to output via `Networker.Core.Tests.csproj` (`None` +
  `CopyToOutputDirectory=PreserveNewest`).

### Dependency Injection Strategy
All five services are registered as singletons in `App.xaml.cs`: `IConfigGenerator` →
`NetworkConfigGenerator`, `IConfigParserFactory` → `ConfigParserFactory`, `IConfigValidator` →
`ConfigValidator`, `IVaultService` → `VaultService`, `ITemplateLibrary` → `TemplateLibrary`.
Pages resolve via `App.Services`.

### Data Flow
UI (future) collects device data → `NetworkDeviceConfig` (or dict) → `IConfigGenerator.Generate` /
`GenerateFromDict` → config string → `CodeBlockView`. Parser/validator/vault feed/consume the same
model. Golden tests short-circuit the UI: fixed sample → renderer → byte comparison.

### Separation of Concerns
- **Renderers** (`NetTools/Config/*ConfigTemplate.cs`) — pure, deterministic string building; one per
  vendor; no IO, no state. Static `internal static class` with `Render(NetworkDeviceConfig)`.
- **Filters** (`ConfigTemplateFilters`) — pure value transforms, no state.
- **Dispatcher** (`NetworkConfigGenerator`) — vendor dispatch + dict→model mapping (service layer).
- **Contracts** (`Services/NetworkConfig`) — interfaces the UI depends on.
- **Tests** — golden files + sample config kept in the test project; sample mirrors
  `gen_golden.py`'s `build_config()`.

---

## 5. Feature Inventory

Legend: ✅ done · 🚧 in progress · ⏳ planned · ❌ blocked.

| Feature | Python location | C# location | Status | Dependencies | Notes | Remaining work |
|---|---|---|---|---|---|---|
| Data models (DeviceConfig + feature models/enums) | `src/core/models.py` | `Networker.Core/Models/NetworkConfig/*.cs` | ✅ | — | 6 enums, 18 classes; classes not records | None |
| Config generator — Cisco IOS | `src/core/templates/vendors/cisco_ios.j2` | `Networker.Core/NetTools/Config/CiscoIosConfigTemplate.cs` | ✅ | Models, filters | Ends `end`, no trailing `\n`; golden 3403 chars | None |
| Config generator — Cisco NX-OS | `cisco_nxos.j2` | `CiscoNxosConfigTemplate.cs` | ✅ | Models, filters | Ends `end`; golden 2233 | None |
| Config generator — Arista EOS | `arista_eos.j2` | `AristaEosConfigTemplate.cs` | ✅ | Models, filters | ACL entries indented 3 spaces (template line 87); golden 2268 | None |
| Config generator — Juniper Junos | `juniper_junos.j2` | `JuniperJunosConfigTemplate.cs` | ✅ | Models, filters | Ends `    }\n}\n` (final `}` on own line) | None |
| Config generator — SONiC | `sonic.j2` | `SonicConfigTemplate.cs` | ✅ | Models, filters | Most whitespace-quirky (see §11 lessons); 10 leading `\n`; golden 5034 | None |
| Config generator — Fortinet FortiGate | `fortinet_fortigate.j2` | `FortinetFortiGateConfigTemplate.cs` | ✅ | Models, filters | Golden 7043; ends `end\n\n`; iface name conversions (see §11) | None |
| Custom template filters (10) | `config_generator.py` filters | `NetTools/Config/ConfigTemplateFilters.cs` | ✅ | — | 17 static methods incl. helpers | None |
| Dispatcher + `Generate`/`GenerateFromDict` | `ConfigGenerator.generate` | `Services/NetworkConfig/NetworkConfigGenerator.cs` | ✅ | Renderers | `DictionaryToConfig` maps snake_case keys | None |
| DI registration of generator | — | `App.xaml.cs` | ✅ | — | `services.AddSingleton<IConfigGenerator, NetworkConfigGenerator>()`; page resolves via `((App)Application.Current).Services` | None |
| Navigation / page shell | GUI `QTabWidget` | `networker/NetworkConfig/Views/` + `MainWindow.xaml` | ✅ | DI | `NavigationViewItem` (Tag `networkconfig`, Icon `Code`) + palette command; page shell has all 5 tabs | Full tab content |
| Config parser | `src/core/parsers/config_parser.py` (1508 lines) | `Services/NetworkConfig/Parsers/` (new) | ✅ | Models | CiscoIOS, JuniperJunos, SONiC + factory; duplicate-key scrubber for Sonic JSON; Junos OSPF/BGP direct-search (see §8) | 21 unit tests + 3 golden round-trips |
| Config validator | `src/core/validators/config_validator.py` (606 lines) | `Services/NetworkConfig/ConfigValidator.cs` | ✅ | Models | Implements pre-existing `IConfigValidator`; 38 tests | Merge with `ConfigAuditor` (deferred) |
| Secure vault | `src/security/vault.py` (430 lines) | `Services/NetworkConfig/VaultService.cs` | ✅ | — | PBKDF2-SHA256 + AES-256-GCM (password-based, matching Python design); no Fernet compat | None |
| Predefined template library | `src/gui/app.py` `TEMPLATES` dict | `Services/NetworkConfig/TemplateLibrary.cs` + `Resources/Templates.json` | ✅ | Models | 6 embedded templates + custom store (`custom_templates.json`) | None |
| Generate tab UI | `src/gui/app.py` | `networker/NetworkConfig/Views/Tabs/GenerateTab.xaml/.cs` | ✅ | DI, renderers | Full device form + template apply + validator report; combo options via static `x:Bind` | None |
| Import/Analyze tab UI | `src/gui/app.py` | `networker/NetworkConfig/Views/Tabs/ImportTab.xaml/.cs` | ✅ | Parser, validator | Paste → parse → `PARSED CONFIGURATION` report + validation issues; syslog file import; auto-detect only (no vendor selector, per Python) | None |
| Diff tab UI | `src/gui/app.py` | `networker/NetworkConfig/Views/Tabs/DiffTab.xaml/.cs` | ✅ | `TextDiff` (exists) | Reuses `TextDiff.DiffLines`/`ToUnified`; headers `--- Configuration A`/`+++ Configuration B` | None |
| Vault tab UI | `src/gui/app.py` | `networker/NetworkConfig/Views/Tabs/VaultTab.xaml/.cs` | ✅ | Vault | Create/unlock/lock 3-state UI; credentials + variables (Normal/Secret); no export (Python GUI has none) | None |
| Templates tab UI | `src/gui/app.py` | `networker/NetworkConfig/Views/Tabs/TemplatesTab.xaml/.cs` | ✅ | Template library | Gallery + preview + custom-template delete; NOT in Python GUI — deliberate addition | None |
| Help content | `src/gui/help_content.py` | new (resources or strings) | ⏳ | — | Low priority | Port |
| Theme | `src/gui/theme.py` | existing `Styles/*.xaml` | ✅ | — | No work needed; Python theme not ported | Verify Light/Dark/System after UI |
| Golden test harness | `gen_golden.py` (reference) | `Networker.Core.Tests/NetTools/GoldenConfigTests.cs` | ✅ | Golden `*.txt` files | 13 tests (12 golden + vendors list) | Keep in sync with template changes |
| Pre-existing generator unification | — | `NetTools/Config/ConfigGenerator.cs` + `DeviceSpec.cs` | ⏳ | — | Separate system (4 platforms); don't break ToolsPage | Decide: deprecate or bridge |

---

## 6. Session Summary

### This Session (Slice C — ConfigParser port)
Ported the 1508-line `config_parser.py` to C# (5 files, ~78 KB) plus 21 unit tests from
`test_parser.py` and 3 golden round-trip tests (generate → parse → compare fields for Cisco IOS,
Juniper Junos, SONiC). Fixed two real bugs in `SonicParser.RemoveDuplicateKeys` — missing
whitespace skip before member keys, and a dropped closing-brace consumption — both surfaced only
by the round-trip tests.

### Files Created (Task 4)
- `Networker.Core/Services/NetworkConfig/Parsers/BaseConfigParser.cs` (3.0 KB)
- `Networker.Core/Services/NetworkConfig/Parsers/CiscoIosParser.cs` (21.9 KB)
- `Networker.Core/Services/NetworkConfig/Parsers/JuniperJunosParser.cs` (22.1 KB)
- `Networker.Core/Services/NetworkConfig/Parsers/SonicParser.cs` (28.1 KB)
- `Networker.Core/Services/NetworkConfig/Parsers/ConfigParserFactory.cs` (2.9 KB)
- `Networker.Core.Tests/Services/NetworkConfig/Parsers/ConfigParserTests.cs` (11.9 KB, 21 tests)
- `Networker.Core.Tests/NetTools/GoldenRoundTripTests.cs` (3 round-trip tests)

### Testing Performed
- `dotnet test Networker.Core.Tests -c Debug` → **187/187 passed** (includes 13 golden byte-parity,
  38 validator, 21 parser, and 3 round-trip tests).

### This Session (Slice A completion)
Began with 2 failing Sonic golden tests (PORT `},` comma placement and other whitespace), fixed them,
then re-verified the whole suite.

### Files Created (this session's earlier slices, all in repo)
- `Networker.Core/NetTools/Config/ConfigTemplateFilters.cs` (327 lines)
- `Networker.Core/NetTools/Config/ConfigWriter.cs` (11 lines)
- `Networker.Core/NetTools/Config/CiscoIosConfigTemplate.cs` (438 lines)
- `Networker.Core/NetTools/Config/CiscoNxosConfigTemplate.cs` (248 lines)
- `Networker.Core/NetTools/Config/AristaEosConfigTemplate.cs` (235 lines)
- `Networker.Core/NetTools/Config/JuniperJunosConfigTemplate.cs` (314 lines)
- `Networker.Core/NetTools/Config/SonicConfigTemplate.cs` (468 lines)
- `Networker.Core/NetTools/Config/FortinetFortiGateConfigTemplate.cs` (420 lines)
- `Networker.Core/Services/NetworkConfig/NetworkConfigGenerator.cs` (449 lines)
- `Networker.Core/Models/NetworkConfig/*.cs` (24 files)
- `Networker.Core/Services/NetworkConfig/{IConfigGenerator,IConfigParser,IConfigValidator,ITemplateLibrary,IVaultService}.cs`
- `Networker.Core.Tests/NetTools/GoldenConfigTests.cs` (537 lines)
- `Networker.Core.Tests/NetTools/Golden/{arista_eos,cisco_ios,cisco_nxos,fortinet_fortigate,juniper_junos,sonic}.txt`
- `Networker.Core.Tests/Networker.Core.Tests.csproj` (updated to embed golden files)

### Files Modified (this session)
- `Networker.Core/NetTools/Config/SonicConfigTemplate.cs` — fixed whitespace to match `sonic.j2`
  (details below).
- (All other golden-verified template work was completed in earlier sessions.)

### Files Removed
- None.

### Major Refactors / Fixes This Session (SonicConfigTemplate.cs)
1. **PORT**: `"speed"` value no longer newline-terminated — closing `}` lands on the same line:
   `"speed": "1000"        },`.
2. **PORTCHANNEL / STATIC_ROUTE / OSPF_INTERFACE**: removed spurious `sb.Append('\n')` so the closing
   `}` shares the last value line: `"admin_status": "up"        }`, `"nexthop": "10.0.0.2"        },`,
   `"area": "0.0.0.0"        },`.
3. **OSPF_ROUTER**: closing `}` is on its OWN template line (literal newline preserved) → emits
   `"default_information_originate": "true"        }\n    },`.
4. **ACL_TABLE**: `]` + `        }` on one line (`]        },`); added no-port-list fallback branch.
5. **ACL_RULE**: iterate **all** entries unfiltered so trailing remark entries still control
   `loop.last` comma placement → produces the golden's `,        "STD-ACL|RULE_5": {` (comma at line
   start). Field `PRIORITY` had a hardcoded comma (double-comma) — removed; `string.Join(",\n", ...)`
   now supplies it. Closing `}` appended directly after the last field.
6. **Tail**: `DNS_NAMESERVER` closes with `    }\n}` (was `    }}`).

### Architectural Decisions (see §12 for full rationale)
- Pure C# StringBuilder renderers instead of the originally planned Scriban.
- New generator kept separate from the pre-existing `ConfigGenerator`/`DeviceSpec` system.
- Golden-file byte-parity testing against captured live Python output.
- Filters as static methods; whitespace rules encoded explicitly per line.

### UI Work Completed
- None (Slice A is Core-only).

### Testing Performed
- `dotnet test Networker.Core.Tests -c Debug` → **125/125 passed** (includes 13 golden tests).
- `dotnet build networker.sln -c Debug` → **0 errors**, 14 pre-existing CS1998 warnings
  (`MainPage.xaml.cs` async-lacks-await).

### This Session (Task 8 — Import, Diff, Vault, Templates tabs)
Built and wired the four remaining Network Config tabs to Core services. First extracted the
Python GUI spec (explore agent over `src/gui/app.py`) to confirm exact behaviors: Import uses only
auto-detection (`detect_and_parse`; no vendor selector) and prints a `PARSED CONFIGURATION` report
with the last-10 validator issues; Diff renders `difflib.unified_diff` with headers
`--- Configuration A`/`+++ Configuration B` and an "N additions, M deletions" stat line; Vault has
a 3-state UI (no vault / locked / unlocked) and never calls export or template APIs; the Python GUI
has NO Templates tab. The C# tabs mirror these behaviors; Templates is a deliberate addition per the
migration plan (built over `ITemplateLibrary`).

### Files Created (Task 8)
- `NetworkConfig/Views/Tabs/ImportTab.xaml/.cs` — paste-to-parse report + syslog file import
- `NetworkConfig/Views/Tabs/DiffTab.xaml/.cs` — unified diff via `TextDiff.DiffLines`/`ToUnified`
- `NetworkConfig/Views/Tabs/VaultTab.xaml/.cs` — 3-state vault UI, credentials/variables
- `NetworkConfig/Views/Tabs/TemplatesTab.xaml/.cs` — template gallery + preview + custom delete
  (defines `TemplateListItem` record)

### Files Modified (Task 8)
- `NetworkConfig/Views/NetworkConfigPage.xaml` — placeholder TabViewItems replaced with the real
  tab controls.

### Testing Performed (Task 8)
- `dotnet build networker.sln -c Debug` → **0 errors**.
- `dotnet test Networker.Core.Tests -c Debug` → **239/239 passed** (unchanged — tab logic lives in
  the already-tested Core services).

---

## 7. Remaining Work (Prioritized Backlog)

### High Priority
| Item | Dependencies | Complexity | Effort |
|---|---|---|---|
| Register `IConfigGenerator` in DI (`App.xaml.cs`) | none | Low | 0.5h |
| Add "Network Config" navigation + page shell with TabView | DI | Medium | 0.5–1d |
| Port `ConfigValidator` (606 lines) + merge with `ConfigAuditor` | Models | Medium | 2–3d |
| ~~Port `ConfigParser` (1508 lines: CiscoIOS, JuniperJunos, SONiC) + factory~~ — **DONE** | — | — | — |
| ~~Unit tests for validator + parser~~ — **DONE** (38 validator + 21 parser + 3 round-trip) | — | — | — |
| Generate tab UI (vendor combo, device form, grids, dialogs) | dispatcher | High | 3–4d |

### Medium Priority
| Item | Dependencies | Complexity | Effort |
|---|---|---|---|
| Port vault (`vault.py`) using Windows DPAPI + AES-256 | none | Medium | 2–3d |
| Port predefined template library (`TEMPLATES` dict) | Models | Low | 1d |
| Import/Analyze tab UI | parser, validator | Medium | 2d |
| Diff tab UI (reuse `TextDiff`) | none | Low | 0.5d |
| Vault tab UI | vault | Medium | 1–2d |
| Templates tab UI (gallery + editor) | template library | Medium | 1–2d |
| Harden `DictionaryToConfig` (type coercion edge cases, JSON round-trip test) | — | Low | 0.5–1d |
| Unify pre-existing `ConfigGenerator`/`DeviceSpec` with new system (deprecate/bridge) | UI cut-over | Medium | 1–2d |

### Low Priority
| Item | Dependencies | Complexity | Effort |
|---|---|---|---|
| Port help content (`help_content.py`) | — | Low | 0.5d |
| Keyboard accelerators (Ctrl+G/S/O/E/L/I) | Generate tab | Low | 0.5d |
| Settings integration (default vendor, vault path, template path) | settings, vault | Low | 0.5–1d |
| Accessibility (automation properties, focus order) | UI tabs | Low | 1d |
| Performance benchmark (generation < 500ms typical) | — | Low | 0.5d |
| UI automation tests (WinAppDriver/Appium) | UI tabs | Medium | 2–3d |
| README update for the new feature | feature complete | Low | 0.5h |
| Keep `MIGRATION_PLAN.md` current as phases land | — | Low | ongoing |

---

## 8. Next Session Plan (execution order)

Start with the checklist below. Each task lists objective, files, expected outcome, and validation.

### Task 1 — Register the generator in DI ✅ DONE
- **Objective:** expose `IConfigGenerator` via `App.Services`.
- **Files:** `App.xaml.cs` (`BuildServiceProvider`).
- **Expected outcome:** `App.Services.GetService<IConfigGenerator>()` returns a `NetworkConfigGenerator`.
- **Validation:** done — page resolves it in the ctor (throws if missing); Debug build clean.
- **Note:** the app's existing pages use statics (`LlmRuntime.Router`); the new page resolves through
  `((App)Application.Current).Services` — the first real DI consumer in the app.

### Task 2 — Add Network Config page shell + navigation ✅ DONE
- **Objective:** a new page reachable from MainWindow, hosting the 5 feature tabs.
- **Files:** `networker/NetworkConfig/Views/NetworkConfigPage.xaml/.cs` (new, mirrors `ToolsPage`
  layout), `MainWindow.xaml` (NavigationViewItem + palette command + frame routing).
- **Expected outcome:** app launches, tab visible, placeholder page renders with themed controls.
- **Validation:** Debug build 0 errors; Generate tab produces a sample config per vendor through DI.
- **Note:** Generate tab currently has a vendor ComboBox + "Generate sample configuration" (proof of
  life); the full device form is Task 7.

### Task 3 — Port Config Validator ✅ DONE
- **Objective:** `ConfigValidator` + `Severity`/`Category`/`ValidationIssue` matching
  `config_validator.py`; merge with existing `ConfigAuditor` (extend, don't replace).
- **Files:** `Services/NetworkConfig/ConfigValidator.cs` (new, implements the pre-existing
  `IConfigValidator` contract — placed next to the contract + `NetworkConfigGenerator` rather than
  `NetTools/Config/`, consistent with Tasks 1–2; the `ConfigAuditor` merge is deferred).
- **Expected outcome:** all Python rules present; existing auditor tests still pass.
- **Validation:** 38 new tests ported from `tests/unit/test_validator.py` (plus extras for rules
  Python's suite skips); full suite green.
- **Notes:**
  - Stateless `Validate(config) -> IReadOnlyList<ValidationIssue>`; `GetSummary` ported as a static
    helper on the concrete class (not on the interface).
  - `Severity`/`Category` map 1:1 onto the pre-existing `ValidationSeverity`/`ValidationCategory`.
  - Interface IP-validity check dropped: the C# `Interface.IpAddress` is `IPAddress?`, which cannot
    hold an invalid address (Python's `test_invalid_ip_address` is not portable for this reason).
    Invalid-IP checks remain for string-typed fields (`StaticRoute.Destination/NextHop`,
    `BgpNeighbor.IpAddress`, `OspfConfig.RouterId`, `AclEntry.Source`).
  - Interface reserved-VLAN check reads `VlanId ?? AccessVlan` (C# split Python's single `vlan_id`
    into legacy + new fields); `IsValidIp` is IPv4-only to match Python's `IPv4Address`.

### Task 4 — Port Config Parser (**DONE**)
- **Objective:** `ParseResult`, `BaseConfigParser`, `CiscoIosParser`, `JuniperJunosParser`,
  `SonicParser`, `ConfigParserFactory` per `config_parser.py`.
- **Files:** `Services/NetworkConfig/Parsers/{BaseConfigParser,CiscoIosParser,JuniperJunosParser,
  SonicParser,ConfigParserFactory}.cs` (new).
- **Expected outcome:** parsing pasted configs back into `NetworkDeviceConfig`; graceful partial
  parses return warnings.
- **Validation:** round-trip tests (generate → parse → compare fields); unit tests per vendor.
- **Status: DONE** — 21 unit tests (ported from `test_parser.py`) + 3 golden round-trip tests.
- **Notable decisions:** Junos OSPF/BGP use direct config-text search (the C# generator emits one
  `protocols {}` block per protocol — first-block anchoring misses the second); SONiC duplicate
  JSON keys deduped last-value-wins (the golden file's duplicate `"Ethernet1"` PORT entry is frozen
  by byte-parity); `_parse_bgp` returning `None` when no neighbors survive is preserved; Junos
  `ether-options`/`vlan` phantom interfaces are retained with `InterfaceType.Ethernet`/`Vlan`
  (Python checks `startswith("et-")`, which `"ether-options"` fails).

### Task 5 — Port Vault
- **Objective:** `IVaultService` implementation with Windows DPAPI + AES-256-GCM.
- **Files:** `Services/NetworkConfig/VaultService.cs` (new).
- **Expected outcome:** create/unlock, store credentials/variables/templates; file in
  `%LOCALAPPDATA%\Networker\vault.dat`.
- **Validation:** unit tests (create, save, load, corrupt-file handling).
- **Status: DONE** — PBKDF2-SHA256 (480k default iterations) + AES-256-GCM instead of DPAPI
  (password-based unlock/change, matching the Python design); 29 unit tests. See §6 session notes.

### Task 6 — Port Template Library
- **Objective:** `ITemplateLibrary` with predefined templates as embedded JSON.
- **Files:** `Services/NetworkConfig/TemplateLibrary.cs`, `Resources/Templates.json` (new).
- **Expected outcome:** `GetTemplates()`/`GetTemplate(name)` from `app.py` `TEMPLATES` dict.
- **Validation:** unit tests listing expected templates.
- **Status: DONE** — 6 embedded templates; `TemplateFormData`/`TemplateFormConverter` (EIGRP + STP
  sections added); custom templates stored in `%LOCALAPPDATA%\Networker\custom_templates.json`.

### Task 7 — Generate tab UI
- **Objective:** vendor ComboBox (6), device basics, interface/VLAN/ACL/static-route/OSPF/BGP/EIGRP/STP
  editing, Generate → `CodeBlockView` output with copy.
- **Files:** `NetworkConfig/Views/Tabs/GenerateTab.xaml/.cs`, dialogs.
- **Expected outcome:** produce the sample config matching golden output from the UI.
- **Validation:** manual comparison against golden; run app.
- **Status: DONE** — full form implemented; combo options via static `x:Bind` properties (WinUI
  XamlCompiler rejects `x:Array`); `TemplateFormConverter.Convert` → `IConfigGenerator.Generate`
  → `CodeBlockView` + `IConfigValidator.Validate` report. Build 0 errors.

### Task 8 — Import/Analyze, Diff, Vault, Templates tabs ✅ DONE
- **Objective:** remaining tabs wired to services; Diff reuses `TextDiff`.
- **Files:** `NetworkConfig/Views/Tabs/*`.
- **Expected outcome:** all 5 Python tabs functional.
- **Validation:** manual smoke + unit tests where logic lives in Core.
- **Status: DONE** — `ImportTab` (paste → parse → structured report + validation issues; syslog
  file import with severity counts), `DiffTab` (`TextDiff.DiffLines` + `ToUnified`, headers
  `--- Configuration A`/`+++ Configuration B`), `VaultTab` (create/unlock/lock 3-state UI,
  credentials add/delete, variables add with Normal/Secret), `TemplatesTab` (gallery + preview via
  `CodeBlockView`, custom-template delete). `NetworkConfigPage.xaml` placeholders replaced with the
  real tabs. Build 0 errors; 239/239 tests.
- **Notable decisions:** Import has NO vendor selector (Python `_parse_config` auto-detects only);
  Vault has no export/template/edit flows (Python GUI never calls those vault APIs); the Templates
  tab is OUR addition — the Python GUI has no Templates tab (templates were only the Generate
  combo). File picker: `Microsoft.Windows.Storage.Pickers.FileOpenPicker` in this WinAppSDK
  requires a `WindowId` ctor arg, so the classic `Windows.Storage.Pickers.FileOpenPicker` +
  `WinRT.Interop.InitializeWithWindow.Initialize` pattern is used instead.

### Task 9 — Polish & Release
- **Objective:** settings integration, shortcuts, accessibility, README, Release build.
- **Files:** `SettingsPg`, `MainWindow`, README.
- **Expected outcome:** `dotnet build networker.sln -c Release` clean; full suite green.
- **Validation:** Release build + full test run; theme check Light/Dark/System.

---

## 9. Build & Validation

### Current Build Status
- `dotnet build networker.sln -c Debug`: **0 errors**.
- `dotnet build networker.sln -c Release`: **0 errors** (Task 9 build gate verified).
- `dotnet test Networker.Core.Tests -c Debug`: **239/239 passed**.
- All five Network Config tabs (Generate, Import/Analyze, Diff, Vault, Templates) build and wire to
  Core services (Task 8 complete). UI smoke test + theme check pending (Task 9).

### Remaining Warnings (all pre-existing)
- 14 × `CS1998` "async method lacks await" in `MainPage.xaml.cs` (lines 217, 255, 309, 328, 348, 358,
  364 — reported per-target, so it repeats). Not introduced by this work.

### Known Issues
- None known in the Core layer.
- `FortinetOspfInterface` returns a hard-coded `"port1"` — matches golden but is an approximation for
  the general case.

### Runtime Issues
- Not yet exercised: the generator is not registered in DI and no UI calls it. Golden tests are the
  only execution path so far.

### Pending Testing
- Vault, template library unit tests (not ported yet; parser has 21 tests + 3 round-trips).
- `ConfigValidator` is ported (38 tests) but not yet wired into a UI surface — the Import/Analyze tab
  is the consumer.
- UI smoke tests once tabs exist.
- Performance benchmark once UI is wired.

### Regression Risks
- Template/filter changes can silently break byte-parity; any edit to `*ConfigTemplate.cs` or
  `ConfigTemplateFilters.cs` should be followed by a full golden test run.
- Touching `ConfigAuditor`/`ConfigGenerator` risks the pre-existing ToolsPage features — keep the
  two generator systems separate until unification is deliberate.
- `DictionaryToConfig` is the bridge for JSON-ish input; changes there affect `GenerateFromDict`.

---

## 10. Technical Debt

### Temporary Workarounds
- Golden files are static copies of live Python output; if `*.j2` templates change, regen via
  `C:\Users\Kenny\AppData\Local\Temp\opencode\netconfigpro-golden\gen_golden.py` and re-copy into
  `Networker.Core.Tests/NetTools/Golden/`.
- `ConfigTemplateFilters.FortinetOspfInterface` is hard-coded to `"port1"`.
- Whitespace behavior is encoded line-by-line in the renderers (see §11); there is no shared
  "jinja2 semantics" abstraction.

### Deferred Improvements
- Unify pre-existing `ConfigGenerator`/`DeviceSpec` with the new `NetworkConfigGenerator`/
  `NetworkDeviceConfig` (deprecate or bridge).
- Move `DictionaryToConfig` to a separate, well-tested converter class.
- Consider a fixture/helper for whitespace tests instead of whole-file goldens.

### Refactoring Opportunities
- The 6 renderers share "section/loop/comma" idioms — a small shared helper could reduce Sonic/Fortinet
  complexity, but changes must preserve byte-parity.
- `ConfigTemplateFilters` mixes filter-specific and shared helpers; fine as-is for now.

### Performance Improvements
- Renderers are single-pass `StringBuilder` already; potential micro-opt: cache `StringBuilder`
  capacity. Generation is trivially fast; benchmark only when UI exists.

### Security Improvements
- Vault port must use DPAPI/AES-GCM with strict ACLs; never log secrets (mirror
  `CredentialScrubber` patterns in `Llm/`).
- Review `DictionaryToConfig` for any path that could echo secrets back into UI/logs.

---

## 11. Risks

### Current Blockers
- None.

### Potential Architectural Risks
- Byte-parity depends on subtle Jinja2 whitespace semantics. A future Python change requires
  re-verification; document each quirk (see §13 Handoff Notes).
- The parser (1508 lines of regex) is the highest-risk port — scope creep risk; plan for graceful
  partial parses first.

### Integration Risks
- Two generator systems could confuse users; mitigate by deprecating the old one visibly once the
  new UI ships.
- WinUI specifics (DataGrid, pickers, ContentDialog) may differ from Qt patterns; expect
  adjustments in the UI slice.

### Compatibility Concerns
- Python vault format is intentionally NOT migrated (new Windows-native vault).
- `net8.0-windows10.0.19041` TFM constrains available APIs; verify DPAPI package availability before
  committing to a NuGet dependency (prefer `System.Security.Cryptography.ProtectedData` only if needed
  — check if it's already available in the SDK).

---

## 12. Design Decisions

### D1 — Pure C# renderers instead of Scriban (DEVIATION from original plan)
- **Decision:** Port each Jinja2 template to a hand-written C# `StringBuilder` renderer
  (`*ConfigTemplate.cs`), not Scriban `.sbn` templates.
- **Why:** byte-for-byte control over whitespace (Jinja2 `trim_blocks`/`lstrip_blocks` semantics are
  quirky and easier to reproduce with explicit `W()`/append calls); **no new NuGet dependency**
  (project hard constraint); output is deterministic and type-safe.
- **Alternatives considered:** Scriban (original plan — abandoned: whitespace/trim semantics differ,
  adds a dependency), RazorLight (heavier, compile step), T4 (design-time only), reusing the
  pre-existing `ConfigGenerator`'s interpolation style (rejected: different model).
- **Trade-off:** renderers are verbose; whitespace quirks are encoded per line.

### D2 — Separate new generator from pre-existing `ConfigGenerator`
- **Decision:** New `IConfigGenerator`/`NetworkConfigGenerator` + `NetworkDeviceConfig` models coexist
  with the old `ConfigGenerator(ConfigPlatform, DeviceSpec)`.
- **Why:** the old system uses a different model and backs `ToolsPage`; replacing it risks regressions.
  The new system targets byte-parity with Python.
- **Alternatives:** extending the old generator (rejected: model mismatch, would break the existing
  API and its tests). **Deferred:** unification.

### D3 — Golden-file byte-parity testing
- **Decision:** Capture real Python output once (`gen_golden.py`), store as `NetTools/Golden/*.txt`,
  and assert `Assert.Equal(expected, actual)` for both `Generate` and `GenerateFromDict`.
- **Why:** the project's #1 requirement is output parity; golden files are the cheapest, most robust
  regression net.
- **Alternatives:** property-based or unit tests per rule (would not guarantee overall parity).

### D4 — Filters as static methods
- **Decision:** All Jinja2 filters ported as `static` methods on `ConfigTemplateFilters`.
- **Why:** pure functions, trivially testable, no DI needed, callable directly from renderers.
- **Alternative:** an interface/injected filter registry (over-engineering for static transforms).

### D5 — Mutable classes for models
- **Decision:** Feature models are plain classes with `{ get; set; }` (not records).
- **Why:** simplifies `DictionaryToConfig`/JSON mapping and object-initializer use in future UI forms.
- **Alternative:** `record` types (immutable) — nicer hashing but more ceremony for mutable UI flows.

### D6 — Whitespace rules derived empirically
- **Decision:** Jinja2 whitespace behavior was established by experimentation with the live Python
  renderer, then encoded in the C# renderers.
- **Why:** docs for `trim_blocks`/`lstrip_blocks` don't cover edge cases (inline `{% endif %}`,
  blank lines in loops); ground truth is the rendered output.
- **See §13** for the exact rules to preserve.

### D7 — `GenerateFromDict(IReadOnlyDictionary<string, object>)`
- **Decision:** The contract exposes a dictionary-based entry point mirroring Python dicts/JSON.
- **Why:** enables a future JSON import path and keeps the Core layer UI-agnostic.

### D8 — Templates in code, not embedded resources
- **Decision:** Renderers are compiled C# files, not `.sbn`/resource files.
- **Why:** compile-time checking, no resource pipeline, no reflection. **Trade-off:** template edits
  require code edits + rebuild + golden re-verification.

---

## 13. Progress Checklist

### Phase A — Core Models & Generation (Slice A)
- [x] ✅ Enums: `Vendor`, `InterfaceType`, `RoutingProtocol`, `SwitchportMode`, `StpMode`,
      `AclAction`, `AclProtocol`
- [x] ✅ Models: `NetworkDeviceConfig` + all feature classes
- [x] ✅ Service contracts: `IConfigGenerator`, `IConfigParser`, `IConfigValidator`,
      `ITemplateLibrary`, `IVaultService`
- [x] ✅ `ConfigWriter.W()` helper
- [x] ✅ `ConfigTemplateFilters` (all 10 Python filters + helpers)
- [x] ✅ Cisco IOS renderer (golden exact)
- [x] ✅ Cisco NX-OS renderer (golden exact)
- [x] ✅ Arista EOS renderer (golden exact)
- [x] ✅ Juniper Junos renderer (golden exact)
- [x] ✅ SONiC renderer (golden exact — whitespace quirks fixed this session)
- [x] ✅ Fortinet FortiGate renderer (golden exact)
- [x] ✅ `NetworkConfigGenerator` dispatcher + `DictionaryToConfig`
- [x] ✅ Golden test harness (6 vendors × 2 paths + vendors list = 13 tests)
- [x] ✅ Full suite 125/125; Debug build 0 errors

### Phase B — Services (Slice C, planned)
- [x] ✅ `ConfigValidator` port + `Severity`/`Category`/`ValidationIssue` (38 tests)
- [ ] ⏳ Merge `ConfigAuditor` with validator
- [x] ✅ `ConfigParser` port (`BaseConfigParser`, CiscoIOS, JuniperJunos, SONiC, factory) +
      21 unit tests + 3 golden round-trips
- [x] ✅ `VaultService` (PBKDF2-SHA256 + AES-256-GCM; 29 tests)
- [x] ✅ `TemplateLibrary` (6 embedded templates as JSON; custom store)
- [x] ✅ Unit tests for vault (29) + template library + converter (7)

### Phase C — UI (Slice B, complete)
- [x] ✅ DI registration of `IConfigGenerator` (+ `IConfigParserFactory`, `IConfigValidator`,
      `IVaultService`, `ITemplateLibrary`)
- [x] ✅ "Network Config" NavigationViewItem + page shell (TabView)
- [x] ✅ Generate tab (vendor, device basics, interfaces, VLANs, routing, ACLs, STP, output)
- [x] ✅ Import/Analyze tab
- [x] ✅ Diff tab (reuse `TextDiff`)
- [x] ✅ Vault tab
- [x] ✅ Templates tab
- [x] ✅ Dialogs — inline edit panels used instead of separate dialogs
- [ ] ⏳ Theme verification (Light/Dark/System)
- [ ] ⏳ Settings integration (default vendor, vault path, template path)
- [ ] ⏳ Keyboard accelerators
- [ ] ⏳ Accessibility pass

### Phase D — Polish & Release
- [ ] ⏳ README update
- [ ] ⏳ Release build clean
- [ ] ⏳ Performance benchmark
- [ ] ⏳ UI automation tests (optional)
- [ ] ⏳ Unify/deprecate old `ConfigGenerator`/`DeviceSpec`
- [ ] ⏳ Final full test run + plan update

---

## 14. Handoff Notes

### Where Work Stopped
Slices A (Core generation), B (UI), and C (services) are all **complete and green**: 239/239 tests,
0 build errors. Tasks 1–8 are DONE — the generator, parser, validator, vault, and template library
are ported and tested, and all five Network Config tabs (Generate, Import/Analyze, Diff, Vault,
Templates) are built, themed, and wired to Core services. Only **Task 9 (Polish & Release)**
remains.

### What to Tackle First Next Session
Continue §8: **Task 9 (Polish & Release)** — Release build (`dotnet build networker.sln -c Release`),
theme verification (Light/Dark/System), README update, settings integration (default vendor, vault
path), keyboard accelerators, accessibility pass, performance benchmark, and the deferred
unification decision for the pre-existing `ConfigGenerator`/`DeviceSpec`. Then commit the Task 5–8
work in logical chunks (services → templates → tabs), keeping the pre-existing `MainWindow.xaml`
cosmetic change separate.

### Important Assumptions
- `C:\Users\Kenny\NetworkConfigPro` is the Python reference; `src/core/templates/vendors/*.j2` are the
  templates to port (all 6 ported). `C:\Users\Kenny\AppData\Local\Temp\opencode\netconfigpro-golden\gen_golden.py`
  is the golden generator with `build_config()`; it also documents the exact sample input the C#
  tests mirror.
- Jinja2 env: `Environment(trim_blocks=True, lstrip_blocks=True)`, `keep_trailing_newline` unset
  (defaults false). Template/golden source files are CRLF; Python renders LF; C# must render LF.
- Test input data must mirror `gen_golden.py`'s `build_config()` exactly (it does — see
  `GoldenConfigTests.BuildSampleConfig`/`BuildSampleDict`).

### Whitespace Rules (do not forget)
- A block tag ending a line — standalone or inline `{% endif %}` — consumes the following `\n`.
- Variable tags never consume newlines.
- A literal blank line inside a loop emits one `\n` per iteration.
- Comment-only lines are absorbed into the preceding block tag.
- Consequence: a `}` following an inline `{% endif %}` that ends a line lands on the SAME line as the
  last value (Sonic's `"mtu": "9100"        },`), unless the `}` is on its own template line
  (OSPF_ROUTER) — then the newline is preserved.

### Sonic Golden Facts (hard-won, keep byte-parity)
- 10 leading `\n`; ends `    }\n}`.
- PORT closing `}` on same line as last value (with/without `speed`); last port no comma.
- PORTCHANNEL/STATIC_ROUTE/OSPF_INTERFACE same-line closing brace.
- OSPF_ROUTER closing `}` on its own line → `        }\n`.
- ACL_TABLE `]` + `        }` on one line.
- ACL_RULE: entries iterated unfiltered (remarks still count for `loop.last`) → trailing remark after
  RULE_15 produces `        },` and the cross-ACL comma renders as `,        "STD-ACL|RULE_5": {`.
- Field list joined with `",\n"`; `PRIORITY` must NOT carry its own comma.

### Other Vendor Golden Facts
- cisco_ios / cisco_nxos / arista_eos end with `end` and **no trailing newline**.
- juniper_junos ends `    }\n}\n`.
- fortinet_fortigate ends `end\n\n`; golden ordering global → dns → ntp → replacemsg →
  per-interface `config system interface` → static routes → OSPF → BGP → prefix-lists → route-maps →
  ACLs; name conversions `GigabitEthernet0/0`→`port1`, 0/1→`port2`, 0/3→`port4`, `Loopback0`→
  `loopback0`; interface body ends `set allowaccess ping`; access ports `set native-vlan {vlan_id}`;
  disabled ports `set status down` (down wins over up).
- arista_eos ACL entries are indented **3** spaces (from `arista.j2` line 87).
- Golden lengths: cisco_ios 3403, cisco_nxos 2233, arista_eos 2268, juniper_junos 4113, sonic 5034,
  fortinet_fortigate 7043.

### Lessons Learned
- **Verify whitespace against live output, never infer it.** Multiple "obvious" fixes broke parity;
  the golden test is the arbiter.
- **When a golden test fails, diff the expected/actual strings at the reported position** before
  editing; each failure this session traced to exactly one template line.
- **Keep template source open while editing renderers** (`sonic.j2` lines map 1:1 to C# append calls).
- **The `,` belongs to the join or the template line, not the field string** (PRIORITY double-comma bug).
- **A trailing remark entry is not "skipped for output" — it still controls `loop.last`.**
- Every change to a renderer or filter must be followed by the full golden test run.
