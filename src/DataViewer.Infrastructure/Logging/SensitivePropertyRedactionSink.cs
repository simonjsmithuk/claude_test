// =============================================================================
// SensitivePropertyRedactionSink — DataViewer.Infrastructure
// =============================================================================
// License note: Serilog (Apache 2.0) — https://github.com/serilog/serilog
// =============================================================================

using Serilog.Core;
using Serilog.Events;

namespace DataViewer.Infrastructure.Logging;

/// <summary>
/// A Serilog <see cref="ILogEventSink"/> decorator that post-processes every
/// <see cref="LogEvent"/> and replaces the value of any top-level property whose
/// name matches a known sensitive field with the redaction marker
/// <c>***REDACTED***</c> before forwarding the sanitised event to the wrapped sink.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SensitiveFieldDestructuringPolicy"/> handles objects that are
/// <em>destructured</em> by Serilog (structured objects passed with the
/// <c>@</c> operator or <c>destructureObjects: true</c>). However, a developer
/// might log a sensitive value as a plain scalar property, for example:
/// <code>
/// Log.Information("Token: {Authorization}", authHeader);
/// // or
/// using (LogContext.PushProperty("Authorization", authHeader)) { … }
/// // or
/// logger.LogInformation("{PasswordHash}", user.PasswordHash);
/// </code>
/// In all these cases, the destructuring policy is never invoked, so the sensitive
/// value would reach the sinks unredacted.
/// </para>
/// <para>
/// This sink sits between the Serilog pipeline and every downstream write sink
/// (console and file) and performs a final pass over the event's
/// <see cref="LogEvent.Properties"/> dictionary. Any property whose name is in
/// <see cref="SensitiveFieldDestructuringPolicy.SensitivePropertyNames"/> is replaced
/// with a <see cref="ScalarValue"/> containing <c>***REDACTED***</c>.
/// </para>
/// <para>
/// The check is applied using the same case-insensitive
/// <see cref="StringComparer.OrdinalIgnoreCase"/> semantics as the destructuring policy,
/// so camelCase, PascalCase, and SCREAMING_SNAKE_CASE variants are all caught.
/// </para>
/// <para>
/// <strong>Thread safety:</strong> this class is stateless beyond the immutable
/// <see cref="_wrappedSink"/> reference. It is safe for concurrent use without locking.
/// </para>
/// </remarks>
internal sealed class SensitivePropertyRedactionSink : ILogEventSink
{
    private readonly ILogEventSink _wrappedSink;

    /// <summary>
    /// Initialises a new <see cref="SensitivePropertyRedactionSink"/> that forwards
    /// sanitised events to <paramref name="wrappedSink"/>.
    /// </summary>
    /// <param name="wrappedSink">
    /// The downstream sink (console, file, etc.) to which sanitised events are forwarded.
    /// Must not be <see langword="null"/>.
    /// </param>
    public SensitivePropertyRedactionSink(ILogEventSink wrappedSink)
    {
        _wrappedSink = wrappedSink
            ?? throw new ArgumentNullException(nameof(wrappedSink));
    }

    /// <inheritdoc />
    /// <summary>
    /// Sanitises sensitive top-level log properties in <paramref name="logEvent"/> and
    /// forwards the event to the wrapped downstream sink.
    /// </summary>
    public void Emit(LogEvent logEvent)
    {
        // Walk the top-level properties dictionary for names that must be redacted.
        // We build the replacement list first to avoid mutating the collection while
        // iterating it.
        List<string>? sensitiveKeys = null;

        foreach (var key in logEvent.Properties.Keys)
        {
            if (SensitiveFieldDestructuringPolicy.SensitivePropertyNames.Contains(key))
            {
                sensitiveKeys ??= new List<string>(capacity: 4);
                sensitiveKeys.Add(key);
            }
        }

        if (sensitiveKeys is not null)
        {
            // AddOrUpdateProperty replaces an existing property in the event.
            // LogEvent.AddPropertyIfAbsent is read-only for absent keys, so we use
            // the mutable AddOrUpdateProperty overload to force the redaction.
            foreach (var key in sensitiveKeys)
            {
                logEvent.AddOrUpdateProperty(
                    new LogEventProperty(key, new ScalarValue(SensitiveFieldDestructuringPolicy.RedactedMarker)));
            }
        }

        _wrappedSink.Emit(logEvent);
    }
}
