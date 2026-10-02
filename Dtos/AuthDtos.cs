using System.ComponentModel.DataAnnotations;
using PortfolioManager.Api.Auth;

namespace PortfolioManager.Api.Dtos;

public record UserDto(int Id, string Username, string Role, DateTime CreatedAt, DateTime? LastLoginAt);

public record LoginRequest([Required] string Username, [Required] string Password, bool RememberMe = false);

public record TokenRequest([Required] string Username, [Required] string Password);

public record RefreshRequest([Required] string RefreshToken);

public record ChangePasswordRequest(
    [Required] string CurrentPassword,
    [Required, MinLength(UserService.MinPasswordLength, ErrorMessage = UserService.PasswordTooShort), MaxLength(128)] string NewPassword);

public record CreateUserRequest(
    [Required, MaxLength(50), RegularExpression(@"^[A-Za-z0-9._-]+$", ErrorMessage = "Username may only contain letters, digits, '.', '_' and '-'.")] string Username,
    [Required, MinLength(UserService.MinPasswordLength, ErrorMessage = UserService.PasswordTooShort), MaxLength(128)] string Password,
    [Required] string Role);

public record SetPasswordRequest([Required, MinLength(UserService.MinPasswordLength, ErrorMessage = UserService.PasswordTooShort), MaxLength(128)] string Password);
