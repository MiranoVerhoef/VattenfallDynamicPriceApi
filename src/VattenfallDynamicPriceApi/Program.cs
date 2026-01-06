using VattenfallDynamicPriceApi;
using VattenfallDynamicPriceApi.Services;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
	options.SerializerOptions.TypeInfoResolverChain.Insert(0, SourceGenerationContext.Default);
});

var app = builder.Build();
var dataService = new VattenfallDataService();
await dataService.InitializeAsync();

var version1Group = app.MapGroup("/v1");
version1Group.MapGet("/data", () => dataService.Data);
version1Group.MapGet("/evcc", () => dataService.EvccData);

await app.RunAsync();