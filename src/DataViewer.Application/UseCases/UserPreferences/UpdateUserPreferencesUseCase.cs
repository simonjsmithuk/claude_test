namespace DataViewer.Application.UseCases.UserPreferences;

using DataViewer.Application.DTOs.UserPreferences;
using DataViewer.Application.Interfaces;
using DataViewer.Domain.Exceptions;

/// <summary>
/// Persists or updates a user's UI preferences.
/// </summary>
/// <remarks>
/// <para>
/// <b>Upsert semantics:</b>
/// This use case performs an atomic upsert via
/// <see cref="IUserPreferencesRepository.UpsertAsync"/>. If the user has never saved
/// preferences before (no <see cref="DataViewer.Domain.Entities.UserPreference"/> row exists),
/// a new row is inserted. If a row already exists, all fields are updated in place.
/// </para>
///
/// <para>
/// <b>PreferredProfileId validation:</b>
/// When <see cref="UserPreferenceDto.PreferredProfileId"/> is non-null, this use case
/// validates that a non-deleted <see cref="DataViewer.Domain.Entities.CredentialProfile"/>
/// with that ID exists. If not, <see cref="NotFoundException"/> is thrown. When
/// <see langword="null"/>, no validation is performed — the application falls back to the
/// system-active profile at search time.
/// </para>
///
/// <para>
/// <b>No audit entry:</b>
/// Updating one's own UI preferences is not audited under the current design. Audit-first
/// (ADR-009) applies to operations that change security-sensitive state (credentials, users,
/// system settings) or expose transaction data. UI preference changes are informational and
/// do not affect system security posture.
/// </para>
/// </remarks>
public sealed class UpdateUserPreferencesUseCase
{
    private readonly IUserPreferencesRepository _repository;
    private readonly ICredentialProfileRepository _credentialProfileRepository;

    public UpdateUserPreferencesUseCase(
        IUserPreferencesRepository repository,
        ICredentialProfileRepository credentialProfileRepository)
    {
        _repository = repository;
        _credentialProfileRepository = credentialProfileRepository;
    }

    /// <summary>
    /// Upserts the UI preferences for the authenticated user.
    /// </summary>
    /// <param name="userId">
    /// The <see cref="DataViewer.Domain.Entities.User.Id"/> of the authenticated user,
    /// extracted from the JWT access token by the API layer.
    /// </param>
    /// <param name="request">
    /// The new preference values. All fields must be present — this is a full-replacement
    /// upsert, not a partial update.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    /// <returns>
    /// A <see cref="UserPreferenceDto"/> representing the persisted preferences.
    /// The returned DTO is identical to <paramref name="request"/> (no server-managed fields
    /// are added).
    /// </returns>
    /// <exception cref="NotFoundException">
    /// Thrown when <see cref="UserPreferenceDto.PreferredProfileId"/> is non-null and no
    /// non-deleted credential profile with that ID exists.
    /// </exception>
    public async Task<UserPreferenceDto> ExecuteAsync(
        Guid userId,
        UserPreferenceDto request,
        CancellationToken cancellationToken)
    {
        // Validate PreferredProfileId if specified
        if (request.PreferredProfileId.HasValue)
        {
            var profile = await _credentialProfileRepository.GetByIdAsync(
                request.PreferredProfileId.Value,
                cancellationToken);

            if (profile is null || profile.IsDeleted)
            {
                throw new NotFoundException(
                    $"Credential profile with ID '{request.PreferredProfileId.Value}' not found.");
            }
        }

        // Map DTO to domain entity
        var preferences = new DataViewer.Domain.Entities.UserPreference
        {
            UserId = userId,
            DefaultPageSize = request.DefaultPageSize,
            DefaultDateRangeDays = request.DefaultDateRangeDays,
            PreferredProfileId = request.PreferredProfileId
        };

        // Upsert (insert if new, update if exists)
        await _repository.UpsertAsync(preferences, cancellationToken);

        // Return the persisted preferences (no server-managed fields to merge back)
        return request;
    }
}
