using System.ComponentModel.DataAnnotations;

namespace DataViewer.Application.DTOs.Auth;

/// <summary>
/// Request payload for the POST /api/auth/login endpoint.
/// </summary>
public sealed record LoginRequestDto
{
    /// <summary>
    /// The unique login name of the user attempting to authenticate.
    /// Case-insensitive; normalised to lower-case at the application layer.
    /// </summary>
    [Required(ErrorMessage = "Username is required.")]
    [MaxLength(100, ErrorMessage = "Username must not exceed 100 characters.")]
    public string UserName { get; init; } = string.Empty;

    /// <summary>
    /// The plain-text password supplied by the user.
    /// Validated against the stored bcrypt hash — never logged or persisted.
    /// </summary>
    /// <remarks>
    /// <c>[MinLength(1)]</c> is applied in addition to <c>[Required]</c> so that an
    /// empty-string password is rejected at the HTTP layer before reaching the bcrypt
    /// comparison service, avoiding a full bcrypt round-trip on clearly invalid input.
    /// Note: the <c>= string.Empty</c> default means a missing body field produces
    /// an empty string rather than <see langword="null"/>. The <c>[Required]</c> and
    /// <c>[MinLength(1)]</c> annotations together ensure this is rejected as invalid
    /// — provided <c>[ApiController]</c> is applied to the controller so that automatic
    /// model-state validation returns HTTP 400 before handler execution.
    /// </remarks>
    [Required(ErrorMessage = "Password is required.")]
    [MinLength(1, ErrorMessage = "Password must not be empty.")]
    [MaxLength(200, ErrorMessage = "Password must not exceed 200 characters.")]
    public string Password { get; init; } = string.Empty;
}
