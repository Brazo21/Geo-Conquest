using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using GeoConquest.Configuration;
using GeoConquest.Data;
using GeoConquest.Models.Domain;
using GeoConquest.Repositories;
using GeoConquest.Security;
using GeoConquest.Services;
using GeoConquest.Services.Analytics;
using GeoConquest.Services.Commentary;
using GeoConquest.Services.Email;
using GeoConquest.Services.Npc;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.HttpOverrides;
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
builder.Services.AddScoped<IAnalyticsService, AnalyticsService>();

// ── AI battle commentary (any OpenAI-compatible API; Gemini by default) ───────
builder.Services
    .AddOptions<CommentarySettings>()
    .Bind(builder.Configuration.GetSection(CommentarySettings.SectionName));
builder.Services.AddHttpClient(LlmCommentaryService.HttpClientName);
builder.Services.AddSingleton<ICommentaryService, LlmCommentaryService>();

// ── Accounts & security ───────────────────────────────────────────────────────
builder.Services.AddSingleton<IPasswordHasher<UserAccount>, PasswordHasher<UserAccount>>();
builder.Services.AddScoped<IAccountService, AccountService>();
builder.Services.AddScoped<IAdminService, AdminService>();
builder.Services.AddScoped<AdminSeeder>();
builder.Services.Configure<AdminSeedSettings>(builder.Configuration.GetSection(AdminSeedSettings.SectionName));

// Signed-in sessions are re-checked against the account (suspended / password changed → signed out).
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<SessionStateCache>();
builder.Services.AddScoped<AccountSessionValidator>();

// ── Email (password resets): queued and sent in the background over SMTP ─────
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection(EmailSettings.SectionName));
builder.Services.AddSingleton<EmailQueue>();
builder.Services.AddSingleton<IEmailQueue>(sp => sp.GetRequiredService<EmailQueue>());
builder.Services.AddHostedService<EmailSenderBackgroundService>();
builder.Services.AddHttpClient(EmailSenderBackgroundService.BrevoClientName, c => c.Timeout = TimeSpan.FromSeconds(20));

// ── Cookie-encryption keys live in MongoDB, so restarts and redeploys don't log everyone out ──
builder.Services.AddDataProtection().SetApplicationName("GeoConquest");
builder.Services.AddOptions<KeyManagementOptions>()
    .Configure<MongoDbContext>((options, db) => options.XmlRepository = new MongoXmlRepository(db.DataProtectionKeys));

// ── Running behind a hosting proxy (Render sets RENDER=true) ──────────────────
// The proxy ends HTTPS and forwards plain HTTP. Trusting its X-Forwarded-* headers lets the app see the
// real scheme (secure cookies, HTTPS redirect) and the visitor's real IP (login rate limit). Only switched on
// behind a proxy: an app open to the internet directly must not trust these headers, which anyone can fake.
bool behindProxy = builder.Configuration.GetValue<bool>("BehindProxy") ||
                   string.Equals(Environment.GetEnvironmentVariable("RENDER"), "true", StringComparison.OrdinalIgnoreCase);
if (behindProxy)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownNetworks.Clear();      // the proxy's address isn't fixed on Render
        options.KnownProxies.Clear();
        options.ForwardLimit = 1;           // only the entry the host's own proxy added
    });
    builder.Services.AddHttpsRedirection(options => options.HttpsPort = 443);
}

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
        options.Events.OnValidatePrincipal = context =>
            context.HttpContext.RequestServices.GetRequiredService<AccountSessionValidator>().ValidateAsync(context);
    });

builder.Services.AddAuthorization();

// Lets browser scripts (Phase 4) send the anti-forgery token in a header.
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});

builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(180);
    options.IncludeSubDomains = true;
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(RateLimitPolicies.Auth, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
    options.AddPolicy(RateLimitPolicies.Moves, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1) }));
});

// ── MVC ───────────────────────────────────────────────────────────────────────
// Every POST must carry a valid anti-forgery token (protects moves from cross-site forgery).
builder.Services
    .AddControllersWithViews(options =>
    {
        options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
        options.Filters.Add<PopiaConsentFilter>();     // logged-in users must accept the privacy notice first
    })
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

// ── Verify the Atlas connection and indexes before accepting traffic ─────────
var mongo = app.Services.GetRequiredService<MongoDbContext>();
try
{
    await mongo.PingAsync();
    await mongo.EnsureIndexesAsync();

    // A bad admin setting (e.g. weak password) is logged clearly but doesn't stop the site from starting.
    try
    {
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AdminSeeder>().SeedAsync();
    }
    catch (DomainException ex)
    {
        app.Logger.LogError("Admin account not created: {Reason} Fix Admin__Password / Admin__Username / Admin__Email and redeploy.", ex.Message);
    }

    var email = app.Services.GetRequiredService<IOptions<EmailSettings>>().Value;
    if (email.IsConfigured && string.IsNullOrWhiteSpace(email.PublicBaseUrl) && !app.Environment.IsDevelopment())
        app.Logger.LogError("Email:PublicBaseUrl is empty: password-reset links can't be built in production.");
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
// Friendly error pages everywhere: no stack traces or technical details ever reach the browser
// (the full error is written to the log instead).
if (behindProxy)
    app.UseForwardedHeaders();

app.UseExceptionHandler("/Home/Error");
app.UseStatusCodePagesWithReExecute("/Home/Status/{0}");
if (!app.Environment.IsDevelopment())
    app.UseHsts();

app.UseMiddleware<SecurityHeadersMiddleware>();
// /healthz is answered over plain HTTP too: the host's health checker calls the container directly.
app.UseWhen(ctx => !ctx.Request.Path.StartsWithSegments("/healthz"), branch => branch.UseHttpsRedirection());
app.UseStaticFiles();

app.UseRouting();
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

// Health check for the host: answers fast and touches nothing personal.
app.MapGet("/healthz", () => Results.Text("ok")).AllowAnonymous().DisableRateLimiting();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
