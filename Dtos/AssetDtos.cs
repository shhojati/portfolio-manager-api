using System.ComponentModel.DataAnnotations;

namespace PortfolioManager.Api.Dtos;

public record AssetDto(int Id, string Symbol, string Identifier, string Name, string Type, DateTime CreatedAt);

public record AssetRequest(
    [Required, MaxLength(20)] string Symbol,
    [Required, MaxLength(100)] string Identifier,
    [Required, MaxLength(100)] string Name,
    [MaxLength(50)] string Type);
