using System.Text.Json;
using ClaudeMon.Models;
using ClaudeMon.Services;

namespace ClaudeMon.Tests.Services;

/// <summary>
/// Comprehensive tests for ClaudeDataService.
/// Tests JSONL parsing (core business logic), file reading, and data aggregation.
/// </summary>
public sealed class ClaudeDataServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ClaudeDataService _service;

    public ClaudeDataServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ClaudeMon-Tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
        _service = new ClaudeDataService(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }
    }

    #region GetRecentJsonlStatsAsync Tests

    [Fact]
    public async Task GetRecentJsonlStatsAsync_EmptyProjectsDir_ReturnsEmptyDict()
    {
        // Arrange
        var projectsDir = Path.Combine(_tempDir, "projects");
        Directory.CreateDirectory(projectsDir);

        // Act
        var result = await _service.GetRecentJsonlStatsAsync(days: 7);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetRecentJsonlStatsAsync_NoProjectsDir_ReturnsEmptyDict()
    {
        // Arrange - do not create projects directory

        // Act
        var result = await _service.GetRecentJsonlStatsAsync(days: 7);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetRecentJsonlStatsAsync_HumanTextMessage_CountsAsMessage()
    {
        // Arrange
        var today = DateTime.UtcNow;
        var projectDir = Path.Combine(_tempDir, "projects", "test-project");
        Directory.CreateDirectory(projectDir);

        var jsonlFile = Path.Combine(projectDir, "conversation.jsonl");
        var line = JsonSerializer.Serialize(new
        {
            type = "user",
            timestamp = today.ToString("O"),
            message = new
            {
                content = "Hello, world!"
            }
        });
        File.WriteAllText(jsonlFile, line);

        // Act
        var result = await _service.GetRecentJsonlStatsAsync(days: 7);

        // Assert
        var dateKey = today.Date;
        Assert.True(result.ContainsKey(dateKey));
        Assert.Equal(1, result[dateKey].Messages);
    }

    [Fact]
    public async Task GetRecentJsonlStatsAsync_SystemInjection_NotCounted()
    {
        // Arrange
        var today = DateTime.UtcNow;
        var projectDir = Path.Combine(_tempDir, "projects", "test-project");
        Directory.CreateDirectory(projectDir);

        var jsonlFile = Path.Combine(projectDir, "conversation.jsonl");
        var line = JsonSerializer.Serialize(new
        {
            type = "user",
            timestamp = today.ToString("O"),
            message = new
            {
                content = "<local-command-stdout>some output</local-command-stdout>"
            }
        });
        File.WriteAllText(jsonlFile, line);

        // Act
        var result = await _service.GetRecentJsonlStatsAsync(days: 7);

        // Assert
        var dateKey = today.Date;
        if (result.ContainsKey(dateKey))
        {
            Assert.Equal(0, result[dateKey].Messages);
        }
        else
        {
            Assert.Empty(result);
        }
    }

    [Fact]
    public async Task GetRecentJsonlStatsAsync_ToolResultArray_NotCountedAsHumanMessage()
    {
        // Arrange
        var today = DateTime.UtcNow;
        var projectDir = Path.Combine(_tempDir, "projects", "test-project");
        Directory.CreateDirectory(projectDir);

        var jsonlFile = Path.Combine(projectDir, "conversation.jsonl");
        var line = JsonSerializer.Serialize(new
        {
            type = "user",
            timestamp = today.ToString("O"),
            message = new
            {
                content = new object[]
                {
                    new { type = "tool_result", content = "tool output" }
                }
            }
        });
        File.WriteAllText(jsonlFile, line);

        // Act
        var result = await _service.GetRecentJsonlStatsAsync(days: 7);

        // Assert
        var dateKey = today.Date;
        if (result.ContainsKey(dateKey))
        {
            Assert.Equal(0, result[dateKey].Messages);
        }
        else
        {
            Assert.Empty(result);
        }
    }

    [Fact]
    public async Task GetRecentJsonlStatsAsync_ArrayContentWithTextType_CountedAsHumanMessage()
    {
        // Arrange
        var today = DateTime.UtcNow;
        var projectDir = Path.Combine(_tempDir, "projects", "test-project");
        Directory.CreateDirectory(projectDir);

        var jsonlFile = Path.Combine(projectDir, "conversation.jsonl");
        var line = JsonSerializer.Serialize(new
        {
            type = "user",
            timestamp = today.ToString("O"),
            message = new
            {
                content = new object[]
                {
                    new { type = "text", text = "Hello with attachment" },
                    new { type = "document", url = "file://doc.txt" }
                }
            }
        });
        File.WriteAllText(jsonlFile, line);

        // Act
        var result = await _service.GetRecentJsonlStatsAsync(days: 7);

        // Assert
        var dateKey = today.Date;
        Assert.True(result.ContainsKey(dateKey));
        Assert.Equal(1, result[dateKey].Messages);
    }

    [Fact]
    public async Task GetRecentJsonlStatsAsync_AssistantMessageWithOutputTokens_AccumulatesTokens()
    {
        // Arrange
        var today = DateTime.UtcNow;
        var projectDir = Path.Combine(_tempDir, "projects", "test-project");
        Directory.CreateDirectory(projectDir);

        var jsonlFile = Path.Combine(projectDir, "conversation.jsonl");
        var lines = new[]
        {
            JsonSerializer.Serialize(new
            {
                type = "user",
                timestamp = today.ToString("O"),
                message = new { content = "Hello" }
            }),
            JsonSerializer.Serialize(new
            {
                type = "assistant",
                timestamp = today.AddHours(1).ToString("O"),
                message = new
                {
                    model = "claude-3-5-sonnet-20241022",
                    usage = new { output_tokens = 150L }
                }
            })
        };
        File.WriteAllLines(jsonlFile, lines);

        // Act
        var result = await _service.GetRecentJsonlStatsAsync(days: 7);

        // Assert
        var dateKey = today.Date;
        Assert.True(result.ContainsKey(dateKey));
        Assert.Equal(150, result[dateKey].OutputTokens);
    }

    [Fact]
    public async Task GetRecentJsonlStatsAsync_MultipleModels_TracksTokensPerModel()
    {
        // Arrange
        var today = DateTime.UtcNow;
        var projectDir = Path.Combine(_tempDir, "projects", "test-project");
        Directory.CreateDirectory(projectDir);

        var jsonlFile = Path.Combine(projectDir, "conversation.jsonl");
        var lines = new[]
        {
            JsonSerializer.Serialize(new
            {
                type = "assistant",
                timestamp = today.ToString("O"),
                message = new
                {
                    model = "claude-3-5-sonnet-20241022",
                    usage = new { output_tokens = 100L }
                }
            }),
            JsonSerializer.Serialize(new
            {
                type = "assistant",
                timestamp = today.AddHours(1).ToString("O"),
                message = new
                {
                    model = "claude-3-5-sonnet-20241022",
                    usage = new { output_tokens = 50L }
                }
            }),
            JsonSerializer.Serialize(new
            {
                type = "assistant",
                timestamp = today.AddHours(2).ToString("O"),
                message = new
                {
                    model = "claude-3-7-long-context-20250219",
                    usage = new { output_tokens = 200L }
                }
            })
        };
        File.WriteAllLines(jsonlFile, lines);

        // Act
        var result = await _service.GetRecentJsonlStatsAsync(days: 7);

        // Assert
        var dateKey = today.Date;
        Assert.True(result.ContainsKey(dateKey));
        Assert.Equal(350, result[dateKey].OutputTokens);
        Assert.Equal(150, result[dateKey].TokensByModel["claude-3-5-sonnet-20241022"]);
        Assert.Equal(200, result[dateKey].TokensByModel["claude-3-7-long-context-20250219"]);
    }

    [Fact]
    public async Task GetRecentJsonlStatsAsync_SessionCount_OnePerFilePerDay()
    {
        // Arrange
        var today = DateTime.UtcNow;
        var projectDir = Path.Combine(_tempDir, "projects", "test-project");
        Directory.CreateDirectory(projectDir);

        var jsonlFile = Path.Combine(projectDir, "conversation.jsonl");
        var lines = new[]
        {
            JsonSerializer.Serialize(new
            {
                type = "user",
                timestamp = today.ToString("O"),
                message = new { content = "First message" }
            }),
            JsonSerializer.Serialize(new
            {
                type = "user",
                timestamp = today.AddHours(2).ToString("O"),
                message = new { content = "Second message" }
            })
        };
        File.WriteAllLines(jsonlFile, lines);

        // Act
        var result = await _service.GetRecentJsonlStatsAsync(days: 7);

        // Assert
        var dateKey = today.Date;
        Assert.True(result.ContainsKey(dateKey));
        Assert.Equal(2, result[dateKey].Messages);
        Assert.Equal(1, result[dateKey].Sessions); // One session per file per day
    }

    [Fact]
    public async Task GetRecentJsonlStatsAsync_SessionCount_MultipleDays_OnePerFilePerDay()
    {
        // Arrange
        var today = DateTime.UtcNow;
        var yesterday = today.AddDays(-1);
        var projectDir = Path.Combine(_tempDir, "projects", "test-project");
        Directory.CreateDirectory(projectDir);

        var jsonlFile = Path.Combine(projectDir, "conversation.jsonl");
        var lines = new[]
        {
            JsonSerializer.Serialize(new
            {
                type = "user",
                timestamp = yesterday.ToString("O"),
                message = new { content = "Yesterday message" }
            }),
            JsonSerializer.Serialize(new
            {
                type = "user",
                timestamp = today.ToString("O"),
                message = new { content = "Today message" }
            })
        };
        File.WriteAllLines(jsonlFile, lines);

        // Act
        var result = await _service.GetRecentJsonlStatsAsync(days: 7);

        // Assert
        Assert.Equal(1, result[yesterday.Date].Sessions);
        Assert.Equal(1, result[today.Date].Sessions);
    }

    [Fact]
    public async Task GetRecentJsonlStatsAsync_SubagentsDirectory_Skipped()
    {
        // Arrange
        var today = DateTime.UtcNow;
        var subagentsDir = Path.Combine(_tempDir, "projects", "test-project", "subagents");
        Directory.CreateDirectory(subagentsDir);

        var jsonlFile = Path.Combine(subagentsDir, "subagent-conv.jsonl");
        var line = JsonSerializer.Serialize(new
        {
            type = "user",
            timestamp = today.ToString("O"),
            message = new { content = "Should not be counted" }
        });
        File.WriteAllText(jsonlFile, line);

        // Act
        var result = await _service.GetRecentJsonlStatsAsync(days: 7);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetRecentJsonlStatsAsync_OldFiles_Skipped()
    {
        // Arrange
        var cutoffDate = DateTime.UtcNow.Date.AddDays(-6); // 7-day window
        var oldDate = cutoffDate.AddDays(-1);

        var projectDir = Path.Combine(_tempDir, "projects", "test-project");
        Directory.CreateDirectory(projectDir);

        var jsonlFile = Path.Combine(projectDir, "old.jsonl");
        var line = JsonSerializer.Serialize(new
        {
            type = "user",
            timestamp = oldDate.ToString("O"),
            message = new { content = "Old message" }
        });
        File.WriteAllText(jsonlFile, line);

        // Set file modification time to old date
        File.SetLastWriteTimeUtc(jsonlFile, oldDate);

        // Act
        var result = await _service.GetRecentJsonlStatsAsync(days: 7);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetRecentJsonlStatsAsync_MultipleDaysInOneFile_CorrectPerDayBreakdown()
    {
        // Arrange
        var today = DateTime.UtcNow;
        var yesterday = today.AddDays(-1);
        var projectDir = Path.Combine(_tempDir, "projects", "test-project");
        Directory.CreateDirectory(projectDir);

        var jsonlFile = Path.Combine(projectDir, "conversation.jsonl");
        var lines = new[]
        {
            JsonSerializer.Serialize(new
            {
                type = "user",
                timestamp = yesterday.ToString("O"),
                message = new { content = "Yesterday: msg 1" }
            }),
            JsonSerializer.Serialize(new
            {
                type = "user",
                timestamp = yesterday.AddHours(6).ToString("O"),
                message = new { content = "Yesterday: msg 2" }
            }),
            JsonSerializer.Serialize(new
            {
                type = "user",
                timestamp = today.ToString("O"),
                message = new { content = "Today: msg 1" }
            })
        };
        File.WriteAllLines(jsonlFile, lines);

        // Act
        var result = await _service.GetRecentJsonlStatsAsync(days: 7);

        // Assert
        Assert.Equal(2, result[yesterday.Date].Messages);
        Assert.Equal(1, result[yesterday.Date].Sessions);
        Assert.Equal(1, result[today.Date].Messages);
        Assert.Equal(1, result[today.Date].Sessions);
    }

    [Fact]
    public async Task GetRecentJsonlStatsAsync_BlankLines_Skipped()
    {
        // Arrange
        var today = DateTime.UtcNow;
        var projectDir = Path.Combine(_tempDir, "projects", "test-project");
        Directory.CreateDirectory(projectDir);

        var jsonlFile = Path.Combine(projectDir, "conversation.jsonl");
        var content = $@"
{JsonSerializer.Serialize(new { type = "user", timestamp = today.ToString("O"), message = new { content = "Hello" } })}


{JsonSerializer.Serialize(new { type = "user", timestamp = today.AddHours(1).ToString("O"), message = new { content = "World" } })}

";
        File.WriteAllText(jsonlFile, content);

        // Act
        var result = await _service.GetRecentJsonlStatsAsync(days: 7);

        // Assert
        var dateKey = today.Date;
        Assert.True(result.ContainsKey(dateKey));
        Assert.Equal(2, result[dateKey].Messages);
    }

    [Fact]
    public async Task GetRecentJsonlStatsAsync_MissingTimestamp_Skipped()
    {
        // Arrange
        var today = DateTime.UtcNow;
        var projectDir = Path.Combine(_tempDir, "projects", "test-project");
        Directory.CreateDirectory(projectDir);

        var jsonlFile = Path.Combine(projectDir, "conversation.jsonl");
        var lines = new[]
        {
            JsonSerializer.Serialize(new
            {
                type = "user",
                message = new { content = "No timestamp" }
            }),
            JsonSerializer.Serialize(new
            {
                type = "user",
                timestamp = today.ToString("O"),
                message = new { content = "Valid" }
            })
        };
        File.WriteAllLines(jsonlFile, lines);

        // Act
        var result = await _service.GetRecentJsonlStatsAsync(days: 7);

        // Assert
        var dateKey = today.Date;
        Assert.True(result.ContainsKey(dateKey));
        Assert.Equal(1, result[dateKey].Messages);
    }

    [Fact]
    public async Task GetRecentJsonlStatsAsync_InvalidTimestamp_Skipped()
    {
        // Arrange
        var today = DateTime.UtcNow;
        var projectDir = Path.Combine(_tempDir, "projects", "test-project");
        Directory.CreateDirectory(projectDir);

        var jsonlFile = Path.Combine(projectDir, "conversation.jsonl");
        var lines = new[]
        {
            JsonSerializer.Serialize(new
            {
                type = "user",
                timestamp = "not-a-date",
                message = new { content = "Bad timestamp" }
            }),
            JsonSerializer.Serialize(new
            {
                type = "user",
                timestamp = today.ToString("O"),
                message = new { content = "Valid" }
            })
        };
        File.WriteAllLines(jsonlFile, lines);

        // Act
        var result = await _service.GetRecentJsonlStatsAsync(days: 7);

        // Assert
        var dateKey = today.Date;
        Assert.True(result.ContainsKey(dateKey));
        Assert.Equal(1, result[dateKey].Messages);
    }

    [Fact]
    public async Task GetRecentJsonlStatsAsync_MissingType_Skipped()
    {
        // Arrange
        var today = DateTime.UtcNow;
        var projectDir = Path.Combine(_tempDir, "projects", "test-project");
        Directory.CreateDirectory(projectDir);

        var jsonlFile = Path.Combine(projectDir, "conversation.jsonl");
        var lines = new[]
        {
            JsonSerializer.Serialize(new
            {
                timestamp = today.ToString("O"),
                message = new { content = "No type" }
            }),
            JsonSerializer.Serialize(new
            {
                type = "user",
                timestamp = today.AddHours(1).ToString("O"),
                message = new { content = "Valid" }
            })
        };
        File.WriteAllLines(jsonlFile, lines);

        // Act
        var result = await _service.GetRecentJsonlStatsAsync(days: 7);

        // Assert
        var dateKey = today.Date;
        Assert.True(result.ContainsKey(dateKey));
        Assert.Equal(1, result[dateKey].Messages);
    }

    [Fact]
    public async Task GetRecentJsonlStatsAsync_AssistantMessageWithoutUsage_Ignored()
    {
        // Arrange
        var today = DateTime.UtcNow;
        var projectDir = Path.Combine(_tempDir, "projects", "test-project");
        Directory.CreateDirectory(projectDir);

        var jsonlFile = Path.Combine(projectDir, "conversation.jsonl");
        var lines = new[]
        {
            JsonSerializer.Serialize(new
            {
                type = "assistant",
                timestamp = today.ToString("O"),
                message = new { model = "claude-3-5-sonnet-20241022" }
            })
        };
        File.WriteAllLines(jsonlFile, lines);

        // Act
        var result = await _service.GetRecentJsonlStatsAsync(days: 7);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetRecentJsonlStatsAsync_AssistantMessageWithoutModel_TracksTokensButNoModelKey()
    {
        // Arrange
        var today = DateTime.UtcNow;
        var projectDir = Path.Combine(_tempDir, "projects", "test-project");
        Directory.CreateDirectory(projectDir);

        var jsonlFile = Path.Combine(projectDir, "conversation.jsonl");
        var lines = new[]
        {
            JsonSerializer.Serialize(new
            {
                type = "assistant",
                timestamp = today.ToString("O"),
                message = new
                {
                    usage = new { output_tokens = 100L }
                }
            })
        };
        File.WriteAllLines(jsonlFile, lines);

        // Act
        var result = await _service.GetRecentJsonlStatsAsync(days: 7);

        // Assert
        var dateKey = today.Date;
        Assert.True(result.ContainsKey(dateKey));
        // Output tokens are tracked
        Assert.Equal(100, result[dateKey].OutputTokens);
        // But model tokens dict stays empty since no model property exists
        Assert.Empty(result[dateKey].TokensByModel);
    }

    [Fact]
    public async Task GetRecentJsonlStatsAsync_ZeroOutputTokens_Counted()
    {
        // Arrange
        var today = DateTime.UtcNow;
        var projectDir = Path.Combine(_tempDir, "projects", "test-project");
        Directory.CreateDirectory(projectDir);

        var jsonlFile = Path.Combine(projectDir, "conversation.jsonl");
        var lines = new[]
        {
            JsonSerializer.Serialize(new
            {
                type = "assistant",
                timestamp = today.ToString("O"),
                message = new
                {
                    model = "claude-3-5-sonnet-20241022",
                    usage = new { output_tokens = 0L }
                }
            })
        };
        File.WriteAllLines(jsonlFile, lines);

        // Act
        var result = await _service.GetRecentJsonlStatsAsync(days: 7);

        // Assert
        var dateKey = today.Date;
        Assert.True(result.ContainsKey(dateKey));
        Assert.Equal(0, result[dateKey].OutputTokens);
    }

    [Fact]
    public async Task GetRecentJsonlStatsAsync_MultipleFiles_AggregatesCorrectly()
    {
        // Arrange
        var today = DateTime.UtcNow;
        var projectDir1 = Path.Combine(_tempDir, "projects", "project1");
        var projectDir2 = Path.Combine(_tempDir, "projects", "project2");
        Directory.CreateDirectory(projectDir1);
        Directory.CreateDirectory(projectDir2);

        var file1 = Path.Combine(projectDir1, "conv.jsonl");
        var file2 = Path.Combine(projectDir2, "conv.jsonl");

        File.WriteAllLines(file1, new[]
        {
            JsonSerializer.Serialize(new
            {
                type = "user",
                timestamp = today.ToString("O"),
                message = new { content = "File 1 msg" }
            })
        });

        File.WriteAllLines(file2, new[]
        {
            JsonSerializer.Serialize(new
            {
                type = "user",
                timestamp = today.ToString("O"),
                message = new { content = "File 2 msg" }
            })
        });

        // Act
        var result = await _service.GetRecentJsonlStatsAsync(days: 7);

        // Assert
        var dateKey = today.Date;
        Assert.True(result.ContainsKey(dateKey));
        Assert.Equal(2, result[dateKey].Messages);
        Assert.Equal(2, result[dateKey].Sessions); // One session per file per day
    }

    [Fact]
    public async Task GetRecentJsonlStatsAsync_CustomDayWindow_RespectsCutoff()
    {
        // Arrange
        var today = DateTime.UtcNow;
        var eightDaysAgo = today.AddDays(-8);
        var sixDaysAgo = today.AddDays(-6);

        var projectDir = Path.Combine(_tempDir, "projects", "test-project");
        Directory.CreateDirectory(projectDir);

        var jsonlFile = Path.Combine(projectDir, "conversation.jsonl");
        var lines = new[]
        {
            JsonSerializer.Serialize(new
            {
                type = "user",
                timestamp = eightDaysAgo.ToString("O"),
                message = new { content = "Eight days ago (outside window)" }
            }),
            JsonSerializer.Serialize(new
            {
                type = "user",
                timestamp = sixDaysAgo.ToString("O"),
                message = new { content = "Six days ago (inside window)" }
            })
        };
        File.WriteAllLines(jsonlFile, lines);

        // Set file modification time to recent (so it won't be filtered by last write time)
        File.SetLastWriteTimeUtc(jsonlFile, today);

        // Act - request only 7 days
        var result = await _service.GetRecentJsonlStatsAsync(days: 7);

        // Assert - should not include eight days ago, should include six days ago
        Assert.DoesNotContain(eightDaysAgo.Date, result.Keys);
        Assert.Contains(sixDaysAgo.Date, result.Keys);
    }

    [Fact]
    public async Task GetRecentJsonlStatsAsync_HumanMessageMissingContent_NotCounted()
    {
        // Arrange
        var today = DateTime.UtcNow;
        var projectDir = Path.Combine(_tempDir, "projects", "test-project");
        Directory.CreateDirectory(projectDir);

        var jsonlFile = Path.Combine(projectDir, "conversation.jsonl");
        var lines = new[]
        {
            JsonSerializer.Serialize(new
            {
                type = "user",
                timestamp = today.ToString("O"),
                message = new { }
            }),
            JsonSerializer.Serialize(new
            {
                type = "user",
                timestamp = today.AddHours(1).ToString("O"),
                message = new { content = "Valid message" }
            })
        };
        File.WriteAllLines(jsonlFile, lines);

        // Act
        var result = await _service.GetRecentJsonlStatsAsync(days: 7);

        // Assert
        var dateKey = today.Date;
        Assert.True(result.ContainsKey(dateKey));
        Assert.Equal(1, result[dateKey].Messages);
    }

    [Fact]
    public async Task GetRecentJsonlStatsAsync_TokensByModelCaseInsensitive()
    {
        // Arrange
        var today = DateTime.UtcNow;
        var projectDir = Path.Combine(_tempDir, "projects", "test-project");
        Directory.CreateDirectory(projectDir);

        var jsonlFile = Path.Combine(projectDir, "conversation.jsonl");
        var lines = new[]
        {
            JsonSerializer.Serialize(new
            {
                type = "assistant",
                timestamp = today.ToString("O"),
                message = new
                {
                    model = "Claude-3-5-Sonnet",
                    usage = new { output_tokens = 100L }
                }
            }),
            JsonSerializer.Serialize(new
            {
                type = "assistant",
                timestamp = today.AddHours(1).ToString("O"),
                message = new
                {
                    model = "claude-3-5-sonnet",
                    usage = new { output_tokens = 50L }
                }
            })
        };
        File.WriteAllLines(jsonlFile, lines);

        // Act
        var result = await _service.GetRecentJsonlStatsAsync(days: 7);

        // Assert
        var dateKey = today.Date;
        Assert.True(result.ContainsKey(dateKey));
        // Models are stored with their original casing but dictionary is case-insensitive
        Assert.Equal(150, result[dateKey].OutputTokens);
    }

    [Fact]
    public async Task GetRecentJsonlStatsAsync_UserMessageMissingMessage_NotCounted()
    {
        // Arrange
        var today = DateTime.UtcNow;
        var projectDir = Path.Combine(_tempDir, "projects", "test-project");
        Directory.CreateDirectory(projectDir);

        var jsonlFile = Path.Combine(projectDir, "conversation.jsonl");
        var lines = new[]
        {
            JsonSerializer.Serialize(new
            {
                type = "user",
                timestamp = today.ToString("O")
            }),
            JsonSerializer.Serialize(new
            {
                type = "user",
                timestamp = today.AddHours(1).ToString("O"),
                message = new { content = "Valid" }
            })
        };
        File.WriteAllLines(jsonlFile, lines);

        // Act
        var result = await _service.GetRecentJsonlStatsAsync(days: 7);

        // Assert
        var dateKey = today.Date;
        Assert.True(result.ContainsKey(dateKey));
        Assert.Equal(1, result[dateKey].Messages);
    }

    #endregion

    #region GetStatsCacheAsync Tests

    [Fact]
    public async Task GetStatsCacheAsync_ValidFile_DeserializesCorrectly()
    {
        // Arrange
        var stats = new StatsCache
        {
            Version = 2,
            LastComputedDate = DateTime.UtcNow.ToString("O"),
            TotalSessions = 42,
            TotalMessages = 1000,
            TotalSpeculationTimeSavedMs = 5000
        };
        var json = JsonSerializer.Serialize(stats);
        File.WriteAllText(Path.Combine(_tempDir, "stats-cache.json"), json);

        // Act
        var result = await _service.GetStatsCacheAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Version);
        Assert.Equal(42, result.TotalSessions);
        Assert.Equal(1000, result.TotalMessages);
    }

    [Fact]
    public async Task GetStatsCacheAsync_MissingFile_ReturnsNull()
    {
        // Act
        var result = await _service.GetStatsCacheAsync();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetStatsCacheAsync_InvalidJson_ReturnsNull()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_tempDir, "stats-cache.json"), "{ invalid json");

        // Act
        var result = await _service.GetStatsCacheAsync();

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region GetCredentialsAsync Tests

    [Fact]
    public async Task GetCredentialsAsync_ValidFile_ReturnsOAuthInfo()
    {
        // Arrange
        var credentials = new CredentialsInfo
        {
            ClaudeAiOAuth = new ClaudeAiOAuthInfo
            {
                SubscriptionType = "pro",
                RateLimitTier = "tier_3"
            }
        };
        var json = JsonSerializer.Serialize(credentials);
        File.WriteAllText(Path.Combine(_tempDir, ".credentials.json"), json);

        // Act
        var result = await _service.GetCredentialsAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal("pro", result.SubscriptionType);
        Assert.Equal("tier_3", result.RateLimitTier);
    }

    [Fact]
    public async Task GetCredentialsAsync_MissingFile_ReturnsNull()
    {
        // Act
        var result = await _service.GetCredentialsAsync();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetCredentialsAsync_NoOAuthField_ReturnsNull()
    {
        // Arrange
        var credentials = new { other = "field" };
        var json = JsonSerializer.Serialize(credentials);
        File.WriteAllText(Path.Combine(_tempDir, ".credentials.json"), json);

        // Act
        var result = await _service.GetCredentialsAsync();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetCredentialsAsync_InvalidJson_ReturnsNull()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_tempDir, ".credentials.json"), "{ invalid");

        // Act
        var result = await _service.GetCredentialsAsync();

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region GetConfigAsync Tests

    [Fact]
    public async Task GetConfigAsync_ValidFile_DeserializesCorrectly()
    {
        // Arrange
        var config = new ClaudeConfig
        {
            NumStartups = 5,
            InstallMethod = "direct",
            AutoUpdates = true
        };
        var json = JsonSerializer.Serialize(config);
        File.WriteAllText(Path.Combine(_tempDir, ".claude.json"), json);

        // Act
        var result = await _service.GetConfigAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(5, result.NumStartups);
        Assert.Equal("direct", result.InstallMethod);
        Assert.True(result.AutoUpdates);
    }

    [Fact]
    public async Task GetConfigAsync_MissingFile_ReturnsNull()
    {
        // Act
        var result = await _service.GetConfigAsync();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetConfigAsync_InvalidJson_ReturnsNull()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_tempDir, ".claude.json"), "not valid json {");

        // Act
        var result = await _service.GetConfigAsync();

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region GetRecentSessionsAsync Tests

    [Fact]
    public async Task GetRecentSessionsAsync_ValidSession_ReturnsSession()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var sessionDir = Path.Combine(_tempDir, "usage-data", "session-meta");
        Directory.CreateDirectory(sessionDir);

        var session = new SessionMeta
        {
            SessionId = "session-123",
            ProjectPath = "/home/user/project",
            StartTime = now.ToString("O"),
            DurationMinutes = 15.5,
            UserMessageCount = 10,
            AssistantMessageCount = 10
        };
        var json = JsonSerializer.Serialize(session);
        File.WriteAllText(Path.Combine(sessionDir, "session-123.json"), json);

        // Act
        var result = await _service.GetRecentSessionsAsync(days: 7);

        // Assert
        Assert.Single(result);
        Assert.Equal("session-123", result[0].SessionId);
    }

    [Fact]
    public async Task GetRecentSessionsAsync_OldSession_Excluded()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var oldDate = now.AddDays(-8);
        var sessionDir = Path.Combine(_tempDir, "usage-data", "session-meta");
        Directory.CreateDirectory(sessionDir);

        var session = new SessionMeta
        {
            SessionId = "old-session",
            StartTime = oldDate.ToString("O")
        };
        var json = JsonSerializer.Serialize(session);
        File.WriteAllText(Path.Combine(sessionDir, "old.json"), json);

        // Act
        var result = await _service.GetRecentSessionsAsync(days: 7);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetRecentSessionsAsync_MissingDirectory_ReturnsEmptyList()
    {
        // Act
        var result = await _service.GetRecentSessionsAsync(days: 7);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetRecentSessionsAsync_InvalidJson_Skipped()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var sessionDir = Path.Combine(_tempDir, "usage-data", "session-meta");
        Directory.CreateDirectory(sessionDir);

        File.WriteAllText(Path.Combine(sessionDir, "invalid.json"), "{ bad json");
        var session = new SessionMeta
        {
            SessionId = "valid-session",
            StartTime = now.ToString("O")
        };
        var json = JsonSerializer.Serialize(session);
        File.WriteAllText(Path.Combine(sessionDir, "valid.json"), json);

        // Act
        var result = await _service.GetRecentSessionsAsync(days: 7);

        // Assert
        Assert.Single(result);
        Assert.Equal("valid-session", result[0].SessionId);
    }

    [Fact]
    public async Task GetRecentSessionsAsync_InvalidTimestamp_Skipped()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var sessionDir = Path.Combine(_tempDir, "usage-data", "session-meta");
        Directory.CreateDirectory(sessionDir);

        var invalidSession = new { start_time = "not-a-date" };
        File.WriteAllText(Path.Combine(sessionDir, "invalid-time.json"),
            JsonSerializer.Serialize(invalidSession));

        var validSession = new SessionMeta
        {
            SessionId = "valid",
            StartTime = now.ToString("O")
        };
        File.WriteAllText(Path.Combine(sessionDir, "valid.json"),
            JsonSerializer.Serialize(validSession));

        // Act
        var result = await _service.GetRecentSessionsAsync(days: 7);

        // Assert
        Assert.Single(result);
        Assert.Equal("valid", result[0].SessionId);
    }

    [Fact]
    public async Task GetRecentSessionsAsync_MultipleValidSessions_ReturnsAll()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var yesterday = now.AddDays(-1);
        var sessionDir = Path.Combine(_tempDir, "usage-data", "session-meta");
        Directory.CreateDirectory(sessionDir);

        var session1 = new SessionMeta
        {
            SessionId = "session-1",
            StartTime = now.ToString("O")
        };
        var session2 = new SessionMeta
        {
            SessionId = "session-2",
            StartTime = yesterday.ToString("O")
        };

        File.WriteAllText(Path.Combine(sessionDir, "1.json"), JsonSerializer.Serialize(session1));
        File.WriteAllText(Path.Combine(sessionDir, "2.json"), JsonSerializer.Serialize(session2));

        // Act
        var result = await _service.GetRecentSessionsAsync(days: 7);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(result, s => s.SessionId == "session-1");
        Assert.Contains(result, s => s.SessionId == "session-2");
    }

    #endregion
}
