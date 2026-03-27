namespace DataViewer.Application.UseCases.CredentialProfiles;

using DataViewer.Application.DTOs.CredentialProfiles;
using DataViewer.Application.Interfaces;

/// <summary>
/// Retrieves all non-deleted AWS credential profiles for Admin management purposes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Security enforcement:</b>
/// The mapping layer that projects from <see cref="DataViewer.Domain.Entities.CredentialProfile"/>
/// domain entities to <see cref="CredentialProfileDto"/> DTOs is the sole enforcement point
/// for the rule that <see cref="DataViewer.Domain.Entities.CredentialProfile.EncryptedSecretKey"/>
/// MUST NEVER be exposed in any API response (Product Spec § G-04). This use case performs
/// that mapping correctly, omitting the secret key from all returned DTOs.
/// </para>
///
/// <para>
/// <b>Soft-delete filtering:</b>
/// By default, soft-deleted profiles (<see cref="DataViewer.Domain.Entities.CredentialProfile.IsDeleted"/>
/// = <see langword="true"/>) are excluded from the result set. The repository's
/// <see cref="ICredentialProfileRepository.GetAllAsync"/> method handles this filtering.
/// </para>
/// </remarks>
public sealed class GetCredentialProfilesUseCase
{
    private readonly ICredentialProfileRepository _repository;

    public GetCredentialProfilesUseCase(ICredentialProfileRepository repository)
    {
        _repository = repository;
    }

    /// <summary>
    /// Retrieves all non-deleted credential profiles.
    /// </summary>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    /// <returns>
    /// A read-only list of <see cref="CredentialProfileDto"/> records, one per non-deleted
    /// profile, ordered by <see cref="DataViewer.Domain.Entities.CredentialProfile.CreatedAt"/>
    /// ascending. Returns an empty list when no profiles exist.
    /// The AWS Secret Access Key is NOT included in any of the returned DTOs.
    /// </returns>
    public async Task<IReadOnlyList<CredentialProfileDto>> ExecuteAsync(
        CancellationToken cancellationToken)
    {
        // Retrieve all non-deleted profiles from repository
        var profiles = await _repository.GetAllAsync(
            includeDeleted: false,
            cancellationToken);

        // Map each entity to DTO WITHOUT exposing the secret key
        return profiles
            .Select(MapToDto)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Maps a <see cref="DataViewer.Domain.Entities.CredentialProfile"/> domain entity
    /// to a <see cref="CredentialProfileDto"/> for API responses.
    /// </summary>
    /// <remarks>
    /// <b>Security enforcement point:</b> This method deliberately omits the
    /// <see cref="DataViewer.Domain.Entities.CredentialProfile.EncryptedSecretKey"/> field
    /// from the output DTO. The AWS Secret Access Key is NEVER returned in any API response
    /// under any circumstances (Product Spec § G-04).
    /// </remarks>
    private static CredentialProfileDto MapToDto(DataViewer.Domain.Entities.CredentialProfile profile)
    {
        return new CredentialProfileDto
        {
            Id = profile.Id,
            Name = profile.Name,
            AccessKeyId = profile.AccessKeyId,
            Region = profile.Region,
            BucketName = profile.BucketName,
            KeyPrefix = profile.KeyPrefix,
            IsActive = profile.IsActive,
            IsDeleted = profile.IsDeleted,
            CreatedAt = new DateTimeOffset(
                DateTime.SpecifyKind(profile.CreatedAt, DateTimeKind.Utc),
                TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(
                DateTime.SpecifyKind(profile.UpdatedAt, DateTimeKind.Utc),
                TimeSpan.Zero)
        };
    }
}
