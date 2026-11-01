namespace RadialShell.App;

internal static class RadialLoggerSmokeTests
{
    public static void Run()
    {
        Verify("error", RadialLogLevel.Error);
        Verify("WARN", RadialLogLevel.Warn);
        Verify("info", RadialLogLevel.Info);
        Verify("debug", RadialLogLevel.Debug);
        Verify("trace", RadialLogLevel.Trace);

        if (RadialLogger.TryParseLevel("verbose", out _))
            throw new InvalidOperationException("An unsupported log-level value was accepted.");
        VerifyArguments(["--verbose"], RadialLogLevel.Debug);
        VerifyArguments(["--trace"], RadialLogLevel.Trace);
        VerifyArguments(["--trace", "--log-level", "warn"], RadialLogLevel.Warn);
        if (Program.TryGetLogLevel(["--log-level"], out _, out _))
            throw new InvalidOperationException("A missing --log-level value was accepted.");

        Console.WriteLine("PASS: log levels and command-line overrides are validated.");
        RadialLogger.Info("self_test.logger", "Structured JSONL logger smoke test.");
    }

    private static void VerifyArguments(string[] args, RadialLogLevel expected)
    {
        if (!Program.TryGetLogLevel(args, out var actual, out var error) || actual != expected)
            throw new InvalidOperationException($"Command-line log level parsing failed: {error}");
    }

    private static void Verify(string value, RadialLogLevel expected)
    {
        if (!RadialLogger.TryParseLevel(value, out var actual) || actual != expected)
            throw new InvalidOperationException($"Log level parsing failed for '{value}'.");
    }
}
