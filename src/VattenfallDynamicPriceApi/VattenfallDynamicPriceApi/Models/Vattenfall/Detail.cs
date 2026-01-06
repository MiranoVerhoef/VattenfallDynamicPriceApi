using System.Text.Json.Serialization;

namespace VattenfallDynamicPriceApi.Models.Vattenfall;

public class Detail
{
	[JsonPropertyName("amount")]
	public decimal Amount { get; set; }

	[JsonPropertyName("amountExclVat")]
	public decimal AmountExclVat { get; set; }

	[JsonPropertyName("amountInclVat")]
	public decimal AmountInclVat { get; set; }

	[JsonPropertyName("type")]
	public string? Type { get; set; }
}