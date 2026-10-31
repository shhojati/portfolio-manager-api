namespace Chandoo.Api;

/// <summary>
/// Where Chandoo reads prices from ("Api" in appsettings.json): the Portfolio Manager API and an API client account on it
/// (role "API client", created in the API's backoffice under Users).
/// </summary>
public sealed class ApiOptions
{
    public const string SectionName = "Api";

    /// <summary>The API's address, e.g. https://portfolio-api.shahabdev.ir.</summary>
    public string BaseUrl { get; set; } = "";

    public string Username { get; set; } = "";
    public string Password { get; set; } = "";

    /// <summary>How long one request to the API may take before the page gives up.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(20);
}
