using System.Text.Json.Serialization;

namespace VattenfallDynamicPriceApi.Models.Evcc;

public class EvccApiHourlyData
{
	[JsonPropertyName("start")]
	public DateTime Start { get; set; }

	[JsonPropertyName("end")]
	public DateTime End { get; set; }

	[JsonPropertyName("value")]
	public decimal Value { get; set; }
}