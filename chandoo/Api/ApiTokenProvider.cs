using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace Chandoo.Api;

/// <summary>
/// Signs in to the API with the configured API client account (POST /api/auth/token) and keeps the access token until a
/// minute before it expires. One token is shared by every request.
/// </summary>
public sealed class ApiTokenProvider(IHttpClientFactory httpClients, IOptions<ApiOptions> options, ILogger<ApiTokenProvider> logger)
{
    public const string ClientName = "api-auth";

    private readonly SemaphoreSlim gate = new(1, 1);
    private string? token;
    private DateTimeOffset expires;

    private sealed record TokenResponse(string AccessToken, int ExpiresIn);

    public async Task<string> GetAsync(CancellationToken ct)
    {
        if (token is not null && DateTimeOffset.UtcNow < expires)
            return token;

        await gate.WaitAsync(ct);
        try
        {
            if (token is not null && DateTimeOffset.UtcNow < expires)
                return token;

            var http = httpClients.CreateClient(ClientName);
            using var response = await http.PostAsJsonAsync("api/auth/token",
                new { username = options.Value.Username, password = options.Value.Password }, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("The API refused to sign in API client {Username}: {Status}", options.Value.Username, (int)response.StatusCode);
                response.EnsureSuccessStatusCode();
            }
            var body = await response.Content.ReadFromJsonAsync<TokenResponse>(ct)
                ?? throw new HttpRequestException("The API's token response was empty.");
            token = body.AccessToken;
            expires = DateTimeOffset.UtcNow.AddSeconds(Math.Max(0, body.ExpiresIn - 60));
            return token;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Forgets the token, after the API refused it (e.g. the account's password changed).</summary>
    public void Reset() => token = null;
}

/// <summary>Adds the API client's bearer token to each request, signing in again once if the API refuses it.</summary>
public sealed class ApiTokenHandler(ApiTokenProvider tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetAsync(ct));
        var response = await base.SendAsync(request, ct);
        if (response.StatusCode != System.Net.HttpStatusCode.Unauthorized)
            return response;

        // Requests here are GETs without a body, so sending one again is safe.
        response.Dispose();
        tokens.Reset();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetAsync(ct));
        return await base.SendAsync(request, ct);
    }
}
