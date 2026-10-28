using GeoConquest.Models.Domain;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;

namespace GeoConquest.Data;

/// <summary>
/// All BSON mapping lives here, so the domain classes stay free of MongoDB attributes
/// (database concerns are kept separate from the rest of the application).
/// Must be called once, before the first collection is used.
/// </summary>
public static class MongoMappings
{
    private static int _registered;

    public static void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1)
            return;

        var conventions = new ConventionPack
        {
            new CamelCaseElementNameConvention(),
            new EnumRepresentationConvention(BsonType.String), // "Attacking" instead of 3 — readable in Atlas
            new IgnoreExtraElementsConvention(true)            // tolerate fields added in later versions
        };
        ConventionRegistry.Register(
            "GeoConquestConventions",
            conventions,
            type => type.Namespace?.StartsWith("GeoConquest", StringComparison.Ordinal) == true);

        // Private setters and private parameterless constructors are populated by the driver via reflection.
        BsonClassMap.TryRegisterClassMap<Country>(cm => cm.AutoMap());
        BsonClassMap.TryRegisterClassMap<Player>(cm => cm.AutoMap());
        BsonClassMap.TryRegisterClassMap<UserAccount>(cm =>
        {
            cm.AutoMap();
            cm.MapIdMember(u => u.Id);
        });

        BsonClassMap.TryRegisterClassMap<GameEvent>(cm =>
        {
            cm.AutoMap();
            cm.UnmapMember(e => e.HasCommentary);
            cm.UnmapMember(e => e.IsBattleEvent);
            cm.UnmapMember(e => e.AttackerDice);
            cm.UnmapMember(e => e.DefenderDice);
            cm.MapField("_attackerDice").SetElementName("attackerDice");
            cm.MapField("_defenderDice").SetElementName("defenderDice");
        });

        BsonClassMap.TryRegisterClassMap<GameSession>(cm =>
        {
            cm.AutoMap();
            cm.MapIdMember(g => g.Id);

            // Persist the private backing lists, not the read-only views.
            cm.UnmapMember(g => g.Players);
            cm.UnmapMember(g => g.Countries);
            cm.UnmapMember(g => g.Events);
            cm.MapField("_players").SetElementName("players");
            cm.MapField("_countries").SetElementName("countries");
            cm.MapField("_events").SetElementName("events");
        });
    }
}
