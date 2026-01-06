using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using VattenfallDynamicPriceApi.Extensions;
using VattenfallDynamicPriceApi.Models.Evcc;
using VattenfallDynamicPriceApi.Models.Vattenfall;

namespace VattenfallDynamicPriceApi.Services;

public partial class VattenfallDataService
{
	private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);

	public FlexTariffData[]? Data { get; private set; } = [];
	public EvccApiHourlyData[]? EvccData { get; private set; } = [];

	public async Task InitializeAsync()
	{
		Console.WriteLine("Refresh interval: " + CacheDuration);
		
		await UpdateDataAsync();
		_ = new Timer(RefreshTimerElapsed, null, CacheDuration, CacheDuration);
	}

	private void RefreshTimerElapsed(object? _)
	{
		try
		{
			Console.WriteLine("Updating data");
			Task.Run(UpdateDataAsync).Wait();
			Console.WriteLine("Updated data");
		}
		catch (Exception e)
		{
			Console.WriteLine("Failed to update data: " + e);
		}
	}

	private async Task UpdateDataAsync()
	{
		var (apiBaseUrl, apiKey) = await TryGetApiUrlAndKeyAsync();
		Data = await GetFlexTariffDataAsync(apiBaseUrl, apiKey);
		
		var electricityData = Data.FirstOrDefault(d => d.Product == "E");
		if (electricityData == null)
		{
			Console.WriteLine("Could not find electricity data in API response");
			return;
		}
		
		EvccData = electricityData.TariffData.Select(td => new EvccApiHourlyData
			{
				Start = td.StartTime.ToUtcKeepTimeAsIs(),
				End = td.EndTime.ToUtcKeepTimeAsIs(),
				Value = td.AmountInclVat * 100 // Values are in cents
			})
			.OrderBy(td => td.Start)
			.ToArray();
	}

	private static async Task<FlexTariffData[]> GetFlexTariffDataAsync(string apiBaseUrl, string apiKey)
	{
		var apiEndpoint = apiBaseUrl + "/DynamicTariff";
		var apiRequest = new HttpRequestMessage(HttpMethod.Get, apiEndpoint);
		apiRequest.Headers.Add("ocp-apim-subscription-key", apiKey);

		using var httpClient = new HttpClient();
		var apiResponse = await httpClient.SendAsync(apiRequest);
		apiResponse.EnsureSuccessStatusCode();

		var jsonData = await apiResponse.Content.ReadAsStringAsync();
		return JsonSerializer.Deserialize(jsonData, SourceGenerationContext.Default.FlexTariffDataArray)!;
	}

	private static async Task<(string apiBaseUrl, string apiKey)> TryGetApiUrlAndKeyAsync()
	{
		if (SettingsProvider.Instance.Settings.UseKnownValues)
			return (SettingsProvider.Instance.Settings.KnownApiBaseUrl, SettingsProvider.Instance.Settings.KnownApiKey);

		try
		{
			return await GetApiUrlAndKeyAsync();
		}
		catch (Exception e)
		{
			Console.WriteLine("Failed to get API URL and key dynamically, falling back to known values: " + e);
			return (SettingsProvider.Instance.Settings.KnownApiBaseUrl, SettingsProvider.Instance.Settings.KnownApiKey);
		}
	}

	private static async Task<(string apiBaseUrl, string apiKey)> GetApiUrlAndKeyAsync()
	{
		var handler = new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
		};

		using var httpClient = new HttpClient(handler);
		httpClient.Timeout = TimeSpan.FromSeconds(10);
		httpClient.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate");

		var webPageHtml = await httpClient.GetStringAsync(SettingsProvider.Instance.Settings.ScrapePageUrl);
		
		// Find page script
		var scriptUrl = EpiScriptRegex().Match(webPageHtml).Groups["url"].Value;
		if (string.IsNullOrWhiteSpace(scriptUrl) || !Uri.IsWellFormedUriString(scriptUrl, UriKind.Absolute))
			throw new Exception("Could not find the epi-es2015.js script URL");

		Console.WriteLine("Found epi JS script: " + scriptUrl);

		// Find API base URL in page script
		var js = await httpClient.GetStringAsync(scriptUrl);
		var apiBaseUrl = ApiBaseUrlRegex().Match(js).Groups["url"].Value.TrimEnd('/');
		if (string.IsNullOrWhiteSpace(apiBaseUrl))
			throw new Exception("Could not find the API base URL");

		Console.WriteLine("API base URL: " + apiBaseUrl);

		// Find API key in page script
		var apiKey = TariffApiKeyRegex().Match(js).Groups["key"].Value;
		if (string.IsNullOrWhiteSpace(apiKey))
			throw new Exception("Could not find the API key");
		
		Console.WriteLine("API key: " + apiKey);

		// Update known values
		SettingsProvider.Instance.Settings.KnownApiBaseUrl = apiBaseUrl;
		SettingsProvider.Instance.Settings.KnownApiKey = apiKey;
		
		return (apiBaseUrl, apiKey);
	}

	[GeneratedRegex(@"src=""(?<url>https:\/\/cdn\.vattenfall\.nl\/vattenfallnlprd\/features\/epi\/(?:[^\/]*)\/epi-es2015\.js)""", RegexOptions.Compiled)]
	private static partial Regex EpiScriptRegex();
	
	[GeneratedRegex(@"dynamicTariffsBaseApiURL:""(?<url>[^""]*)", RegexOptions.Compiled)]
	private static partial Regex ApiBaseUrlRegex();
	
	[GeneratedRegex(@"ocpApimSubscriptionFeaturesDynamicTariffsKey:""(?<key>[^""]*)")]
	private static partial Regex TariffApiKeyRegex();
}