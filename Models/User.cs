namespace PortfolioManager.Api.Models;

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty; // compared case-insensitively
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty; // see Auth.Roles
    public string SecurityStamp { get; set; } = NewSecurityStamp(); // changes on password change, signing out existing sessions
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }

    public static string NewSecurityStamp() => Guid.NewGuid().ToString("N");
}
