using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;
using MongoDB.Bson;
using MongoDB.Driver;

namespace GeoConquest.Data;

public sealed class MongoXmlRepository : IXmlRepository
{
    private readonly IMongoCollection<BsonDocument> _keys;

    public MongoXmlRepository(IMongoCollection<BsonDocument> keys)
    {
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
    }

    public IReadOnlyCollection<XElement> GetAllElements() =>
        _keys.Find(FilterDefinition<BsonDocument>.Empty)
            .ToList()
            .Select(d => XElement.Parse(d["xml"].AsString))
            .ToList()
            .AsReadOnly();

    public void StoreElement(XElement element, string friendlyName) =>
        _keys.InsertOne(new BsonDocument
        {
            { "friendlyName", friendlyName ?? string.Empty },
            { "xml", element.ToString(SaveOptions.DisableFormatting) },
            { "createdAtUtc", DateTime.UtcNow }
        });
}
