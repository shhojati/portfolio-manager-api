namespace PortfolioManager.Api.Dtos;

/// <summary>An asset's latest price with its change and recent trend, for price boards such as the Chandoo site.</summary>
/// <param name="Date">The day of the latest price (Tehran calendar day, at midnight UTC).</param>
/// <param name="ChangePercent">Change from the asset's previous stored price, in percent; null when it has only one.</param>
/// <param name="Trend">The asset's last few prices, oldest first, ending with <paramref name="Value"/>.</param>
public record BoardItemDto(
    string Identifier,
    string Symbol,
    string Name,
    string Type,
    string? Unit,
    decimal Value,
    DateTime Date,
    decimal? ChangePercent,
    IReadOnlyList<decimal> Trend);

public record BoardPageDto(int Total, int Page, int PageCount, IReadOnlyList<BoardItemDto> Items);
