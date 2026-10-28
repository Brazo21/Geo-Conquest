using GeoConquest.Models.Domain;

namespace GeoConquest.Services;

/// <summary>
/// A country a player can pick or be drawn against, with its real population.
/// MapId is the ISO 3166-1 numeric code used by the world map; Lat/Lon place its pin.
/// </summary>
public sealed record CountryInfo(string Code, string Name, long Population, string MapId, double Lat, double Lon)
{
    public int SoldierValue => Country.CalculateSoldierValue(Population);
}

public interface ICountryCatalog
{
    IReadOnlyList<CountryInfo> GetAll();

    CountryInfo? Find(string code);
}

/// <summary>
/// The selectable world. Populations are 2026 estimates from Worldometers
/// (https://www.worldometers.info/world-population/population-by-country/,
/// based on UN World Population Prospects 2024). Covers every soldier tier from 1 to 9.
/// </summary>
public sealed class StaticCountryCatalog : ICountryCatalog
{
    private static readonly IReadOnlyList<CountryInfo> Countries = new List<CountryInfo>
    {
        // 9 soldiers (> 150M)
        new("IN", "India", 1_476_625_576, "356", 21, 78),
        new("CN", "China", 1_412_914_089, "156", 35, 104),
        new("US", "United States", 349_035_494, "840", 39, -98),
        new("ID", "Indonesia", 287_886_782, "360", -2, 118),
        new("PK", "Pakistan", 259_299_791, "586", 30, 70),
        new("NG", "Nigeria", 242_431_832, "566", 9, 8),
        new("BR", "Brazil", 213_562_666, "076", -10, -52),
        new("BD", "Bangladesh", 177_818_044, "050", 24, 90),

        // 8 soldiers (≤ 150M)
        new("RU", "Russia", 143_394_458, "643", 60, 95),
        new("ET", "Ethiopia", 138_902_185, "231", 9, 39),
        new("MX", "Mexico", 132_997_658, "484", 23, -102),

        // 7 soldiers (≤ 125M)
        new("JP", "Japan", 122_427_731, "392", 36, 138),
        new("EG", "Egypt", 120_101_175, "818", 27, 30),
        new("PH", "Philippines", 117_724_471, "608", 12, 122),
        new("CD", "DR Congo", 116_452_162, "180", -3, 23),
        new("VN", "Vietnam", 102_177_431, "704", 16, 107),

        // 6 soldiers (≤ 100M)
        new("IR", "Iran", 93_168_497, "364", 32, 53),
        new("TR", "Turkey", 87_926_082, "792", 39, 35),
        new("DE", "Germany", 83_644_258, "276", 51, 10),

        // 5 soldiers (≤ 75M)
        new("TZ", "Tanzania", 72_563_780, "834", -6, 35),
        new("TH", "Thailand", 71_559_614, "764", 15, 101),
        new("GB", "United Kingdom", 69_931_528, "826", 54, -2),
        new("FR", "France", 66_746_401, "250", 46, 2),
        new("ZA", "South Africa", 65_453_084, "710", -29, 24),
        new("IT", "Italy", 58_926_166, "380", 42, 12.5),
        new("KE", "Kenya", 58_636_412, "404", 0.5, 38),
        new("CO", "Colombia", 53_936_226, "170", 4, -73),
        new("UG", "Uganda", 52_761_469, "800", 1.5, 32.5),
        new("KR", "South Korea", 51_600_388, "410", 36, 128),

        // 4 soldiers (≤ 50M)
        new("ES", "Spain", 47_850_793, "724", 40, -4),
        new("AR", "Argentina", 46_003_734, "032", -34, -64),
        new("CA", "Canada", 40_467_728, "124", 56, -106),
        new("AO", "Angola", 40_215_179, "024", -12, 18),
        new("UA", "Ukraine", 39_535_849, "804", 49, 32),
        new("MA", "Morocco", 38_762_441, "504", 32, -6),
        new("PL", "Poland", 37_843_188, "616", 52, 19),
        new("MZ", "Mozambique", 36_639_851, "508", -18, 35),
        new("GH", "Ghana", 35_697_557, "288", 8, -1),
        new("SA", "Saudi Arabia", 35_165_787, "682", 24, 45),
        new("PE", "Peru", 34_922_148, "604", -10, -76),
        new("AU", "Australia", 27_227_096, "036", -25, 134),

        // 3 soldiers (≤ 25M)
        new("ZM", "Zambia", 22_521_915, "894", -14, 28),
        new("CL", "Chile", 19_945_850, "152", -33, -71),
        new("NL", "Netherlands", 18_448_775, "528", 52.3, 5.3),
        new("ZW", "Zimbabwe", 17_273_580, "716", -19, 30),
        new("RW", "Rwanda", 14_889_693, "646", -2, 30),
        new("BE", "Belgium", 11_774_642, "056", 50.6, 4.6),
        new("SE", "Sweden", 10_701_047, "752", 62, 15),
        new("PT", "Portugal", 10_395_362, "620", 39.5, -8),

        // 2 soldiers (≤ 10M)
        new("GR", "Greece", 9_897_115, "300", 39, 22),
        new("IL", "Israel", 9_647_689, "376", 31.5, 35),
        new("AT", "Austria", 9_107_266, "040", 47.5, 14.5),
        new("CH", "Switzerland", 9_007_798, "756", 46.8, 8.2),
        new("DK", "Denmark", 6_023_520, "208", 56, 10),
        new("SG", "Singapore", 5_905_748, "702", 1.35, 103.8),

        // 1 soldier (≤ 5M)
        new("LU", "Luxembourg", 687_448, "442", 49.8, 6.1),
        new("MT", "Malta", 549_011, "470", 35.9, 14.4),
        new("IS", "Iceland", 402_329, "352", 65, -18),
        new("MC", "Monaco", 38_087, "492", 43.7, 7.4),
        new("VA", "Vatican City", 506, "336", 41.9, 12.45)
    }.AsReadOnly();

    private static readonly Dictionary<string, CountryInfo> ByCode =
        Countries.ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<CountryInfo> GetAll() => Countries;

    public CountryInfo? Find(string code) =>
        !string.IsNullOrWhiteSpace(code) && ByCode.TryGetValue(code.Trim(), out var info) ? info : null;
}
