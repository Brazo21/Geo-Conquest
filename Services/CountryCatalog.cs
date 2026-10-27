using GeoConquest.Models.Domain;

namespace GeoConquest.Services;

/// <summary>A country a player can pick or be drawn against, with its real population.</summary>
public sealed record CountryInfo(string Code, string Name, long Population)
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
        new("IN", "India", 1_476_625_576),
        new("CN", "China", 1_412_914_089),
        new("US", "United States", 349_035_494),
        new("ID", "Indonesia", 287_886_782),
        new("PK", "Pakistan", 259_299_791),
        new("NG", "Nigeria", 242_431_832),
        new("BR", "Brazil", 213_562_666),
        new("BD", "Bangladesh", 177_818_044),

        // 8 soldiers (≤ 150M)
        new("RU", "Russia", 143_394_458),
        new("ET", "Ethiopia", 138_902_185),
        new("MX", "Mexico", 132_997_658),

        // 7 soldiers (≤ 125M)
        new("JP", "Japan", 122_427_731),
        new("EG", "Egypt", 120_101_175),
        new("PH", "Philippines", 117_724_471),
        new("CD", "DR Congo", 116_452_162),
        new("VN", "Vietnam", 102_177_431),

        // 6 soldiers (≤ 100M)
        new("IR", "Iran", 93_168_497),
        new("TR", "Turkey", 87_926_082),
        new("DE", "Germany", 83_644_258),

        // 5 soldiers (≤ 75M)
        new("TZ", "Tanzania", 72_563_780),
        new("TH", "Thailand", 71_559_614),
        new("GB", "United Kingdom", 69_931_528),
        new("FR", "France", 66_746_401),
        new("ZA", "South Africa", 65_453_084),
        new("IT", "Italy", 58_926_166),
        new("KE", "Kenya", 58_636_412),
        new("CO", "Colombia", 53_936_226),
        new("UG", "Uganda", 52_761_469),
        new("KR", "South Korea", 51_600_388),

        // 4 soldiers (≤ 50M)
        new("ES", "Spain", 47_850_793),
        new("AR", "Argentina", 46_003_734),
        new("CA", "Canada", 40_467_728),
        new("AO", "Angola", 40_215_179),
        new("UA", "Ukraine", 39_535_849),
        new("MA", "Morocco", 38_762_441),
        new("PL", "Poland", 37_843_188),
        new("MZ", "Mozambique", 36_639_851),
        new("GH", "Ghana", 35_697_557),
        new("SA", "Saudi Arabia", 35_165_787),
        new("PE", "Peru", 34_922_148),
        new("AU", "Australia", 27_227_096),

        // 3 soldiers (≤ 25M)
        new("ZM", "Zambia", 22_521_915),
        new("CL", "Chile", 19_945_850),
        new("NL", "Netherlands", 18_448_775),
        new("ZW", "Zimbabwe", 17_273_580),
        new("RW", "Rwanda", 14_889_693),
        new("BE", "Belgium", 11_774_642),
        new("SE", "Sweden", 10_701_047),
        new("PT", "Portugal", 10_395_362),

        // 2 soldiers (≤ 10M)
        new("GR", "Greece", 9_897_115),
        new("IL", "Israel", 9_647_689),
        new("AT", "Austria", 9_107_266),
        new("CH", "Switzerland", 9_007_798),
        new("DK", "Denmark", 6_023_520),
        new("SG", "Singapore", 5_905_748),

        // 1 soldier (≤ 5M)
        new("LU", "Luxembourg", 687_448),
        new("MT", "Malta", 549_011),
        new("IS", "Iceland", 402_329),
        new("MC", "Monaco", 38_087),
        new("VA", "Vatican City", 506)
    }.AsReadOnly();

    private static readonly Dictionary<string, CountryInfo> ByCode =
        Countries.ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<CountryInfo> GetAll() => Countries;

    public CountryInfo? Find(string code) =>
        !string.IsNullOrWhiteSpace(code) && ByCode.TryGetValue(code.Trim(), out var info) ? info : null;
}
