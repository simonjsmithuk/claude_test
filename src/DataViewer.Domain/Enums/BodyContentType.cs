#nullable enable

namespace DataViewer.Domain.Enums;

/// <summary>
/// Categorises the detected content type of a decompressed S3 object body.
/// Used to select the appropriate syntax-highlighting renderer in the frontend.
/// </summary>
/// <remarks>
/// Integer values are persisted to the database — do not reorder or reassign.
/// </remarks>
public enum BodyContentType
{
    /// <summary>Body content is valid JSON.</summary>
    Json = 0,

    /// <summary>Body content is valid XML.</summary>
    Xml = 1,

    /// <summary>Body content is plain text or an unrecognised format.</summary>
    Text = 2
}
