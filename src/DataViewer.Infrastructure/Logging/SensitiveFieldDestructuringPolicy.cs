using Serilog.Core;
using Serilog.Events;

namespace DataViewer.Infrastructure.Logging;

/// <summary>
/// A Serilog <see cref="IDestructuringPolicy"/> that intercepts structured objects before
/// they are serialised into log events and replaces the value of any property whose name
/// matches a known sensitive field with a fixed redaction marker.
/// </summary>
/// <remarks>
/// <para>
/// This policy is registered globally on the <see cref="Serilog.LoggerConfiguration"/> and
/// therefore applies to every log entry at every level — Debug, Information, Warning, Error,
/// and Fatal — without exception.
/// </para>
/// <para>
/// Fields protected by this policy (per section 6.3 of the System Design Document):
/// <list type="bullet">
///   <item><description><c>SecretAccessKey</c>  — raw or decrypted AWS IAM secret</description></item>
///   <item><description><c>PasswordHash</c>     — bcrypt hash from <c>User.PasswordHash</c></description></item>
///   <item><description><c>TokenHash</c>        — SHA-256 refresh-token hash from <c>RefreshToken.TokenHash</c></description></item>
///   <item><description><c>Authorization</c>    — HTTP <c>Authorization</c> request header (Bearer JWT)</description></item>
///   <item><description><c>DATAVIEWER_ENCRYPTION_KEY</c> — AES-256 master key environment variable name</description></item>
/// </list>
/// </para>
/// <para>
/// The comparison is intentionally case-insensitive so that camelCase, PascalCase, and
/// SCREAMING_SNAKE_CASE variants are all caught.
/// </para>
/// </remarks>
public sealed class SensitiveFieldDestructuringPolicy : IDestructuringPolicy
{
    // Redaction marker placed wherever a sensitive value would appear.
    private const string RedactedMarker = "***REDACTED***";

    /// <summary>
    /// Property names that must never appear in plaintext in any log output.
    /// Comparison is performed case-insensitively (see <see cref="TryDestructure"/>).
    /// </summary>
    private static readonly HashSet<string> SensitivePropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "SecretAccessKey",
        "PasswordHash",
        "TokenHash",
        "Authorization",
        "DATAVIEWER_ENCRYPTION_KEY",
    };

    /// <inheritdoc />
    /// <summary>
    /// Inspects <paramref name="value"/> if it is a structured object and replaces the scalar
    /// value of each sensitive property with <see cref="RedactedMarker"/>.
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
    /// <see langword="true"/> when the value is a type this policy handles and
    /// <paramref name="result"/> has been populated; <see langword="false"/> to let Serilog
    /// fall through to the next destructuring policy.
    /// </returns>
    public bool TryDestructure(
        object value,
        ILogEventPropertyValueFactory propertyValueFactory,
        out LogEventPropertyValue? result)
    {
        // Only intercept structured objects (classes/records with properties).
        // Primitives, strings, and collections are handled by other Serilog policies.
        if (value is null || value.GetType().IsPrimitive || value is string)
        {
            result = null;
            return false;
        }

        var properties = value.GetType().GetProperties();

        // Short-circuit: skip objects that have no sensitive properties to avoid
        // unnecessary reflection overhead on every logged value.
        if (!ContainsSensitiveProperty(properties))
        {
            result = null;
            return false;
        }

        // Rebuild the object's properties, redacting sensitive ones.
        var sanitisedProperties = new List<LogEventProperty>(properties.Length);

        foreach (var prop in properties)
        {
            LogEventPropertyValue propertyValue;

            if (SensitivePropertyNames.Contains(prop.Name))
            {
                // Replace the actual value with the redaction marker regardless of
                // what the value actually contains — including null, empty, or default.
                propertyValue = new ScalarValue(RedactedMarker);
            }
            else
            {
                try
                {
                    var rawValue = prop.GetValue(value);
                    // Recursively destructure non-sensitive nested objects so that
                    // deeper sensitive fields are also redacted.
                    propertyValue = propertyValueFactory.CreatePropertyValue(rawValue, destructureObjects: true);
                }
                catch (Exception)
                {
                    // ASSUMPTION: If a property getter throws (e.g. navigation property
                    // not loaded), treat it as an unreadable value rather than propagating
                    // the exception into the logging pipeline.
                    propertyValue = new ScalarValue("<unreadable>");
                }
            }

            sanitisedProperties.Add(new LogEventProperty(prop.Name, propertyValue));
        }

        result = new StructureValue(sanitisedProperties);
        return true;
    }

    /// <summary>
    /// Returns <see langword="true"/> if any of <paramref name="properties"/> has a name
    /// that matches a known sensitive field (case-insensitive check).
    /// </summary>
    private static bool ContainsSensitiveProperty(System.Reflection.PropertyInfo[] properties)
    {
        foreach (var prop in properties)
        {
            if (SensitivePropertyNames.Contains(prop.Name))
                return true;
        }

        return false;
    }
}
