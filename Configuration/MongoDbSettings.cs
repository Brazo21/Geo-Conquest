using System.ComponentModel.DataAnnotations;

namespace GeoConquest.Configuration;

/// <summary>
/// Strongly typed binding for the "MongoDb" section of appsettings.json.
/// Validated at startup so a missing or malformed Atlas connection string fails fast
/// with a clear message instead of on the first player request.
/// </summary>
public sealed class MongoDbSettings
{
    public const string SectionName = "MongoDb";

    [Required(ErrorMessage =
        "MongoDb:ConnectionString is missing. In Visual Studio right-click the project > Manage User Secrets " +
        "and add it there, or set the environment variable MongoDb__ConnectionString.")]
    [RegularExpression(@"^mongodb(\+srv)?://.+",
        ErrorMessage = "MongoDb:ConnectionString must start with mongodb:// or mongodb+srv://.")]
    public string ConnectionString { get; init; } = string.Empty;

    [Required]
    [MinLength(1)]
    public string DatabaseName { get; init; } = "GeoConquest";

    [Required]
    [MinLength(1)]
    public string GamesCollectionName { get; init; } = "games";

    [Required]
    [MinLength(1)]
    public string UsersCollectionName { get; init; } = "users";
}
