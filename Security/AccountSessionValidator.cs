using System.Security.Claims;
using GeoConquest.Models.Domain;
using GeoConquest.Repositories;
using GeoConquest.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.Memory;

namespace GeoConquest.Security;

public sealed class SessionStateCache
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

    private readonly IMemoryCache _cache;

    public SessionStateCache(IMemoryCache cache)
    {
        _cache = cache;
    }

    internal sealed record State(string Stamp, AccountStatus Status);

    private static string Key(string userId) => $"session:{userId}";

    internal bool TryGet(string userId, out State? state) => _cache.TryGetValue(Key(userId), out state);

    internal void Set(string userId, State? state) =>
        _cache.Set(Key(userId), state, Lifetime);

    public void Invalidate(string userId)
    {
        if (!string.IsNullOrEmpty(userId))
            _cache.Remove(Key(userId));
    }
}

public sealed class AccountSessionValidator
{
    private readonly IUserRepository _users;
    private readonly SessionStateCache _cache;
    private readonly ILogger<AccountSessionValidator> _logger;

    public AccountSessionValidator(IUserRepository users, SessionStateCache cache, ILogger<AccountSessionValidator> logger)
    {
        _users = users;
        _cache = cache;
        _logger = logger;
    }

    public async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        string? userId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        string? stamp = context.Principal?.FindFirstValue(AccountService.StampClaim);

        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(stamp))
        {
            await RejectAsync(context);          // cookies from before stamps existed: log in once more
            return;
        }

        SessionStateCache.State? state;
        if (!_cache.TryGet(userId, out state))
        {
            try
            {
                var account = await _users.GetByIdAsync(userId, context.HttpContext.RequestAborted);
                state = account is null ? null : new SessionStateCache.State(account.SecurityStamp, account.Status);
                _cache.Set(userId, state);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Database briefly unreachable: keep the session rather than logging everyone out.
                _logger.LogWarning(ex, "Could not check the session for user {UserId}", userId);
                return;
            }
        }

        if (state is null || state.Status == AccountStatus.Suspended || !string.Equals(state.Stamp, stamp, StringComparison.Ordinal))
            await RejectAsync(context);
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
