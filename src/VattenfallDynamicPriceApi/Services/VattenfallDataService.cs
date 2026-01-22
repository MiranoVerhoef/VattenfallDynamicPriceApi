using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using Serilog;
using VattenfallDynamicPriceApi.Extensions;
using VattenfallDynamicPriceApi.Models.Evcc;
using VattenfallDynamicPriceApi.Models.Vattenfall;

namespace VattenfallDynamicPriceApi.Services;

public partial class VattenfallDataService : IDisposable, IAsyncDisposable
{

	public FlexTariffData[]? Data { get; private set; } = [];
	
	public EvccApiHourlyData[]? EvccData { get; private set; } = [];
	
	private TimeSpan _cacheDuration = TimeSpan.FromSeconds(60);
	private Timer? _timer;
	private readonly SemaphoreSlim _updateLock = new(1, 1);

	public async Task InitializeAsync()
	{
		_cacheDuration = TimeSpan.FromSeconds(Math.Max(60, SettingsProvider.Instance.Settings.RefreshIntervalSeconds));
		Log.Information("Refresh interval: {Interval}", _cacheDuration);
		
		await UpdateDataAsyncSafe();
		_timer = new Timer(RefreshTimerElapsed, null, _cacheDuration, _cacheDuration);
	}

	private decimal GetCurrentTariffForProductType(string productType, string description)
	{
		var productData = Data?.FirstOrDefault(d => d.Product == productType);
		if (productData == null)
		{
			Log.Error("Could not get current {Description} tariff, no data", description);
			return 999;
		}

		var now = DateTime.Now;
		var currentTariff = productData.TariffData.FirstOrDefault(d => d.StartTime <= now && d.EndTime >= now);
		if (currentTariff != null)
			return currentTariff.AmountInclVat;

		var highestTariff = productData.TariffData.Max(t => t.AmountInclVat);
		Log.Error("Could not get current {Description} tariff, no value found for current time, returning highest value: {HighestTariff}", description, highestTariff);

		return highestTariff;
	}

	public decimal GetCurrentElectricityTariff()
		=> GetCurrentTariffForProductType("E", "electricity");
	
	public decimal GetCurrentGasTariff()
		=> GetCurrentTariffForProductType("G", "gas");

	private void RefreshTimerElapsed(object? _)
	{
		// Fire-and-forget; UpdateDataAsyncSafe handles locking + error logging.
		_ = UpdateDataAsyncSafe();
	}

	private async Task UpdateDataAsyncSafe()
	{
		if (!await _updateLock.WaitAsync(0))
			return; // Skip if an update is already running.

		try
		{
			Log.Information("Updating data");
			await UpdateDataAsync();
			Log.Information("Updated data");
		}
		catch (Exception e)
		{
			Log.Error(e, "Failed to update data");
		}
		finally
		{
			_updateLock.Release();
		}
	}

private async Task UpdateDataAsync()
	{
		var (apiBaseUrl, apiKey) = await TryGetApiUrlAndKeyAsync();
		Data = await GetFlexTariffDataAsync(apiBaseUrl, apiKey);
		
		var electricityData = Data.FirstOrDefault(d => d.Product == "E");
		if (electricityData == null)
		{
			Log.Error("Could not find electricity data in API response");
			return;
		}
		
		EvccData = electricityData.TariffData.Select(td => new EvccApiHourlyData
			{
				Start = td.StartTime.ToUtcKeepTimeAsIs(),
				End = td.EndTime.ToUtcKeepTimeAsIs(),
				Value = td.AmountInclVat
			})
			.OrderBy(td => td.Start)
			.ToArray();
	}

	private static async Task<FlexTariffData[]> GetFlexTariffDataAsync(string apiBaseUrl, string apiKey)
	{
		if (string.IsNullOrWhiteSpace(apiBaseUrl))
			throw new Exception("API base URL is empty. Provide VFAPI_KnownApiBaseUrl/VFAPI_KnownApiKey or allow dynamic scraping.");
		if (string.IsNullOrWhiteSpace(apiKey))
			throw new Exception("API key is empty. Provide VFAPI_KnownApiBaseUrl/VFAPI_KnownApiKey or allow dynamic scraping.");

		if (!Uri.TryCreate(apiBaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri))
			throw new Exception($"API base URL is not a valid absolute URL: '{apiBaseUrl}'");

		var apiEndpoint = new Uri(baseUri, "DynamicTariff");
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
		{
			var knownBaseUrl = SettingsProvider.Instance.Settings.KnownApiBaseUrl;
			var knownKey = SettingsProvider.Instance.Settings.KnownApiKey;
			if (string.IsNullOrWhiteSpace(knownBaseUrl) || string.IsNullOrWhiteSpace(knownKey))
				Log.Warning("UseKnownValues is enabled but KnownApiBaseUrl/KnownApiKey are empty");
			return (knownBaseUrl, knownKey);
		}

		try
		{
			return await GetApiUrlAndKeyAsync();
		}
		catch (Exception e)
		{
			Log.Error(e, "Failed to get API URL and key dynamically, falling back to known values");
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

		Log.Information("Found epi JS script: {Url}", scriptUrl);

		// Find API base URL in page script
		var js = await httpClient.GetStringAsync(scriptUrl);
		var apiBaseUrl = ApiBaseUrlRegex().Match(js).Groups["url"].Value.TrimEnd('/');
		if (string.IsNullOrWhiteSpace(apiBaseUrl))
			throw new Exception("Could not find the API base URL");

		Log.Information("API base URL: {ApiBaseUrl}", apiBaseUrl);

		// Find API key in page script
		var apiKey = TariffApiKeyRegex().Match(js).Groups["key"].Value;
		if (string.IsNullOrWhiteSpace(apiKey))
			throw new Exception("Could not find the API key");
		
		Log.Information("API key: {ApiKey}", apiKey);

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

	public void Dispose()
	{
		_timer?.Dispose();
	}

	public async ValueTask DisposeAsync()
	{
		if (_timer != null)
			await _timer.DisposeAsync();
	}
}