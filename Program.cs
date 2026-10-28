using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using GeoConquest.Configuration;
using GeoConquest.Data;
using GeoConquest.Models.Domain;
using GeoConquest.Repositories;
using GeoConquest.Services;
using GeoConquest.Services.Commentary;
using GeoConquest.Services.Npc;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
builder.Services.AddScoped<IUserRepository, MongoUserRepository>();

// ── Game engine ───────────────────────────────────────────────────────────────
// Stateless helpers are singletons (the odds cache is shared); the engine is per-request.
builder.Services.AddSingleton<IRandomSource, CryptoRandomSource>();
builder.Services.AddSingleton<ICountryCatalog, StaticCountryCatalog>();
builder.Services.AddSingleton<IBattleOddsCalculator, BattleOddsCalculator>();
builder.Services.AddSingleton<INpcStrategy, NpcStrategy>();
builder.Services.AddScoped<IGameEngineService, GameEngineService>();

// ── AI battle commentary (any OpenAI-compatible API; Gemini by default) ───────
builder.Services
    .AddOptions<CommentarySettings>()
    .Bind(builder.Configuration.GetSection(CommentarySettings.SectionName));
builder.Services.AddHttpClient(LlmCommentaryService.HttpClientName);
builder.Services.AddSingleton<ICommentaryService, LlmCommentaryService>();

// ── Accounts & security ───────────────────────────────────────────────────────
builder.Services.AddSingleton<IPasswordHasher<UserAccount>, PasswordHasher<UserAccount>>();
builder.Services.AddScoped<IAccountService, AccountService>();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.Cookie.Name = "GeoConquest.Auth";
        options.Cookie.HttpOnly = true;                              // not readable by JavaScript
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest                         // allows the local http profile
            : CookieSecurePolicy.Always;                               // HTTPS only when deployed
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
    });

builder.Services.AddAuthorization();

// Lets browser scripts (Phase 4) send the anti-forgery token in a header.
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(RateLimitPolicies.Auth, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});

// ── MVC ───────────────────────────────────────────────────────────────────────
// Every POST must carry a valid anti-forgery token (protects moves from cross-site forgery).
builder.Services
    .AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()))
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

// ── Verify the Atlas connection and indexes before accepting traffic ─────────
var mongo = app.Services.GetRequiredService<MongoDbContext>();
try
{
    await mongo.PingAsync();
    await mongo.EnsureIndexesAsync();
    app.Logger.LogInformation("Connected to MongoDB Atlas and verified indexes.");

    var commentary = app.Services.GetRequiredService<IOptions<CommentarySettings>>().Value;
    if (commentary.IsConfigured)
        app.Logger.LogInformation("AI commentary enabled with model {Model}.", commentary.Model);
    else
        app.Logger.LogWarning("AI commentary is OFF: no Commentary:ApiKey set. Built-in fallback lines will be used.");
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
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
