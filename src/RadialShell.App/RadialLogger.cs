using System.Text.Json;
using System.Threading.Channels;

namespace RadialShell.App;

internal enum RadialLogLevel
{
    Trace,
    Debug,
    Info,
    Warn,
    Error
}

/// <summary>Writes bounded JSONL diagnostics to the app-local data directory.</summary>
internal static class RadialLogger
{
    private const int QueueCapacity = 512;
    private const int MaxMessageLength = 4096;
    private const long MaxLogBytes = 2 * 1024 * 1024;
    private const int BackupCount = 3;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private static readonly string LogPath = Path.Combine(
        AppContext.BaseDirectory, "data", "logs", "radialshell.jsonl");
    private static readonly object Gate = new();
    private static Channel<LogRecord>? _queue;
    private static Task? _writerTask;
    private static int _minimumLevel = (int)RadialLogLevel.Info;

    private sealed record LogRecord(DateTimeOffset TimestampUtc, string Level, string Event, string Message);

    public static IDisposable Initialize(RadialLogLevel minimumLevel)
    {
        lock (Gate)
        {
            if (_queue is not null)
                throw new InvalidOperationException("RadialShell logging is already initialized.");

            Volatile.Write(ref _minimumLevel, (int)minimumLevel);
            var queue = Channel.CreateBounded<LogRecord>(new BoundedChannelOptions(QueueCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });
            Volatile.Write(ref _queue, queue);
            _writerTask = Task.Run(() => ConsumeAsync(queue.Reader));
            return new ShutdownLease();
        }
    }

    public static bool TryParseLevel(string value, out RadialLogLevel level)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "error":
                level = RadialLogLevel.Error;
                return true;
            case "warn":
            case "warning":
                level = RadialLogLevel.Warn;
                return true;
            case "info":
                level = RadialLogLevel.Info;
                return true;
            case "debug":
                level = RadialLogLevel.Debug;
                return true;
            case "trace":
                level = RadialLogLevel.Trace;
                return true;
            default:
                level = RadialLogLevel.Info;
                return false;
        }
    }

    public static void Trace(string eventName, string message) => Write(RadialLogLevel.Trace, eventName, message);
    public static void Debug(string eventName, string message) => Write(RadialLogLevel.Debug, eventName, message);
    public static void Info(string eventName, string message) => Write(RadialLogLevel.Info, eventName, message);
    public static void Warn(string eventName, string message) => Write(RadialLogLevel.Warn, eventName, message);
    public static void Error(string eventName, string message) => Write(RadialLogLevel.Error, eventName, message);

    private static void Write(RadialLogLevel level, string eventName, string message)
    {
        if ((int)level < Volatile.Read(ref _minimumLevel))
            return;

        var safeEvent = Bound(eventName, 96);
        var safeMessage = Bound(message, MaxMessageLength);
        var record = new LogRecord(DateTimeOffset.UtcNow, level.ToString().ToUpperInvariant(), safeEvent, safeMessage);
        var queue = Volatile.Read(ref _queue);
        if (queue is null)
            EmitConsole(record);
        else
            _ = queue.Writer.TryWrite(record);
    }

    private static void EmitConsole(LogRecord record)
    {
        try
        {
            var output = $"{record.Level} [{record.Event}] {record.Message}";
            if (record.Level is "WARN" or "ERROR")
                Console.Error.WriteLine(output);
            else
                Console.WriteLine(output);
        }
        catch
        {
            // A redirected/closed console must not interrupt shell or UI work.
        }
    }

    private static string Bound(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static async Task ConsumeAsync(ChannelReader<LogRecord> reader)
    {
        FileStream? stream = null;
        long currentLength = 0;
        try
        {
            await foreach (var record in reader.ReadAllAsync().ConfigureAwait(false))
            {
                EmitConsole(record);
                var json = JsonSerializer.SerializeToUtf8Bytes(record, JsonOptions);
                var line = new byte[json.Length + 1];
                Buffer.BlockCopy(json, 0, line, 0, json.Length);
                line[^1] = 0x0A;
                var recordLength = line.LongLength;
                if (stream is null || currentLength + recordLength > MaxLogBytes)
                {
                    if (stream is not null)
                    {
                        await stream.DisposeAsync().ConfigureAwait(false);
                        stream = null;
                        RotateLogs();
                    }
                    else if (File.Exists(LogPath) && new FileInfo(LogPath).Length + recordLength > MaxLogBytes)
                    {
                        RotateLogs();
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                    stream = new FileStream(
                        LogPath, FileMode.Append, FileAccess.Write, FileShare.Read,
                        bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    currentLength = stream.Length;
                }

                await stream.WriteAsync(line.AsMemory()).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
                currentLength += recordLength;
            }
        }
        catch (Exception ex)
        {
            try { Console.Error.WriteLine($"Local diagnostic log unavailable: {ex.GetBaseException().Message}"); }
            catch { }
        }
        finally
        {
            if (stream is not null)
            {
                try { await stream.DisposeAsync().ConfigureAwait(false); }
                catch { }
            }
        }
    }

    private static void RotateLogs()
    {
        if (File.Exists(LogPath + "." + BackupCount))
            File.Delete(LogPath + "." + BackupCount);

        for (var index = BackupCount - 1; index >= 1; index--)
        {
            var source = LogPath + "." + index;
            if (File.Exists(source))
                File.Move(source, LogPath + "." + (index + 1), overwrite: true);
        }

        if (File.Exists(LogPath))
            File.Move(LogPath, LogPath + ".1", overwrite: true);
    }

    private static void Shutdown()
    {
        Channel<LogRecord>? queue;
        Task? writerTask;
        lock (Gate)
        {
            queue = Volatile.Read(ref _queue);
            writerTask = _writerTask;
            Volatile.Write(ref _queue, null);
            _writerTask = null;
        }

        if (queue is null)
            return;

        queue.Writer.TryComplete();
        try { _ = writerTask?.Wait(TimeSpan.FromMilliseconds(50)); }
        catch (AggregateException) { }
    }

    private sealed class ShutdownLease : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                Shutdown();
        }
    }
}
