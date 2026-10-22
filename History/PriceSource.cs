using PortfolioManager.Api.Bitpin;
using PortfolioManager.Api.Fipiran;
using PortfolioManager.Api.Ime;
using PortfolioManager.Api.Tgju;
using PortfolioManager.Api.Tsetmc;

namespace PortfolioManager.Api.History;

/// <summary>Where an asset's prices come from: the price sync job that imported it.</summary>
public enum PriceSource
{
    /// <summary>Added by hand, or by a source that's no longer synced; its past prices can't be fetched.</summary>
    None,
    Tsetmc,
    Bitpin,
    Tgju,
    Ime,
    Fipiran,
}

public static class PriceSources
{
    /// <summary>The source of an asset, from the type and identifier its sync job gave it.</summary>
    public static PriceSource Of(string type, string identifier, string? tsetmcInsCode) => type switch
    {
        BitpinPriceSyncService.CryptoType => PriceSource.Bitpin,
        FipiranPriceSyncService.FundType => PriceSource.Fipiran,
        // IME's continuous certificates share the Certificate type with TSETMC's, but have no TSETMC code.
        TsetmcPriceSyncService.CertificateType when tsetmcInsCode is null && ImeCertificates.ByCode(identifier) is not null => PriceSource.Ime,
        TsetmcPriceSyncService.StockType or TsetmcPriceSyncService.EtfType or TsetmcPriceSyncService.CertificateType
            or TsetmcPriceSyncService.SalafType or TsetmcDerivativesSyncService.OptionType
            or TsetmcDerivativesSyncService.FutureType when tsetmcInsCode is not null => PriceSource.Tsetmc,
        _ when TgjuInstruments.KeyOf(type, identifier) is not null => PriceSource.Tgju,
        _ => PriceSource.None,
    };
}
