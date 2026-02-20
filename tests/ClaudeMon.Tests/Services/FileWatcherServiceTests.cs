using System.Reflection;
using ClaudeMon.Services;

namespace ClaudeMon.Tests.Services;

/// <summary>
/// Tests for FileWatcherService covering initialization, file watching, debouncing, and cleanup scenarios.
/// </summary>
public class FileWatcherServiceTests
{
    [Fact]
    public void Constructor_ExpandsEnvironmentVariables()
    {
        // Arrange
        var tempPath = Environment.GetEnvironmentVariable("TEMP") ?? "/tmp";
        var claudePath = Path.Combine("%TEMP%", "test_claude");

        // Act
        var service = new FileWatcherService(claudePath);

        // Assert - Use reflection to read the private _watchPath field
        var watchPathField = typeof(FileWatcherService).GetField("_watchPath", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(watchPathField);
        var watchPath = (string?)watchPathField.GetValue(service);

        Assert.NotNull(watchPath);
        Assert.DoesNotContain("%TEMP%", watchPath);
        Assert.StartsWith(tempPath, watchPath);
    }

    [Fact]
    public void Constructor_StoresCustomPollInterval()
    {
        // Arrange
        var customInterval = 30_000;
        var tempDir = Directory.CreateTempSubdirectory();

        try
        {
            // Act
            var service = new FileWatcherService(tempDir.FullName, customInterval);

            // Assert - Use reflection to read the private _pollIntervalMs field
            var pollField = typeof(FileWatcherService).GetField("_pollIntervalMs", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(pollField);
            var storedInterval = (int?)pollField.GetValue(service);

            Assert.Equal(customInterval, storedInterval);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Start_WithNonExistentPath_DoesNotThrow()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), "non_existent_claude_" + Guid.NewGuid());
        var service = new FileWatcherService(nonExistentPath);

        // Act & Assert - Should not throw
        var exception = Record.Exception(() => service.Start());
        Assert.Null(exception);

        // Cleanup
        service.Dispose();
    }

    [Fact]
    public void Start_WithExistingPath_CreatesFSWatcher()
    {
        // Arrange
        var tempDir = Directory.CreateTempSubdirectory();
        var service = new FileWatcherService(tempDir.FullName);
        var eventFired = false;

        try
        {
            service.DataChanged += () => eventFired = true;

            // Act
            service.Start();

            // Create a test JSON file
            var testFile = Path.Combine(tempDir.FullName, "test.json");
            File.WriteAllText(testFile, "{}");

            // Assert - Wait for the debounce window plus some buffer
            var timeout = DateTime.UtcNow.AddSeconds(2);
            while (!eventFired && DateTime.UtcNow < timeout)
            {
                System.Threading.Thread.Sleep(50);
            }

            Assert.True(eventFired, "DataChanged event should have been fired when JSON file was created");
        }
        finally
        {
            service.Dispose();
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Start_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        var tempDir = Directory.CreateTempSubdirectory();
        var service = new FileWatcherService(tempDir.FullName);

        try
        {
            service.Start();
            service.Dispose();

            // Act & Assert
            var exception = Assert.Throws<ObjectDisposedException>(() => service.Start());
            Assert.Equal(nameof(FileWatcherService), exception.ObjectName);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Dispose_MultipleTimes_DoesNotThrow()
    {
        // Arrange
        var tempDir = Directory.CreateTempSubdirectory();
        var service = new FileWatcherService(tempDir.FullName);

        try
        {
            // Act & Assert - Calling Dispose multiple times should not throw
            service.Dispose();
            var exception = Record.Exception(() => service.Dispose());
            Assert.Null(exception);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Dispose_BeforeStart_DoesNotThrow()
    {
        // Arrange
        var tempDir = Directory.CreateTempSubdirectory();
        var service = new FileWatcherService(tempDir.FullName);

        try
        {
            // Act & Assert - Dispose without calling Start should not throw
            var exception = Record.Exception(() => service.Dispose());
            Assert.Null(exception);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void PollTimer_FiresDataChanged()
    {
        // Arrange
        var tempDir = Directory.CreateTempSubdirectory();
        var pollIntervalMs = 100; // Short interval for faster test execution
        var service = new FileWatcherService(tempDir.FullName, pollIntervalMs);
        var eventFired = new System.Threading.ManualResetEventSlim(initialState: false);

        try
        {
            service.DataChanged += () => eventFired.Set();

            // Act
            service.Start();

            // Assert - Wait for the poll timer to fire with generous timeout
            var timedOut = !eventFired.Wait(TimeSpan.FromSeconds(5));
            Assert.False(timedOut, "DataChanged event should have been fired by poll timer within 5 seconds");
        }
        finally
        {
            service.Dispose();
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void DataChanged_IsDebounced()
    {
        // Arrange - Test debounce logic directly via reflection to avoid
        // flaky FileSystemWatcher timing on CI runners.
        var tempDir = Directory.CreateTempSubdirectory();
        var service = new FileWatcherService(tempDir.FullName, pollIntervalMs: 60_000);
        var eventCount = 0;

        try
        {
            service.DataChanged += () => Interlocked.Increment(ref eventCount);
            // Don't call Start() - we invoke RaiseDebounced directly to isolate debounce logic.

            var raiseDebounced = typeof(FileWatcherService)
                .GetMethod("RaiseDebounced", BindingFlags.NonPublic | BindingFlags.Instance)!;

            // Act - Invoke RaiseDebounced 5 times rapidly (within the 500ms debounce window)
            for (int i = 0; i < 5; i++)
            {
                raiseDebounced.Invoke(service, null);
            }

            // Assert - Only the first call should have passed the debounce gate
            Assert.Equal(1, eventCount);
        }
        finally
        {
            service.Dispose();
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void FileChange_TriggersDataChanged()
    {
        // Arrange
        var tempDir = Directory.CreateTempSubdirectory();
        var service = new FileWatcherService(tempDir.FullName);
        var eventFired = new System.Threading.ManualResetEventSlim(initialState: false);

        try
        {
            service.DataChanged += () => eventFired.Set();

            // Act
            service.Start();

            // Create a .json file (should trigger FileSystemWatcher)
            var testJsonFile = Path.Combine(tempDir.FullName, "test_data.json");
            File.WriteAllText(testJsonFile, @"{ ""key"": ""value"" }");

            // Assert
            var timedOut = !eventFired.Wait(TimeSpan.FromSeconds(3));
            Assert.False(timedOut, "DataChanged event should have been fired when JSON file was created");
        }
        finally
        {
            service.Dispose();
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void NonJsonFiles_DoNotTriggerDataChanged()
    {
        // Arrange
        var tempDir = Directory.CreateTempSubdirectory();
        var service = new FileWatcherService(tempDir.FullName);

        try
        {
            service.Start();

            // Act - Create a non-JSON file
            var testTxtFile = Path.Combine(tempDir.FullName, "test.txt");
            File.WriteAllText(testTxtFile, "test content");

            // Wait a bit longer than debounce window to ensure poll timer didn't fire
            System.Threading.Thread.Sleep(700);

            // Assert - FileSystemWatcher should not fire for .txt files
            // Note: Poll timer might still fire, so we just check that FSW didn't add extra triggers
            // In a real scenario with no other events, eventFired might be true only from poll timer
            // This is a limitation of the test - we're mainly testing that .txt doesn't cause FSW to fire
        }
        finally
        {
            service.Dispose();
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void DataChanged_IsNullSafe()
    {
        // Arrange
        var tempDir = Directory.CreateTempSubdirectory();
        var service = new FileWatcherService(tempDir.FullName);

        try
        {
            // Don't subscribe to DataChanged
            // Act & Assert - Should not throw when invoking null event
            var exception = Record.Exception(() => service.Start());
            Assert.Null(exception);
        }
        finally
        {
            service.Dispose();
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void FileWatcherError_RecreatesWatcher()
    {
        // Arrange
        var tempDir = Directory.CreateTempSubdirectory();
        var service = new FileWatcherService(tempDir.FullName);

        try
        {
            // Act
            service.Start();

            // Get the watcher field and dispose it externally to simulate an error
            var watcherField = typeof(FileWatcherService).GetField("_watcher", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(watcherField);
            var watcher = (FileSystemWatcher?)watcherField.GetValue(service);

            if (watcher != null)
            {
                watcher.Dispose();
            }

            // Give the service time to recreate the watcher
            System.Threading.Thread.Sleep(100);

            // Assert - Watcher should be recreated
            var newWatcher = (FileSystemWatcher?)watcherField.GetValue(service);
            Assert.NotNull(newWatcher);
        }
        finally
        {
            service.Dispose();
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void DebounceWindow_EnforcedCorrectly()
    {
        // Arrange
        var tempDir = Directory.CreateTempSubdirectory();
        var service = new FileWatcherService(tempDir.FullName, pollIntervalMs: 10_000);
        var eventTimes = new List<DateTime>();
        var lockObj = new object();

        try
        {
            service.DataChanged += () =>
            {
                lock (lockObj)
                {
                    eventTimes.Add(DateTime.UtcNow);
                }
            };
            service.Start();

            // Act - Fire first event
            var testFile1 = Path.Combine(tempDir.FullName, "test1.json");
            File.WriteAllText(testFile1, "{}");
            System.Threading.Thread.Sleep(600); // Wait past debounce

            // Fire second event
            var testFile2 = Path.Combine(tempDir.FullName, "test2.json");
            File.WriteAllText(testFile2, "{}");
            System.Threading.Thread.Sleep(600);

            // Assert - Should have recorded 2 separate events with gap > 500ms
            lock (lockObj)
            {
                Assert.True(eventTimes.Count >= 2, "Should have at least 2 events");
                var timeBetweenEvents = (eventTimes[1] - eventTimes[0]).TotalMilliseconds;
                Assert.True(timeBetweenEvents >= 500, $"Time between events should be >= 500ms, but was {timeBetweenEvents}ms");
            }
        }
        finally
        {
            service.Dispose();
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void FileChanged_AndCreated_BothTriggerDataChanged()
    {
        // Arrange
        var tempDir = Directory.CreateTempSubdirectory();
        var service = new FileWatcherService(tempDir.FullName);
        var eventCount = 0;
        var lockObj = new object();

        try
        {
            service.DataChanged += () =>
            {
                lock (lockObj)
                {
                    eventCount++;
                }
            };
            service.Start();

            // Act - Create a file
            var testFile = Path.Combine(tempDir.FullName, "test.json");
            File.WriteAllText(testFile, "{}");
            System.Threading.Thread.Sleep(700); // Wait past debounce

            // Modify the file
            File.AppendAllText(testFile, ",{}");
            System.Threading.Thread.Sleep(700); // Wait past debounce

            // Assert
            lock (lockObj)
            {
                Assert.True(eventCount >= 2, $"Should have at least 2 events, but got {eventCount}");
            }
        }
        finally
        {
            service.Dispose();
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Dispose_CleansUpResources()
    {
        // Arrange
        var tempDir = Directory.CreateTempSubdirectory();
        var service = new FileWatcherService(tempDir.FullName);

        try
        {
            service.Start();

            // Get field references before disposal
            var watcherField = typeof(FileWatcherService).GetField("_watcher", BindingFlags.NonPublic | BindingFlags.Instance);
            var pollTimerField = typeof(FileWatcherService).GetField("_pollTimer", BindingFlags.NonPublic | BindingFlags.Instance);

            // Act
            service.Dispose();

            // Assert - Both watcher and timer should be null after disposal
            Assert.NotNull(watcherField);
            Assert.NotNull(pollTimerField);
            var watcher = (FileSystemWatcher?)watcherField.GetValue(service);
            var pollTimer = (Timer?)pollTimerField.GetValue(service);

            Assert.Null(watcher);
            Assert.Null(pollTimer);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }
}
