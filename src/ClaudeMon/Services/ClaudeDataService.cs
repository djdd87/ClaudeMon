using System.IO;
using System.Text.Json;
using ClaudeMon.Helpers;
using ClaudeMon.Models;

namespace ClaudeMon.Services;

/// <summary>
/// Reads and deserializes all Claude Code data files from the local ~/.claude/ directory.
/// </summary>
public class ClaudeDataService
{
    private readonly string _claudePath;

    public ClaudeDataService(string claudePath)
    {
        _claudePath = Environment.ExpandEnvironmentVariables(claudePath);
    }

    /// <summary>
    /// Reads the aggregated usage statistics from stats-cache.json.
    /// </summary>
    public async Task<StatsCache?> GetStatsCacheAsync()
        => await JsonFileReader.ReadAsync<StatsCache>(Path.Combine(_claudePath, "stats-cache.json"));

    /// <summary>
    /// Reads subscription/tier metadata from .credentials.json.
    /// Returns the nested OAuth info (never stores tokens).
    /// </summary>
    public async Task<ClaudeAiOAuthInfo?> GetCredentialsAsync()
    {
        var file = await JsonFileReader.ReadAsync<CredentialsInfo>(
            Path.Combine(_claudePath, ".credentials.json"));
        return file?.ClaudeAiOAuth;
    }

    /// <summary>
    /// Reads Claude Code configuration flags from .claude.json.
    /// </summary>
    public async Task<ClaudeConfig?> GetConfigAsync()
        => await JsonFileReader.ReadAsync<ClaudeConfig>(Path.Combine(_claudePath, ".claude.json"));

    /// <summary>
    /// Scans JSONL conversation files under the projects/ subdirectory for the last
    /// <paramref name="days"/> UTC days and returns per-day message and output-token counts.
    /// stats-cache.json is only updated periodically by Claude Code, so this provides
    /// live figures for any days that are stale or missing from the cache.
    /// Only files modified within the window are read to keep the scan fast.
    /// </summary>
    public async Task<Dictionary<DateTime, (int Messages, long OutputTokens)>> GetRecentJsonlStatsAsync(int days = 7)
    {
        var result = new Dictionary<DateTime, (int Messages, long OutputTokens)>();
        var projectsDir = Path.Combine(_claudePath, "projects");
        if (!Directory.Exists(projectsDir))
            return result;

        var cutoffUtc = DateTime.UtcNow.Date.AddDays(-(days - 1));

        foreach (var file in Directory.EnumerateFiles(projectsDir, "*.jsonl", SearchOption.AllDirectories))
        {
            if (File.GetLastWriteTimeUtc(file).Date < cutoffUtc)
                continue;

            try
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);

                string? line;
                while ((line = await reader.ReadLineAsync()) is not null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    if (!root.TryGetProperty("timestamp", out var tsProp)) continue;
                    if (!DateTime.TryParse(tsProp.GetString(), null,
                            System.Globalization.DateTimeStyles.RoundtripKind, out var ts)) continue;

                    var date = ts.ToUniversalTime().Date;
                    if (date < cutoffUtc) continue;

                    if (!root.TryGetProperty("type", out var typeProp)) continue;

                    result.TryGetValue(date, out var existing);

                    switch (typeProp.GetString())
                    {
                        case "user":
                            result[date] = (existing.Messages + 1, existing.OutputTokens);
                            break;

                        case "assistant":
                            if (root.TryGetProperty("message", out var msgEl) &&
                                msgEl.TryGetProperty("usage", out var usageEl))
                            {
                                result[date] = (existing.Messages, existing.OutputTokens + GetLong(usageEl, "output_tokens"));
                            }
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GetRecentJsonlStats] {file}: {ex.Message}");
            }
        }

        return result;

        static long GetLong(JsonElement el, string prop) =>
            el.TryGetProperty(prop, out var v) && v.TryGetInt64(out var n) ? n : 0;
    }

    /// <summary>
    /// Reads all session-meta JSON files from the last N days.
    /// Files live under ~/.claude/usage-data/session-meta/*.json.
    /// </summary>
    public async Task<List<SessionMeta>> GetRecentSessionsAsync(int days = 7)
    {
        var dir = Path.Combine(_claudePath, "usage-data", "session-meta");
        var result = new List<SessionMeta>();

        if (!Directory.Exists(dir))
            return result;

        var cutoff = DateTime.UtcNow.AddDays(-days);

        foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
        {
            var session = await JsonFileReader.ReadAsync<SessionMeta>(file);
            if (session is null)
                continue;

            // StartTime is stored as an ISO-8601 string; parse to compare with cutoff.
            if (DateTime.TryParse(session.StartTime, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var startTime)
                && startTime >= cutoff)
            {
                result.Add(session);
            }
        }

        return result;
    }
}
