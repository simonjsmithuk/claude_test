using System.ComponentModel.DataAnnotations;

namespace DataViewer.Application.DTOs.CredentialProfiles;

/// <summary>
/// Request payload for updating an existing credential profile (Admin only).
/// Sent as the body of PUT /api/credential-profiles/{id}.
/// </summary>
/// <remarks>
/// All fields are optional to support partial updates. Only non-null fields are
/// applied by the application layer — a <see langword="null"/> value means
/// "do not change this field".
/// <para>
/// <see cref="SecretAccessKey"/> when supplied causes the server to re-encrypt the
/// new key value and overwrite the stored ciphertext. When <see langword="null"/>,
/// the existing encrypted key is left unchanged (Product Spec § G-04).
/// </para>
/// </remarks>
public sealed record UpdateCredentialProfileDto
{
    /// <summary>
    /// Updated display name for the profile. <see langword="null"/> = leave unchanged.
    /// </summary>
    [MaxLength(100, ErrorMessage = "Profile name must not exceed 100 characters.")]
    public string? Name { get; init; }

    /// <summary>
    /// Updated AWS IAM Access Key ID. <see langword="null"/> = leave unchanged.
    /// </summary>
    [MaxLength(128, ErrorMessage = "Access Key ID must not exceed 128 characters.")]
    public string? AccessKeyId { get; init; }

    /// <summary>
    /// Updated AWS Secret Access Key. When non-null the new value is encrypted and
    /// replaces the stored ciphertext. When <see langword="null"/> the existing
    /// encrypted key is preserved unchanged.
    /// This field is NEVER present in any API response (Product Spec § G-04).
    /// </summary>
    [MaxLength(512, ErrorMessage = "Secret Access Key must not exceed 512 characters.")]
    public string? SecretAccessKey { get; init; }

    /// <summary>
    /// Updated AWS region code. <see langword="null"/> = leave unchanged.
    /// </summary>
    [MaxLength(50, ErrorMessage = "Region must not exceed 50 characters.")]
    public string? Region { get; init; }

    /// <summary>
    /// Updated S3 bucket name. <see langword="null"/> = leave unchanged.
    /// </summary>
    /// <remarks>
    /// ⚠️ Nullable + MinLength interaction: <c>[MinLength(3)]</c> is intentionally
    /// applied to a <see langword="nullable"/> <see cref="string"/>. DataAnnotations
    /// does NOT evaluate <c>[MinLength]</c> when the value is <see langword="null"/>
    /// (the "leave unchanged" semantic), so <see langword="null"/> correctly bypasses
    /// the length constraint. The constraint fires only when a non-null, non-empty
    /// string shorter than 3 characters is supplied — which is the exact invalid-update
    /// case we want to reject. This three-state behaviour (null = no-op, "" or short
    /// string = invalid, valid string = update) is intentional and correct.
    /// </remarks>
    [MinLength(3, ErrorMessage = "Bucket name must be at least 3 characters.")]
    [MaxLength(63, ErrorMessage = "Bucket name must not exceed 63 characters.")]
    public string? BucketName { get; init; }

    /// <summary>
    /// Updated S3 key prefix. Supply an empty string to clear the prefix.
    /// <see langword="null"/> = leave unchanged.
    /// </summary>
    /// <remarks>
    /// Three-state encoding: <see langword="null"/> = no-op (leave prefix as-is),
    /// <c>""</c> (empty string) = clear the prefix, non-empty string = set new prefix.
    /// The application layer handler is responsible for distinguishing these three
    /// states — a <see langword="null"/> JSON field and an omitted JSON field are
    /// both deserialised to <see langword="null"/> by <c>System.Text.Json</c> and
    /// must be treated identically as "no change".
    /// </remarks>
    [MaxLength(1024, ErrorMessage = "Key prefix must not exceed 1024 characters.")]
    public string? KeyPrefix { get; init; }

    /// <summary>
    /// When <see langword="true"/>, this profile is set as the active default;
    /// any previously active profile is deactivated in the same transaction.
    /// When <see langword="false"/> or <see langword="null"/>, the active state
    /// of this profile is left unchanged.
    /// </summary>
    public bool? SetAsActive { get; init; }
}
