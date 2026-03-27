namespace DataViewer.Application.DTOs.Common;

/// <summary>
/// Marker attribute that signals to logging middleware, diagnostics pipelines, and
/// code-review tooling that the decorated property carries sensitive data (e.g. tokens,
/// secrets) that must be redacted before the value is written to any log sink.
/// </summary>
/// <remarks>
/// This attribute carries <strong>no runtime behaviour</strong> by itself. It is a
/// documentation and convention signal. Logging middleware authors must inspect this
/// attribute (via reflection) and replace marked values with a redaction placeholder
/// such as <c>"[REDACTED]"</c> before writing response bodies to structured logs.
/// <para>
/// Example: <c>ASP.NET Core UseHttpLogging()</c> middleware will log full response
/// bodies if enabled. Any DTO property decorated with this attribute should be
/// suppressed or masked in that pipeline.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class SensitiveDataAttribute : Attribute
{
    /// <summary>
    /// Optional human-readable reason explaining why this field is sensitive.
    /// Defaults to a generic redaction notice.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// Initialises the attribute with an optional explanatory reason.
    /// </summary>
    /// <param name="reason">
    /// Description of why the field is sensitive. Defaults to
    /// <c>"This field contains sensitive data and must be redacted in logs."</c>
    /// </param>
    public SensitiveDataAttribute(
        string reason = "This field contains sensitive data and must be redacted in logs.")
    {
        Reason = reason;
    }
}
