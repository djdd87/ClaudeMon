using System.IO;
using System.Text.Json;
using ClaudeMon.Helpers;
using ClaudeMon.Models;

namespace ClaudeMon.Services;

/// <summary>
/// Reads and deserializes all Claude Code data files from the local ~/.claude/ directory.
/// </summary>
public sealed class ClaudeDataService
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
    public async Task<Dictionary<DateTime, (int Messages, long OutputTokens, int Sessions, Dictionary<string, long> TokensByModel)>> GetRecentJsonlStatsAsync(int days = 7)
    {
        var result = new Dictionary<DateTime, (int Messages, long OutputTokens, int Sessions, Dictionary<string, long> TokensByModel)>();
        var projectsDir = Path.Combine(_claudePath, "projects");
        if (!Directory.Exists(projectsDir))
            return result;

        var cutoffUtc = DateTime.UtcNow.Date.AddDays(-(days - 1));

        foreach (var file in Directory.EnumerateFiles(projectsDir, "*.jsonl", SearchOption.AllDirectories))
        {
            // Skip subagent conversation files - they contain agent task prompts
            // and tool results, not human-authored messages.
            if (file.Contains(Path.DirectorySeparatorChar + "subagents" + Path.DirectorySeparatorChar))
                continue;

            if (File.GetLastWriteTimeUtc(file).Date < cutoffUtc)
                continue;

            // Track which dates this file contributed a human message on so we can
            // increment the session count once per file per day.
            var datesWithActivity = new HashSet<DateTime>();

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
                            // Only count entries where the user authored text, not tool results.
                            // Tool results are also sent as role=user in the API, so every tool
                            // call generates a "type":"user" JSONL entry - we must skip those.
                            if (IsHumanMessage(root))
                            {
                                result[date] = (existing.Messages + 1, existing.OutputTokens, existing.Sessions, existing.TokensByModel ?? new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase));
                                datesWithActivity.Add(date);
                            }
                            break;

                        case "assistant":
                            if (root.TryGetProperty("message", out var msgEl))
                            {
                                if (msgEl.TryGetProperty("usage", out var usageEl))
                                {
                                    var outputTokens = GetLong(usageEl, "output_tokens");
                                    var modelTokens = existing.TokensByModel ?? new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

                                    if (msgEl.TryGetProperty("model", out var modelProp))
                                    {
                                        var modelName = modelProp.GetString() ?? "unknown";
                                        modelTokens[modelName] = modelTokens.GetValueOrDefault(modelName) + outputTokens;
                                    }

                                    result[date] = (existing.Messages, existing.OutputTokens + outputTokens, existing.Sessions, modelTokens);
                                }
                            }
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GetRecentJsonlStats] {file}: {ex.Message}");
            }

            // Count this file as one session for each day it had human messages.
            foreach (var date in datesWithActivity)
            {
                result.TryGetValue(date, out var existing);
                result[date] = (existing.Messages, existing.OutputTokens, existing.Sessions + 1, existing.TokensByModel ?? new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase));
            }
        }

        return result;

        static long GetLong(JsonElement el, string prop) =>
            el.TryGetProperty(prop, out var v) && v.TryGetInt64(out var n) ? n : 0;

        // Returns true only for entries that contain human-authored text.
        // Three kinds of type="user" entries must be excluded:
        //   1. Tool results  — array content with tool_result type blocks
        //   2. System injections — string content starting with '<', used by Claude Code
        //      to inject slash command output, caveats, stderr, etc. into the conversation
        //   3. Subagent prompts — excluded at the file level (subagents/ directories)
        static bool IsHumanMessage(JsonElement root)
        {
            if (!root.TryGetProperty("message", out var msg)) return false;
            if (!msg.TryGetProperty("content", out var content)) return false;

            if (content.ValueKind == JsonValueKind.String)
            {
                // System-injected messages always start with an XML-like tag (e.g.
                // <local-command-caveat>, <command-name>, <local-command-stdout>).
                // Real human messages never start with '<'.
                var s = content.GetString();
                return s != null && !s.StartsWith('<');
            }

            // Array content: human message only if it has a text block (not just tool results).
            if (content.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in content.EnumerateArray())
                {
                    if (item.TryGetProperty("type", out var t) &&
                        t.GetString() == "text")
                        return true;
                }
            }

            return false;
        }
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
