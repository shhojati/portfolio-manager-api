namespace PortfolioManager.Api.Dtos;

public record InstallTokenResponse(string Token, DateTimeOffset ExpiresAt);
