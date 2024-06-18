using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using FiMAdminApi.Data.Models;
using Event = FiMAdminApi.Clients.Models.Event;

namespace FiMAdminApi.Clients;

public class FrcEventsDataClient : IDataClient
{
    private readonly string _apiKey;
    private readonly Uri _baseUrl;
    private readonly HttpClient _httpClient;

    public FrcEventsDataClient(IServiceProvider sp)
    {
        var configSection = sp.GetRequiredService<IConfiguration>().GetRequiredSection("Clients:FrcEvents");
        
        var apiKey = configSection["ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentNullException(nameof(apiKey));
        _apiKey = apiKey;

        var baseUrl = configSection["BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl)) throw new ArgumentNullException(nameof(baseUrl));
        _baseUrl = new Uri(baseUrl);
        
        _httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("FrcEvents");
    }

    public async Task<Event?> GetEventAsync(Season season, string eventCode)
    {
        var resp = await _httpClient.SendAsync(BuildGetRequest($"{GetSeason(season)}/events", new Dictionary<string, string>
        {
            { "eventCode", eventCode },
        }));
        resp.EnsureSuccessStatusCode();

        using var json = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        return json.RootElement.GetProperty("Events").EnumerateArray().Select(evt =>
        {
            var timeZone =
                TimeZoneInfo.FindSystemTimeZoneById(evt.GetProperty("timezone").GetString() ?? TimeZoneInfo.Utc.Id);
            var startTime = evt.GetProperty("dateStart").GetDateTime();
            var endTime = evt.GetProperty("dateEnd").GetDateTime();

            return new Event
            {
                EventCode = evt.GetProperty("code").GetString() ?? "(No event code)",
                Name = evt.GetProperty("name").GetString() ?? "(No name)",
                DistrictCode = evt.GetProperty("districtCode").GetString(),
                City = evt.GetProperty("city").GetString() ?? "(No city)",
                StartTime = new DateTimeOffset(startTime, timeZone.GetUtcOffset(startTime)),
                EndTime = new DateTimeOffset(endTime, timeZone.GetUtcOffset(endTime))
            };
        }).SingleOrDefault((Event?)null);
    }

    public Task<List<Event>> GetDistrictEventsAsync(Season season, string districtCode)
    {
        throw new NotImplementedException();
    }

    private string GetSeason(Season season)
    {
        return season.StartTime.Year.ToString();
    }

    /// <summary>
    /// Creates a request which encodes all user-provided values
    /// </summary>
    private HttpRequestMessage BuildGetRequest(FormattableString endpoint, Dictionary<string, string>? queryParams = default)
    {
        var request = new HttpRequestMessage();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MediaTypeNames.Application.Json));
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(_apiKey)));

        var relativeUri =
            $"{EncodeString(endpoint, Uri.EscapeDataString)}{(queryParams is not null && queryParams.Count > 0 ? QueryString.Create(queryParams!) : "")}";

        if (relativeUri.StartsWith('/'))
            throw new ArgumentException("Endpoint must be a relative path", nameof(endpoint));
        
        request.RequestUri = new Uri(_baseUrl, relativeUri);

        return request;
    }

    private string EncodeString(FormattableString str, Func<string, string> func)
    {
        var invariantParameters = str.GetArguments()
            .Select(a => FormattableString.Invariant($"{a}"));
        var escapedParameters = invariantParameters
            .Select(func)
            .Cast<object>()
            .ToArray();

        return string.Format(str.Format, escapedParameters);
    }
}