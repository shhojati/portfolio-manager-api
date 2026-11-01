using System.ComponentModel.DataAnnotations;

namespace PortfolioManager.Api.Dtos;

public record FeeRateDto(int Id, DateOnly EffectiveFrom, decimal BuyPercent, decimal SellPercent);

/// <summary>A kind of asset and its rates, oldest first: each applies from its day until the next one's.</summary>
public record FeeClassDto(string Key, string Label, List<FeeRateDto> Rates);

public record FeeRateRequest(
    [Required] string Class,
    [Required] DateOnly EffectiveFrom,
    [Range(0, 99.9999)] decimal BuyPercent,
    [Range(0, 99.9999)] decimal SellPercent);
