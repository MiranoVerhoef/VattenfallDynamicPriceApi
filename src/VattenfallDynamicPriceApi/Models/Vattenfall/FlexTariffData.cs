using System.Text.Json.Serialization;

namespace VattenfallDynamicPriceApi.Models.Vattenfall;

public class FlexTariffData
{
	[JsonPropertyName("product")]
	public string? Product { get; set; }

	[JsonPropertyName("name")]
	public string? Name { get; set; }

	[JsonPropertyName("productCode")]
	public string? ProductCode { get; set; }

	[JsonPropertyName("tariffData")]
	public List<TariffData> TariffData { get; set; } = [];

	[JsonPropertyName("averageTariffs")]
	public List<AverageTariff> AverageTariffs { get; set; } = [];
}