namespace GeoConquest.Services;

/// <summary>Names of the rate-limiting policies registered in Program.cs.</summary>
public static class RateLimitPolicies
{
    /// <summary>Login and sign-up: 10 attempts per minute per IP address, to slow down password guessing.</summary>
    public const string Auth = "auth";
}
