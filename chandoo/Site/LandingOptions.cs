namespace Chandoo.Site;

/// <summary>Settings for the public price pages ("Landing" in appsettings.json).</summary>
public sealed class LandingOptions
{
    public const string SectionName = "Landing";

    /// <summary>The Chandoo app's page on Cafe Bazaar, for the banner's download button; empty hides the button.</summary>
    public string? BazaarUrl { get; set; }

    /// <summary>The Chandoo web app, for the banner's sign-in button; empty hides the button.</summary>
    public string? WebAppUrl { get; set; }

    /// <summary>
    /// The site's public address (e.g. https://chandoo.ir), used for canonical links and the sitemap so every host name
    /// that reaches the app points search engines at one. Empty uses the address the request came in on.
    /// </summary>
    public string? SiteUrl { get; set; }
}
