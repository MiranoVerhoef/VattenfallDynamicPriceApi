using Serilog;
using Serilog.Events;
using VattenfallDynamicPriceApi;
using VattenfallDynamicPriceApi.Services;

try
{
	SetUpSerilog();
	await RunAppAsync(args);
}
catch (Exception ex)
{
	Console.WriteLine("Application crashed on startup: " + ex);
	Log.Fatal(ex, "Application crashed on startup");
}
finally
{
	await Log.CloseAndFlushAsync();
}

return;

static async Task RunAppAsync(string[] args)
{
	var builder = WebApplication.CreateSlimBuilder(args);
	
	builder.Host.UseSerilog();

	builder.Services.ConfigureHttpJsonOptions(options =>
	{
		options.SerializerOptions.TypeInfoResolverChain.Insert(0, SourceGenerationContext.Default);
	});

	var app = builder.Build();
	var dataService = new VattenfallDataService();
	await dataService.InitializeAsync();


    // Simple built-in UI (HTML) for quick human viewing
    app.MapGet("/", () => Results.Content("""
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>Vattenfall Dynamic Price API</title>
  <style>
    :root { font-family: system-ui, -apple-system, Segoe UI, Roboto, Arial, sans-serif; }
    body { margin: 0; padding: 16px; line-height: 1.45; }
    .wrap { max-width: 980px; margin: 0 auto; }
    .card { border: 1px solid #ddd; border-radius: 12px; padding: 14px 16px; margin: 12px 0; }
    h1 { margin: 0 0 6px; font-size: 22px; }
    h2 { margin: 0 0 10px; font-size: 16px; }
    .muted { color: #666; }
    .row { display: flex; gap: 12px; flex-wrap: wrap; }
    .pill { display: inline-block; padding: 2px 8px; border: 1px solid #ddd; border-radius: 999px; font-size: 12px; }
    table { width: 100%; border-collapse: collapse; }
    th, td { padding: 6px 8px; border-bottom: 1px solid #eee; font-size: 13px; text-align: left; vertical-align: top; }
    th { font-weight: 600; }
    .right { text-align: right; }
    .ok { color: #0a7f2e; }
    .warn { color: #b45309; }
    code { background: #f6f6f6; padding: 1px 5px; border-radius: 6px; }
    a { color: inherit; }
  </style>
</head>
<body>
  <div class="wrap">
    <h1>Vattenfall Dynamic Price API</h1>
    <div class="muted">
      If you got an SSL error in your browser: this service is <b>HTTP</b> by default, so use
      <code>http://&lt;host&gt;:8080</code> (not https).
    </div>

    <div class="row">
      <div class="card" style="flex:1 1 280px;">
        <h2>Now</h2>
        <div>Electricity: <b id="nowE">…</b></div>
        <div>Gas: <b id="nowG">…</b></div>
        <div class="muted" style="margin-top:8px;">
          Endpoints: <a href="/v1/now/electricity">/v1/now/electricity</a>,
          <a href="/v1/now/gas">/v1/now/gas</a>
        </div>
      </div>

      <div class="card" style="flex:1 1 280px;">
        <h2>API</h2>
        <div class="muted">
          <div><a href="/v1/data">/v1/data</a> (raw Vattenfall parse)</div>
          <div><a href="/v1/evcc">/v1/evcc</a> (EVCC format)</div>
          <div><a href="/healthz">/healthz</a> (health check)</div>
        </div>
      </div>
    </div>

    <div class="card">
      <h2>Today</h2>
      <div class="muted">Shows <span class="pill">amountInclVat</span> for each hour where available.</div>
      <div id="tables" class="muted" style="margin-top:10px;">Loading…</div>
    </div>
  </div>

<script>
async function text(url) {
  const r = await fetch(url, { cache: "no-store" });
  if (!r.ok) throw new Error(url + " -> " + r.status);
  return await r.text();
}
async function json(url) {
  const r = await fetch(url, { cache: "no-store" });
  if (!r.ok) throw new Error(url + " -> " + r.status);
  return await r.json();
}

function fmtMoney(v) {
  if (v === null || v === undefined) return "—";
  const n = Number(v);
  if (!Number.isFinite(n)) return String(v);
  return n.toFixed(5);
}

function fmtTime(s) {
  try {
    const d = new Date(s);
    return d.toLocaleString(undefined, { hour: "2-digit", minute: "2-digit" });
  } catch { return String(s); }
}

function renderProduct(p) {
  const tariffs = (p.tariffData || []);
  if (!tariffs.length) return "";
  const rows = tariffs.map(t => `
    <tr>
      <td>${fmtTime(t.startTime)}–${fmtTime(t.endTime)}</td>
      <td class="right">${fmtMoney(t.amountInclVat)}</td>
      <td class="right">${t.cheapestOfDay ? '<span class="ok">yes</span>' : '—'}</td>
      <td>${t.isMissingPeriod ? '<span class="warn">missing</span>' : 'ok'}</td>
    </tr>
  `).join("");

  const title = (p.productCode ? p.productCode + " · " : "") + (p.name || p.product || "Product");
  return `
    <div style="margin-top:12px;">
      <div style="font-weight:600; margin-bottom:6px;">${title}</div>
      <table>
        <thead>
          <tr><th>Time</th><th class="right">Incl. VAT</th><th class="right">Cheapest</th><th>Status</th></tr>
        </thead>
        <tbody>${rows}</tbody>
      </table>
    </div>
  `;
}

(async () => {
  try {
    document.getElementById("nowE").textContent = await text("/v1/now/electricity");
    document.getElementById("nowG").textContent = await text("/v1/now/gas");

    const data = await json("/v1/data");
    const tables = (data || []).map(renderProduct).join("");
    document.getElementById("tables").innerHTML = tables || "<div class='muted'>No data yet. Try again in a moment.</div>";
  } catch (e) {
    document.getElementById("tables").textContent = "Error: " + (e && e.message ? e.message : e);
  }
})();
</script>
</body>
</html>
""", "text/html; charset=utf-8"));

    app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));


	var version1Group = app.MapGroup("/v1");
	version1Group.MapGet("/data", () => dataService.Data);
	version1Group.MapGet("/evcc", () => dataService.EvccData);
	version1Group.MapGet("/now/electricity", () => dataService.GetCurrentElectricityTariff());
	version1Group.MapGet("/now/gas", () => dataService.GetCurrentGasTariff());

	await app.RunAsync();
}

static void SetUpSerilog()
{
	if (!Enum.TryParse(SettingsProvider.Instance.Settings.Logging.LogLevel, out LogEventLevel logLevel))
		logLevel = LogEventLevel.Warning;

	if (!Enum.TryParse(SettingsProvider.Instance.Settings.Logging.AspNetLogLevel, out LogEventLevel aspNetLogLevel))
		aspNetLogLevel = LogEventLevel.Warning;

	var loggerConfiguration = new LoggerConfiguration();

	loggerConfiguration
		.Enrich.FromLogContext()
		.MinimumLevel.Is(logLevel)
		.MinimumLevel.Override("Microsoft.Hosting.Lifetime", logLevel)
		.MinimumLevel.Override("Microsoft.Hosting", aspNetLogLevel)
		.MinimumLevel.Override("Microsoft.AspNetCore", aspNetLogLevel);
		
	loggerConfiguration = loggerConfiguration.WriteTo.Console(
		outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");

	Log.Logger = loggerConfiguration.CreateLogger();
}