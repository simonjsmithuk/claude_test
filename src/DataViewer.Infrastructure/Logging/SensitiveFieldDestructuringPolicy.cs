// =============================================================================
// SensitiveFieldDestructuringPolicy — DataViewer.Infrastructure
// =============================================================================
// License note: Serilog (Apache 2.0) — https://github.com/serilog/serilog
// =============================================================================

using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Reflection;
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
/// <strong>Scope of protection:</strong> this policy handles structured-object destructuring
/// for POCO/record types as well as <see cref="IDictionary{TKey,TValue}"/> values whose keys
/// match a sensitive field name. Sensitive data logged as top-level scalar properties
/// (e.g. via <c>LogContext.PushProperty("Authorization", value)</c>) is handled by the
/// companion <see cref="SensitivePropertyRedactionSink"/> which post-processes every
/// <see cref="LogEvent"/> before it reaches any downstream sink.
/// </para>
/// </remarks>
// ASSUMPTION: This class is an infrastructure implementation detail and is never consumed
// directly by application or domain layers. It is therefore internal. Tests reference it
// via [assembly: InternalsVisibleTo("DataViewer.Tests")] declared in the .csproj.
internal sealed class SensitiveFieldDestructuringPolicy : IDestructuringPolicy
{
    // Redaction marker placed wherever a sensitive value would appear in any log output.
    internal const string RedactedMarker = "***REDACTED***";

    /// <summary>
    /// Property names that must never appear in plaintext in any log output.
    /// Comparison is performed case-insensitively (see <see cref="TryDestructure"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="FrozenSet{T}"/> (.NET 8, <c>System.Collections.Frozen</c>) is used
    /// rather than <see cref="HashSet{T}"/> because:
    /// <list type="number">
    ///   <item>
    ///     <description>
    ///       It is structurally immutable — <c>Add</c> / <c>Remove</c> operations are not
    ///       available, so no runtime code (including test code) can accidentally or
    ///       maliciously disable redaction for a field.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       Lookup performance is near-zero in this read-heavy, write-never scenario
    ///       thanks to <see cref="FrozenSet{T}"/>'s specialised hash strategy.
    ///     </description>
    ///   </item>
    /// </list>
    /// </para>
    /// </remarks>
    internal static readonly FrozenSet<string> SensitivePropertyNames =
        FrozenSet.ToFrozenSet(
            new[]
            {
                "SecretAccessKey",
                "PasswordHash",
                "TokenHash",
                "Authorization",
                "DATAVIEWER_ENCRYPTION_KEY",
            },
            StringComparer.OrdinalIgnoreCase);

    // ── Reflection cache ──────────────────────────────────────────────────────
    // GetType().GetProperties() is called on every destructured object. In
    // high-throughput scenarios this reflection cost compounds quickly. Caching by
    // Type reduces the steady-state cost to a single ConcurrentDictionary lookup.
    //
    // The cache stores both the PropertyInfo[] array and a pre-computed flag
    // indicating whether the type carries at least one sensitive property, avoiding
    // a second scan of the properties array in ContainsSensitiveProperty on the hot path.
    private static readonly ConcurrentDictionary<Type, (PropertyInfo[] Props, bool HasSensitive)>
        _typeCache = new();

