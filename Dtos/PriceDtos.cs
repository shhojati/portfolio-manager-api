using System.ComponentModel.DataAnnotations;

namespace PortfolioManager.Api.Dtos;

public record PriceDto(int Id, int AssetId, decimal Value, DateTime Date);

public record PriceRequest(
    [Range(1, int.MaxValue)] int AssetId,
    [Range(typeof(decimal), "0", "79228162514264337593543950335")] decimal Value,
    DateTime Date);
