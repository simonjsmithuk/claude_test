using Microsoft.Extensions.Logging;

namespace DataViewer.Tests.S3.Helpers;

/// <summary>
/// A test-double <see cref="ILogger{TCategoryName}"/> that accumulates every
/// formatted log message string so that tests can assert on log content.
/// </summary>
/// <remarks>
/// Used specifically to verify that sensitive values (e.g. the AWS Secret Access Key)
/// never appear in structured log output, satisfying the acceptance-criteria constraint:
/// "Decrypted secret access key MUST NOT appear in any Serilog structured log output."
/// </remarks>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<string> _messages = new();

    /// <summary>All formatted log message strings written since creation.</summary>
    public IReadOnlyList<string> Messages => _messages.AsReadOnly();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);

        // Also capture the raw state (which carries named properties in Serilog-style
        // structured logging) so that structured property values are not missed.
        var stateString = state?.ToString() ?? string.Empty;

        _messages.Add(message);
        _messages.Add(stateString);

        // Capture exception details too
        if (exception is not null)
        {
            _messages.Add(exception.Message);
            _messages.Add(exception.ToString());
        }
    }

    /// <summary>Minimal no-op scope returned from <see cref="BeginScope{TState}"/>.</summary>
    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
