using System.Text.Json.Serialization;
using VattenfallDynamicPriceApi.Models.Evcc;
using VattenfallDynamicPriceApi.Models.Vattenfall;

namespace VattenfallDynamicPriceApi;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(FlexTariffData[]))]
[JsonSerializable(typeof(EvccApiHourlyData[]))]
internal partial class SourceGenerationContext : JsonSerializerContext { }