    /// <inheritdoc />
    /// <summary>
    /// Inspects <paramref name="value"/> when it is a structured object (POCO / record)
    /// or an <see cref="IDictionary{TKey, TValue}"/> with <see cref="string"/> keys and
    /// replaces the value of each sensitive property/key with <see cref="RedactedMarker"/>.
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
    /// <see langword="true"/> when the value is a structured object or string-keyed
    /// dictionary that contains at least one sensitive property/key, and
    /// <paramref name="result"/> has been populated;
    /// <see langword="false"/> to let Serilog fall through to the next destructuring policy.
    /// </returns>
    public bool TryDestructure(
        object value,
        ILogEventPropertyValueFactory propertyValueFactory,
        out LogEventPropertyValue? result)
    {
        // Only intercept structured objects (classes / records with public properties).
        // Primitives, strings, enums, and collections delegate to Serilog's built-in policies,
        // except for IDictionary<string, *> which we handle explicitly below.
        if (value is null || value.GetType().IsPrimitive || value is string || value is Enum)
        {
            result = null;
            return false;
        }

        // ── Handle IDictionary<string, *> types (e.g. Dictionary<string, object>,
        // ExpandoObject) containing sensitive keys. Serilog would normally emit
        // these as DictionaryValue, which bypasses the POCO property-reflection path.
        // We intercept them here to redact any entry whose key is sensitive.
        if (TryDestructureDictionary(value, propertyValueFactory, out result))
            return result is not null;

        // ── Reflect on POCO / record types ────────────────────────────────────
        var (properties, hasSensitive) = GetCachedTypeInfo(value.GetType());

        // Short-circuit: skip objects that carry no sensitive properties to avoid
        // allocating the sanitised list on every non-sensitive logged value.
        if (!hasSensitive)
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
    /// Attempts to intercept an <see cref="IDictionary{TKey, TValue}"/> (with
    /// <see cref="string"/> keys) and redact any entry whose key is a sensitive field name.
    /// </summary>
    /// <param name="value">The candidate value to inspect.</param>
    /// <param name="propertyValueFactory">Serilog's property-value factory for recursive destructuring.</param>
    /// <param name="result">
    /// Set to a <see cref="DictionaryValue"/> with sensitive values redacted when the type
    /// matches; otherwise <see langword="null"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="value"/> implements
    /// <see cref="IEnumerable{T}"/> of <see cref="KeyValuePair{TKey, TValue}"/> with
    /// <see cref="string"/> keys (i.e. a string-keyed dictionary), regardless of whether
    /// any sensitive keys were found.  The caller should return <c>result is not null</c>
    /// to Serilog.
    /// </returns>
    private static bool TryDestructureDictionary(
        object value,
        ILogEventPropertyValueFactory propertyValueFactory,
        out LogEventPropertyValue? result)
    {
        // Detect IDictionary<string, TValue> by checking for IEnumerable<KeyValuePair<string, *>>.
        // This covers Dictionary<string, object>, Dictionary<string, string>,
        // ExpandoObject (implements IDictionary<string, object>), and similar types.
        var valueType = value.GetType();

        // Find an IDictionary<string, TValue> interface implementation.
        var dictionaryInterface = valueType
            .GetInterfaces()
            .FirstOrDefault(i =>
                i.IsGenericType &&
                i.GetGenericTypeDefinition() == typeof(IDictionary<,>) &&
                i.GetGenericArguments()[0] == typeof(string));

        if (dictionaryInterface is null)
        {
            result = null;
            return false;
        }

        // Cast to the non-generic IDictionary so we can iterate regardless of value type.
        if (value is not System.Collections.IDictionary dict)
        {
            result = null;
            return false;
        }

        // Check whether any key is sensitive — if not, let Serilog handle it normally.
        bool hasSensitiveKey = false;
        foreach (string? key in dict.Keys)
        {
            if (key is not null && SensitivePropertyNames.Contains(key))
            {
                hasSensitiveKey = true;
                break;
            }
        }

        if (!hasSensitiveKey)
        {
            result = null;
            return false;
        }

        // Rebuild the dictionary as a Serilog DictionaryValue with sensitive values redacted.
        var elements = new List<KeyValuePair<ScalarValue, LogEventPropertyValue>>(dict.Count);
        foreach (System.Collections.DictionaryEntry entry in dict)
        {
            var keyStr = entry.Key?.ToString() ?? string.Empty;
            var keyScalar = new ScalarValue(keyStr);

            LogEventPropertyValue entryValue;

            if (!string.IsNullOrEmpty(keyStr) && SensitivePropertyNames.Contains(keyStr))
            {
                entryValue = new ScalarValue(RedactedMarker);
            }
            else
            {
                try
                {
                    entryValue = propertyValueFactory.CreatePropertyValue(
                        entry.Value, destructureObjects: true);
                }
                catch (Exception)
                {
                    entryValue = new ScalarValue("<unreadable>");
                }
            }

            elements.Add(new KeyValuePair<ScalarValue, LogEventPropertyValue>(keyScalar, entryValue));
        }

        result = new DictionaryValue(elements);
        return true;
    }

    /// <summary>
    /// Returns the cached <see cref="PropertyInfo"/> array and sensitive-field flag
    /// for the given <paramref name="type"/>, computing and caching on first access.
    /// </summary>
    /// <remarks>
    /// Caching eliminates repeated <c>Type.GetProperties()</c> calls on the hot logging
    /// path. In a typical API processing hundreds of requests per second, uncached
    /// reflection would incur thousands of redundant type-system traversals per second.
    /// </remarks>
    private static (PropertyInfo[] Props, bool HasSensitive) GetCachedTypeInfo(Type type)
        => _typeCache.GetOrAdd(type, static t =>
        {
            var props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var hasSensitive = ContainsSensitiveProperty(props);
            return (props, hasSensitive);
        });

    /// <summary>
    /// Returns <see langword="true"/> if any element of <paramref name="properties"/>
    /// has a name that matches a known sensitive field (case-insensitive check).
    /// </summary>
    private static bool ContainsSensitiveProperty(PropertyInfo[] properties)
    {
        // Explicit foreach with early return avoids LINQ allocation on every call.
        foreach (var prop in properties)
        {
            if (SensitivePropertyNames.Contains(prop.Name))
                return true;
        }

        return false;
    }
}
