using System.Net.Http.Json;

namespace Catalog.Infrastructure.ExternalService;

public class QuickLinkerService(HttpClient httpClient, IOptions<CatalogOptions> options)
{
    private readonly IOptions<CatalogOptions> _options = options;
    public async Task<string> GetShortURL(string longUrl)
    {
        var shortenerServiceURL = _options.Value.QuickLinkerOptions.URL;
        var url = new Uri(shortenerServiceURL);

        var body = new
        {
            originalURL = longUrl
        };

        var response = await httpClient.PostAsJsonAsync(url.AbsoluteUri, body);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync();
    }
}
