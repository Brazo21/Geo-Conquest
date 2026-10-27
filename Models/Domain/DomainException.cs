namespace GeoConquest.Models.Domain;

/// <summary>
/// Thrown when an operation would break a game rule or an aggregate invariant
/// (e.g. leaving a country with zero soldiers, acting out of turn).
/// Controllers translate this into a user-facing validation error.
/// </summary>
public sealed class DomainException : Exception
{
    public DomainException(string message) : base(message)
    {
    }
}
