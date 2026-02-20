using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using ClaudeMon.Helpers;
using ClaudeMon.Models;

namespace ClaudeMon.Services;

/// <summary>
/// Reads the OAuth access token passively from .credentials.json (never refreshes
/// or modifies it) and calls the usage endpoint. If the token is expired, falls
/// back gracefully to null so the caller can use local estimates instead.
/// </summary>
public sealed class LiveUsageService : IDisposable
{
    private const string UsageEndpoint = "https://api.anthropic.com/api/oauth/usage";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _credentialsPath;
    private readonly HttpClient _httpClient;
    private string? _cachedAccessToken;
    private long _tokenExpiresAt;

    public LiveUsageService(string claudePath)
    {
        _credentialsPath = Path.Combine(
            Environment.ExpandEnvironmentVariables(claudePath), ".credentials.json");
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task<LiveUsageResponse?> GetUsageAsync()
    {
        var token = await GetTokenAsync();
        if (string.IsNullOrEmpty(token))
            return null;

        using var request = new HttpRequestMessage(HttpMethod.Get, UsageEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("anthropic-beta", "oauth-2025-04-20");
        request.Headers.UserAgent.ParseAdd("ClaudeMon/1.0");

        var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                // Token expired on the server side - clear cache so next call
                // re-reads from disk (Claude Code may have refreshed it by then).
                _cachedAccessToken = null;
            }
            System.Diagnostics.Debug.WriteLine(
                $"Usage API returned {response.StatusCode}");
            return null;
        }

        var json = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync<LiveUsageResponse>(json, JsonOptions);
    }

    private async Task<string?> GetTokenAsync()
    {
        // Use cached token if still valid
        if (!string.IsNullOrEmpty(_cachedAccessToken) && !IsTokenExpired())
            return _cachedAccessToken;

        // Read whatever token Claude Code has written to disk
        var creds = await JsonFileReader.ReadAsync<OAuthCredentialsFile>(_credentialsPath);
        var oauth = creds?.ClaudeAiOAuth;
        if (oauth == null || string.IsNullOrEmpty(oauth.AccessToken))
            return null;

        _tokenExpiresAt = oauth.ExpiresAt;

        if (IsTokenExpired())
        {
            System.Diagnostics.Debug.WriteLine(
                "OAuth token expired - waiting for Claude Code to refresh it");
            return null;
        }

        _cachedAccessToken = oauth.AccessToken;
        return _cachedAccessToken;
    }

    private bool IsTokenExpired()
    {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return nowMs >= _tokenExpiresAt - 60_000; // 1 minute buffer
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
