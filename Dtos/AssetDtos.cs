using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using PortfolioManager.Api.Models;

namespace PortfolioManager.Api.Dtos;

public record AssetDto(int Id, string Symbol, string Identifier, string Name, string Type, DateTime CreatedAt, string? Unit,
    ContractDto? Contract);

/// <summary>
/// An option's or future's terms; null for other assets. Its prices are per unit of the underlying, so one contract is
/// worth price × ContractSize.
/// </summary>
public record ContractDto(
    string Kind, // Call, Put or Future
    decimal? StrikePrice,
    DateOnly ExpiryDate,
    int ContractSize,
    int? UnderlyingAssetId,
    string UnderlyingSymbol);

public record AssetRequest(
    [Required, MaxLength(100)] string Symbol,
    [Required, MaxLength(100)] string Identifier,
    [Required, MaxLength(100)] string Name,
    [MaxLength(50)] string Type);

public static class AssetDtoMapping
{
    /// <summary>For queries: EF Core translates it to SQL, joining the contract only where there is one.</summary>
    public static readonly Expression<Func<Asset, AssetDto>> Projection = a => new AssetDto(
        a.Id, a.Symbol, a.Identifier, a.Name, a.Type, a.CreatedAt, a.Unit,
        a.Contract == null
            ? null
            : new ContractDto(a.Contract.Kind, a.Contract.StrikePrice, a.Contract.ExpiryDate, a.Contract.ContractSize,
                a.Contract.UnderlyingAssetId, a.Contract.UnderlyingSymbol));

    /// <summary>For an asset in memory; its contract must be loaded.</summary>
    public static readonly Func<Asset, AssetDto> ToDto = Projection.Compile();
}
