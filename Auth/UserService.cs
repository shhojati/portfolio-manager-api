using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PortfolioManager.Api.Data;
using PortfolioManager.Api.Models;

namespace PortfolioManager.Api.Auth;

public sealed class UserService(AppDbContext db, IPasswordHasher<User> hasher)
{
    public const int MinPasswordLength = 10;
    public const string PasswordTooShort = "The password must be at least 10 characters.";
    public const string StampClaim = "stamp";

    // Verified against when the username doesn't exist, so a miss takes as long as a wrong password.
    private static string? _dummyHash;

    /// <summary>Returns the user if the credentials are valid, recording the sign-in.</summary>
    public async Task<User?> VerifyAsync(string username, string password)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Username == username.Trim());
        if (user is null)
        {
            _dummyHash ??= hasher.HashPassword(new User(), "not-a-real-password");
            hasher.VerifyHashedPassword(new User(), _dummyHash, password);
            return null;
        }

        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed)
            return null;
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
            user.PasswordHash = hasher.HashPassword(user, password);

        user.LastLoginAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return user;
    }

    public bool CheckPassword(User user, string password) =>
        hasher.VerifyHashedPassword(user, user.PasswordHash, password) != PasswordVerificationResult.Failed;

    /// <summary>Sets a new password and rotates the security stamp, which signs out the user's sessions.</summary>
    public void SetPassword(User user, string password)
    {
        user.PasswordHash = hasher.HashPassword(user, password);
        user.SecurityStamp = User.NewSecurityStamp();
    }

    /// <summary>The user behind a principal, or null if they were deleted or their password changed since.</summary>
    public async Task<User?> FindCurrentAsync(ClaimsPrincipal? principal)
    {
        if (!int.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
            return null;

        var stamp = principal!.FindFirstValue(StampClaim);
        var user = await db.Users.FindAsync(id);
        return user is not null && user.SecurityStamp == stamp ? user : null;
    }

    public static ClaimsPrincipal CreatePrincipal(User user, string scheme) =>
        new(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim(StampClaim, user.SecurityStamp),
            ],
            scheme));
}
