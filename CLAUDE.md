# InsaneLimits — Procon v2 Plugin

## Project Overview

InsaneLimits is a Procon v2 plugin that provides a powerful limit/rule engine for Battlefield servers. Administrators can write custom C# expressions that are compiled at runtime to enforce server rules based on kills, deaths, stats, weapons, and more.

- **Language:** C#
- **License:** GPLv3
- **Supported games:** BF3, BF4, BFHL
- **Original author:** micovery
- **Maintainer:** Prophet731
- **Dependencies:** Procon v2 (runtime only)
- **Special:** Contains a runtime C# compiler for user-defined limit code

## Architecture

| File | Responsibility |
|------|---------------|
| `src/InsaneLimits.cs` | Main entry point, plugin metadata, lifecycle, core state, interface declarations |
| `src/InsaneLimits/Description.cs` | HTML plugin description (const string) |
| `src/InsaneLimits/Settings.cs` | Plugin variables UI and persistence |
| `src/InsaneLimits/Events.cs` | Procon event handlers |
| `src/InsaneLimits/LimitCompiler.cs` | Runtime C# compilation of user-defined limit code |
| `src/InsaneLimits/LimitExecutor.cs` | Limit evaluation and execution |
| `src/InsaneLimits/Models.cs` | Data model classes, DataDictionary, limit definitions, player/server/kill info interfaces |
| `src/InsaneLimits/Actions.cs` | Limit action execution (kill, kick, say, log, etc.) |

## Code Style

See the master `CLAUDE.md` at the procon_plugins root for shared conventions.

## Build & CI

- `InsaneLimits.csproj` at root is a **CI-only artifact** for `dotnet format`.
- **CI workflow**: `dotnet format` checks on push/PR to master.
- **Release workflow**: tag-triggered release packaging.

## Threading Model

Uses ThreadPool for async limit evaluation. Limits are compiled once and cached as delegates.

## Event Registrations

```
OnPlayerKilled, OnPlayerSpawned, OnPlayerJoin, OnPlayerLeft,
OnGlobalChat, OnTeamChat, OnSquadChat,
OnServerInfo, OnListPlayers, OnRoundOver, OnLevelLoaded,
OnPlayerTeamChange, OnPlayerSquadChange
```

## Security Notes

- **Runtime compiler**: User-defined C# code is compiled and executed at runtime. Only server administrators can define limits via the plugin settings UI.
- The compiler restricts available namespaces to prevent filesystem/network access from user code.

## Supported Games

- BF3, BF4, BFHL

## Branch Structure

- `master` — current development, Procon v2 only
- `legacy` — archived pre-refactor code
