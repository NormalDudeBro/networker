# OpenAI Codex (ChatGPT OAuth)

Networker integrates **OpenAI Codex** through the official bundled `codex-app-server` helper (Apache-2.0). Sign-in uses your existing **ChatGPT** subscription. Networker never requires, reads, or falls back to `OPENAI_API_KEY`.

## What ships

- Pinned official Windows x64 package: `codex-app-server-package-x86_64-pc-windows-msvc.tar.gz`
- Layout under `Codex/` next to `networker.exe`:
  - `bin/codex-app-server.exe`
  - `bin/codex-code-mode-host.exe`
  - `codex-package.json`
  - `codex-path/rg.exe`
  - `codex-resources/*` (command runner + Windows sandbox setup)
- Credentials: official helper + Windows **keyring** only (`cli_auth_credentials_store="keyring"`). Plaintext `auth.json` is rejected.
- Dedicated home: `%LOCALAPPDATA%\Networker\Codex`

## User flow

1. Settings → provider **codex** → **Sign in with ChatGPT**
2. System browser opens; app-server owns OAuth callback and token storage
3. Select model and reasoning effort from account-aware `model/list`
4. Assist uses a persistent app-server thread (no WebView)
5. Questions, global file changes, and commands share one default conversation. Tool actions are auto-approved and run as the current Windows user with `danger-full-access`.

## Packaging

```powershell
./scripts/Get-CodexAppServer.ps1 -OutputDirectory artifacts\codex\win-x64
```

CI/release runs this before publish. `New-NetworkerPackage.ps1` copies the verified package into each app slot. Upstream OpenAI binaries are **not** re-signed as Networker-owned.

## Limits / non-goals (v1)

- No API-key auth UI or silent key fallback
- No OpenCode / Node / Bun / npm dependency
- No VS Code client impersonation (`--session-source networker`)
- No separate workspace picker or Chat/Agent mode. Codex's native Windows workspace sandbox is not used because it can deadlock during setup on supported Windows configurations.
- No remote plugins or MCP in v1
- Public redistribution still requires OpenAI terms / client-identification disposition

## Attribution

OpenAI Codex components are copyright OpenAI and licensed under Apache License 2.0. Networker is not affiliated with or endorsed by OpenAI.
