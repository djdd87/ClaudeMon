# ClaudeMon

A Windows system tray application that monitors your [Claude Code](https://docs.anthropic.com/en/docs/claude-code) usage in real time.

![Build](https://github.com/djdd87/ClaudeMon/actions/workflows/ci.yml/badge.svg)
![Coverage](https://img.shields.io/endpoint?url=https://gist.githubusercontent.com/djdd87/GIST_ID_HERE/raw/claudemon-coverage.json)
![License](https://img.shields.io/badge/license-MIT-blue)
![Platform](https://img.shields.io/badge/platform-Windows-lightgrey)
![.NET](https://img.shields.io/badge/.NET-10-purple)

## Features

- **System tray icons** — one per Claude profile, showing your current usage percentage with color-coded status (green/amber/red)
- **Live usage data** — pulls real-time 5-hour session and 7-day weekly utilization from the Anthropic API
- **Multi-profile support** — auto-discovers all `~/.claude*` profiles or configure them explicitly
- **Dashboard popup** — click the tray icon to see:
  - Circular usage gauge
  - Today's messages, output tokens, and sessions
  - Weekly token usage
  - 7-day activity chart
  - Model breakdown (tokens per model)
  - Estimated cost and time saved from speculative execution
- **Near-instant updates** — file system watcher detects changes as they happen, with a poll timer as fallback

## How It Works

ClaudeMon reads data from three sources:

1. **JSONL conversation files** (`~/.claude*/projects/**/*.jsonl`) — scanned directly for current-day stats like messages, tokens, and sessions
2. **stats-cache.json** — Claude Code's periodic aggregate, used for lifetime totals and cost estimates
3. **Anthropic API** (`/api/oauth/usage`) — the authoritative source for rate-limit utilization percentages and reset times

The app passively reads your existing Claude Code OAuth token for API access — it never writes credentials or refreshes tokens.

## Requirements

- Windows 10/11
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Claude Code](https://docs.anthropic.com/en/docs/claude-code) installed with at least one `~/.claude*` profile

## Getting Started

```bash
git clone https://github.com/djdd87/ClaudeMon.git
cd ClaudeMon
dotnet run --project src/ClaudeMon/ClaudeMon.csproj
```

The app will auto-discover your Claude profiles and appear in the system tray.

## Configuration

Edit `src/ClaudeMon/appsettings.json`:

```json
{
  "ClaudeMon": {
    "RefreshIntervalSeconds": 60,
    "Profiles": [],
    "PlanLimits": {
      "default_claude_max_5x": 450000,
      "default_claude_max_20x": 1800000,
      "pro": 100000
    }
  }
}
```

| Setting | Description |
|---------|-------------|
| `RefreshIntervalSeconds` | How often to poll for changes (default: 60s). File watcher provides faster updates between polls. |
| `Profiles` | Leave empty for auto-discovery, or set explicitly: `[{"Name": "Work", "Path": "C:\\Users\\you\\.claude-work"}]` |
| `PlanLimits` | Maps Claude plan tier IDs to weekly output token limits. Used for estimated usage percentage when the live API is unavailable. |

## Supported Plans

ClaudeMon recognizes these Claude plan tiers:

| Plan | Tier ID |
|------|---------|
| Pro | `pro`, `default_claude_ai` |
| Max 5x | `default_claude_max_5x` |
| Max 20x | `default_claude_max_20x` |
| Standard | `default_raven` |
| Team variants | Above tiers with `team` subscription type |

## Building

```bash
dotnet build src/ClaudeMon/ClaudeMon.csproj
```

To publish a self-contained executable:

```bash
dotnet publish src/ClaudeMon/ClaudeMon.csproj -c Release -r win-x64 --self-contained
```

## License

[MIT](LICENSE)
