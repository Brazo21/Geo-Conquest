using GeoConquest.Configuration;
using GeoConquest.Data;
using GeoConquest.Repositories;
using GeoConquest.Services;
using GeoConquest.Services.Npc;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

// ── Data layer ────────────────────────────────────────────────────────────────
// BSON class maps must exist before the first collection is touched.
MongoMappings.Register();

builder.Services
    .AddOptions<MongoDbSettings>()
    .Bind(builder.Configuration.GetSection(MongoDbSettings.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton<IMongoClient>(serviceProvider =>
{
    var settings = serviceProvider.GetRequiredService<IOptions<MongoDbSettings>>().Value;

    var clientSettings = MongoClientSettings.FromConnectionString(settings.ConnectionString);
    clientSettings.ServerApi = new ServerApi(ServerApiVersion.V1); // Atlas Stable API
    clientSettings.ApplicationName = "GeoConquest";
    clientSettings.ConnectTimeout = TimeSpan.FromSeconds(10);
    clientSettings.ServerSelectionTimeout = TimeSpan.FromSeconds(10);

    return new MongoClient(clientSettings);
});

builder.Services.AddSingleton<MongoDbContext>();
builder.Services.AddScoped<IGameRepository, MongoGameRepository>();

// ── Game engine ───────────────────────────────────────────────────────────────
// Stateless helpers are singletons (the odds cache is shared); the engine is per-request.
builder.Services.AddSingleton<IRandomSource, CryptoRandomSource>();
builder.Services.AddSingleton<ICountryCatalog, StaticCountryCatalog>();
builder.Services.AddSingleton<IBattleOddsCalculator, BattleOddsCalculator>();
builder.Services.AddSingleton<INpcStrategy, NpcStrategy>();
builder.Services.AddScoped<IGameEngineService, GameEngineService>();

// ── MVC ───────────────────────────────────────────────────────────────────────
builder.Services.AddControllersWithViews();

var app = builder.Build();

// ── Verify the Atlas connection and indexes before accepting traffic ─────────
var mongo = app.Services.GetRequiredService<MongoDbContext>();
try
{
    await mongo.PingAsync();
    await mongo.EnsureIndexesAsync();
    app.Logger.LogInformation("Connected to MongoDB Atlas and verified indexes.");
}
catch (Exception ex)
{
    app.Logger.LogCritical(ex, "Could not connect to MongoDB Atlas. Check the connection string and Network Access list.");
    throw;
}

// ── HTTP pipeline ─────────────────────────────────────────────────────────────
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// Authentication (Identity) is added in the security phase; it must sit before UseAuthorization.
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
