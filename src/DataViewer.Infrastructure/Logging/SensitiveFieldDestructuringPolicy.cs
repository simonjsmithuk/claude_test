// =============================================================================
// SensitiveFieldDestructuringPolicy — DataViewer.Infrastructure
// =============================================================================
// License note: Serilog (Apache 2.0) — https://github.com/serilog/serilog
// =============================================================================

using Serilog.Core;
using Serilog.Events;

namespace DataViewer.Infrastructure.Logging;

/// <summary>
/// A Serilog <see cref="IDestructuringPolicy"/> that intercepts structured objects
/// before they are serialised into log events and replaces the value of any property
/// whose name matches a known sensitive field with a fixed redaction marker.
/// </summary>
/// <remarks>
/// <para>
/// This policy is registered globally on the <see cref="Serilog.LoggerConfiguration"/>
/// (via <c>.Destructure.With&lt;SensitiveFieldDestructuringPolicy&gt;()</c>) and therefore
/// applies to every log entry at every level — Verbose, Debug, Information, Warning, Error,
/// and Fatal — without exception.
/// </para>
/// <para>
/// Fields protected by this policy (SDD § 6.3 and TASK-020 Acceptance Criteria):
/// <list type="bullet">
///   <item><description><c>SecretAccessKey</c>           — raw or decrypted AWS IAM secret</description></item>
///   <item><description><c>PasswordHash</c>              — bcrypt hash stored in <c>User.PasswordHash</c></description></item>
///   <item><description><c>TokenHash</c>                 — SHA-256 refresh-token hash in <c>RefreshToken.TokenHash</c></description></item>
///   <item><description><c>Authorization</c>             — HTTP <c>Authorization</c> request header (Bearer JWT)</description></item>
///   <item><description><c>DATAVIEWER_ENCRYPTION_KEY</c> — AES-256 master key environment variable value</description></item>
/// </list>
/// </para>
/// <para>
/// Comparison is intentionally case-insensitive so that camelCase (<c>secretAccessKey</c>),
/// PascalCase (<c>SecretAccessKey</c>), and SCREAMING_SNAKE_CASE (<c>SECRET_ACCESS_KEY</c>)
/// variants are all caught.
/// </para>
/// <para>
/// <strong>Scope of protection:</strong> this policy handles structured-object destructuring.
/// Sensitive data logged as top-level scalar properties (e.g. via
/// <c>LogContext.PushProperty("Authorization", value)</c> or dictionary-valued log properties)
/// is handled by the companion <see cref="SensitivePropertyRedactionSink"/> which post-processes
/// every <see cref="LogEvent"/> before it reaches any downstream sink.
/// </para>
/// </remarks>
public sealed class SensitiveFieldDestructuringPolicy : IDestructuringPolicy
{
    // Redaction marker placed wherever a sensitive value would appear in any log output.
    internal const string RedactedMarker = "***REDACTED***";

    /// <summary>
    /// Property names that must never appear in plaintext in any log output.
    /// Comparison is performed case-insensitively (see <see cref="TryDestructure"/>).
    /// </summary>
    internal static readonly HashSet<string> SensitivePropertyNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "SecretAccessKey",
            "PasswordHash",
            "TokenHash",
            "Authorization",
            "DATAVIEWER_ENCRYPTION_KEY",
        };

    /// <inheritdoc />
    /// <summary>
    /// Inspects <paramref name="value"/> when it is a structured object (POCO / record)
    /// and replaces the scalar value of each sensitive property with <see cref="RedactedMarker"/>.
    /// Non-sensitive properties are recursively destructured so that nested sensitive
    /// fields inside deeper objects are also redacted.
    /// </summary>
    /// <param name="value">The value being destructured by Serilog.</param>
    /// <param name="propertyValueFactory">
    /// Factory supplied by Serilog to recursively destructure nested values.
    /// </param>
    /// <param name="result">
    /// The sanitised <see cref="LogEventPropertyValue"/> if this policy handles the type;
    /// <see langword="null"/> otherwise.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the value is a structured object that contains at least
    /// one sensitive property, and <paramref name="result"/> has been populated;
    /// <see langword="false"/> to let Serilog fall through to the next destructuring policy.
    /// </returns>
    public bool TryDestructure(
        object value,
        ILogEventPropertyValueFactory propertyValueFactory,
        out LogEventPropertyValue? result)
    {
        // Only intercept structured objects (classes / records with public properties).
        // Primitives, strings, enums, and collections delegate to Serilog's built-in policies.
        if (value is null || value.GetType().IsPrimitive || value is string)
        {
            result = null;
            return false;
        }

        var properties = value.GetType().GetProperties(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        // Short-circuit: skip objects that carry no sensitive properties to avoid
        // unnecessary reflection overhead on every logged value.
        if (!ContainsSensitiveProperty(properties))
        {
            result = null;
            return false;
        }

        // Rebuild the object's property list, redacting sensitive ones.
        var sanitisedProperties = new List<LogEventProperty>(properties.Length);

        foreach (var prop in properties)
        {
            // Skip write-only or indexed properties — they cannot be safely read.
            if (!prop.CanRead || prop.GetIndexParameters().Length > 0)
                continue;

            LogEventPropertyValue propertyValue;

            if (SensitivePropertyNames.Contains(prop.Name))
            {
                // Replace the actual value with the redaction marker regardless of
                // whether the value is null, empty, or a populated secret.
                propertyValue = new ScalarValue(RedactedMarker);
            }
            else
            {
                try
                {
                    var rawValue = prop.GetValue(value);

                    // Recursively destructure non-sensitive nested objects so that
                    // sensitive fields at deeper nesting levels are also redacted.
                    propertyValue = propertyValueFactory.CreatePropertyValue(
                        rawValue, destructureObjects: true);
                }
                catch (Exception)
                {
                    // ASSUMPTION: If a property getter throws (e.g. an EF Core navigation
                    // property that is not loaded), treat the value as unreadable rather than
                    // propagating the exception into the logging pipeline.
                    propertyValue = new ScalarValue("<unreadable>");
                }
            }

            sanitisedProperties.Add(new LogEventProperty(prop.Name, propertyValue));
        }

        result = new StructureValue(sanitisedProperties);
        return true;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns <see langword="true"/> if any element of <paramref name="properties"/>
    /// has a name that matches a known sensitive field (case-insensitive check).
    /// </summary>
    private static bool ContainsSensitiveProperty(
        System.Reflection.PropertyInfo[] properties)
    {
        foreach (var prop in properties)
        {
            if (SensitivePropertyNames.Contains(prop.Name))
                return true;
        }

        return false;
    }
}
