namespace VattenfallDynamicPriceApi.Models.Application;

public class SettingsData
{
	public bool UseKnownValues { get; set; } = false;
	
	public string ScrapePageUrl { get; set; } = "https://www.vattenfall.nl/klantenservice/alles-over-je-dynamische-contract/";
	
	public string KnownApiBaseUrl { get; set; } = string.Empty;

	public string KnownApiKey { get; set; } = string.Empty;
}