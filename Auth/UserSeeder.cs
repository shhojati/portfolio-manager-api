using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PortfolioManager.Api.Data;
using PortfolioManager.Api.Models;

namespace PortfolioManager.Api.Auth;

/// <summary>Creates the users listed under "Auth:Users" that don't exist yet.</summary>
public static class UserSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var hasher = services.GetRequiredService<IPasswordHasher<User>>();
        var options = services.GetRequiredService<IOptions<AuthOptions>>().Value;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(UserSeeder));

        foreach (var seed in options.Users)
        {
            var username = seed.Username.Trim();
            if (username.Length == 0)
                continue;

            if (await db.Users.AnyAsync(u => u.Username == username))
                continue;

            if (!Roles.IsValid(seed.Role))
            {
                logger.LogError("Not creating user {Username}: role '{Role}' is not one of {Roles}", username, seed.Role, string.Join(", ", Roles.All));
                continue;
            }
            if (seed.Password.Length < UserService.MinPasswordLength)
            {
                logger.LogError("Not creating user {Username}: the password must be at least {Length} characters", username, UserService.MinPasswordLength);
                continue;
            }

            var user = new User { Username = username, Role = seed.Role };
            user.PasswordHash = hasher.HashPassword(user, seed.Password);
            db.Users.Add(user);
            await db.SaveChangesAsync();
            logger.LogInformation("Created {Role} user {Username}", user.Role, user.Username);
        }

        if (!await db.Users.AnyAsync(u => u.Role == Roles.Admin))
            logger.LogWarning("No admin user exists, so nobody can sign in to the backoffice. Configure one under Auth:Users (e.g. Auth__Users__0__Username/Password/Role).");
    }
}
