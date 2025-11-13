using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FiMAdminApi.Services;

public class TwitchService(IConfiguration configuration, ILogger<EventStreamService> logger)
{
    // simple in-memory cache for the app access token
    private static string? _cachedAccessToken;
    private static DateTimeOffset _cachedAccessTokenExpiry = DateTimeOffset.MinValue;

    // Get an access token for simple basic API access 
    public async Task<string> GetAccessToken()
    {
        var clientId = configuration["Twitch:ClientId"];
        var clientSecret = configuration["Twitch:ClientSecret"];

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            logger.LogError("Twitch configuration missing (ClientId or ClientSecret).");
            throw new InvalidOperationException("Twitch configuration incomplete.");
        }

        // return cached token when still valid
        if (!string.IsNullOrWhiteSpace(_cachedAccessToken) && DateTimeOffset.UtcNow < _cachedAccessTokenExpiry)
        {
            return _cachedAccessToken!;
        }

        using var http = new HttpClient();

        // Prepare form content as application/x-www-form-urlencoded
        var form = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("client_id", clientId),
            new KeyValuePair<string, string>("client_secret", clientSecret),
            new KeyValuePair<string, string>("grant_type", "client_credentials")
        };

        using var content = new FormUrlEncodedContent(form);

        var tokenUrl = "https://id.twitch.tv/oauth2/token";

        HttpResponseMessage resp;
        try
        {
            resp = await http.PostAsync(tokenUrl, content);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error while requesting Twitch access token.");
            throw;
        }

        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            logger.LogError("Twitch token request failed. Status: {Status}. Body: {Body}", resp.StatusCode, body);
            throw new Exception($"Twitch token request failed: {resp.StatusCode}");
        }

        try
        {
            var token = JsonSerializer.Deserialize<TwitchAuthToken>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (token is null || string.IsNullOrWhiteSpace(token.AccessToken))
            {
                logger.LogError("Twitch token response parsing failed. Body: {Body}", body);
                throw new Exception("Invalid token response from Twitch.");
            }

            // Cache the token with a small safety margin (e.g., 60s)
            var expiresIn = token.ExpiresIn > 60 ? token.ExpiresIn - 60 : token.ExpiresIn;
            _cachedAccessToken = token.AccessToken;
            _cachedAccessTokenExpiry = DateTimeOffset.UtcNow.AddSeconds(expiresIn);

            return _cachedAccessToken;
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed parsing Twitch token response. Body: {Body}", body);
            throw;
        }
    }

    public string GetBroadcasterId(FiMTwitchChannel channel)
    {
        var bid = channel switch
        {
            FiMTwitchChannel.FiMVideo2 => configuration["Twitch:FIRSTinMI02"] ?? string.Empty,
            FiMTwitchChannel.FiMVideo3 => configuration["Twitch:FIRSTinMI03"] ?? string.Empty,
            FiMTwitchChannel.FiMVideo4 => configuration["Twitch:FIRSTinMI04"] ?? string.Empty,
            FiMTwitchChannel.FiMVideo5 => configuration["Twitch:FIRSTinMI05"] ?? string.Empty,
            _ => string.Empty,
        };

        if (string.IsNullOrWhiteSpace(bid))
        {
            return string.Empty;
        }

        // if the value already looks like an id (digits only), return it directly
        var isDigits = true;
        foreach (var ch in bid)
        {
            if (!char.IsDigit(ch))
            {
                isDigits = false;
                break;
            }
        }
        if (isDigits)
        {
            return bid;
        }

        try
        {
            var clientId = configuration["Twitch:ClientId"];
            var accessToken = GetAccessToken().GetAwaiter().GetResult();

            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(accessToken))
            {
                logger.LogWarning("Twitch ClientId or AccessToken missing when resolving broadcaster id for login {Login}.", bid);
                return string.Empty;
            }

            using var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            http.DefaultRequestHeaders.Add("Client-Id", clientId);

            var url = $"https://api.twitch.tv/helix/users?login={Uri.EscapeDataString(bid)}";
            var resp = http.GetAsync(url).GetAwaiter().GetResult();
            var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            if (!resp.IsSuccessStatusCode)
            {
                logger.LogError("Failed fetching Twitch user for login {Login}. Status: {Status}. Body: {Body}", bid, resp.StatusCode, body);
                return string.Empty;
            }

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0)
            {
                var idProp = data[0].GetProperty("id");
                return idProp.GetString() ?? string.Empty;
            }

            logger.LogWarning("No Twitch user found for login {Login}. Body: {Body}", bid, body);
            return string.Empty;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error resolving Twitch broadcaster id for login {Login}.", bid);
            return string.Empty;
        }
    }

    public async Task UpdateLivestreamInformation(string name, FiMTwitchChannel channel)
    {
        var clientId = configuration["Twitch:ClientId"];
        var accessToken = await GetAccessToken();
        var broadcasterId = GetBroadcasterId(channel);

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(broadcasterId))
        {
            logger.LogError("Twitch configuration missing (ClientId, AccessToken or BroadcasterId).");
            throw new InvalidOperationException("Twitch configuration incomplete.");
        }

        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        http.DefaultRequestHeaders.Add("Client-Id", clientId);

        // Update stream title
        try
        {
            var helixUrl = $"https://api.twitch.tv/helix/channels?broadcaster_id={broadcasterId}";
            var helixPayload = JsonSerializer.Serialize(new { title = name });
            using var helixReq = new HttpRequestMessage(HttpMethod.Patch, helixUrl)
            {
                Content = new StringContent(helixPayload, Encoding.UTF8, "application/json")
            };

            var helixResp = await http.SendAsync(helixReq);
            if (!helixResp.IsSuccessStatusCode)
            {
                var body = await helixResp.Content.ReadAsStringAsync();
                logger.LogError("Failed updating Twitch title. Status: {Status}. Body: {Body}", helixResp.StatusCode, body);
                throw new Exception($"Twitch Helix update failed: {helixResp.StatusCode}");
            }

            logger.LogInformation("Twitch title updated.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error while updating Twitch title.");
            throw;
        }
    }

    // Exchange an authorization code (from the redirect) for an access token + refresh token
    public async Task<TwitchAuthToken> ExchangeCodeForTokenAsync(string code, string redirectUri)
    {
        var clientId = configuration["Twitch:ClientId"];
        var clientSecret = configuration["Twitch:ClientSecret"];

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            logger.LogError("Twitch configuration missing (ClientId or ClientSecret). Cannot exchange code for token.");
            throw new InvalidOperationException("Twitch configuration incomplete.");
        }

        using var http = new HttpClient();

        var form = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("client_id", clientId),
            new KeyValuePair<string, string>("client_secret", clientSecret),
            new KeyValuePair<string, string>("code", code),
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("redirect_uri", redirectUri)
        };

        using var content = new FormUrlEncodedContent(form);

        var tokenUrl = "https://id.twitch.tv/oauth2/token";

        HttpResponseMessage resp;
        try
        {
            resp = await http.PostAsync(tokenUrl, content);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error while exchanging authorization code for Twitch token.");
            throw;
        }

        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            logger.LogError("Twitch token exchange failed. Status: {Status}. Body: {Body}", resp.StatusCode, body);
            throw new Exception($"Twitch token exchange failed: {resp.StatusCode}");
        }

        try
        {
            var token = JsonSerializer.Deserialize<TwitchAuthToken>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (token is null || string.IsNullOrWhiteSpace(token.AccessToken))
            {
                logger.LogError("Twitch token exchange response parsing failed. Body: {Body}", body);
                throw new Exception("Invalid token response from Twitch.");
            }

            return token;
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed parsing Twitch token exchange response. Body: {Body}", body);
            throw;
        }
    }


    public sealed class TwitchAuthToken
    {
        public string AccessToken { get; init; } = string.Empty;
        public string RefreshToken { get; init; } = string.Empty;
        public int ExpiresIn { get; init; }
        public string[] Scope { get; init; } = Array.Empty<string>();
        public string TokenType { get; init; } = string.Empty;
    }
}

public enum FiMTwitchChannel
{
    FiMVideo1,
    FiMVideo2,
    FiMVideo3,
    FiMVideo4,
    FiMVideo5,
    FiMVideo6,
    FiMVideo7,
}