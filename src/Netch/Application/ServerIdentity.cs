using System.Security.Cryptography;
using System.Text;
using Netch.Models;
using System.Collections.Concurrent;

namespace Netch.Application;

public static class ServerIdentity
{
    private const string FavoriteKeyVersion = "v1:";
    private static readonly ConcurrentDictionary<string, string> GeoIpCountries = new(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, string> CountryTokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["германия"] = "DE", ["нидерланды"] = "NL", ["финляндия"] = "FI", ["франция"] = "FR",
        ["швеция"] = "SE", ["норвегия"] = "NO", ["великобритания"] = "GB", ["сша"] = "US",
        ["россия"] = "RU", ["турция"] = "TR", ["казахстан"] = "KZ", ["польша"] = "PL",
        ["швейцария"] = "CH", ["латвия"] = "LV", ["литва"] = "LT", ["эстония"] = "EE",
        ["япония"] = "JP", ["сингапур"] = "SG", ["канада"] = "CA", ["австрия"] = "AT",
        ["европа"] = "EU",
        ["amsterdam"] = "NL", ["australia"] = "AU", ["austria"] = "AT", ["belarus"] = "BY",
        ["belgium"] = "BE", ["bucharest"] = "RO", ["canada"] = "CA", ["china"] = "CN",
        ["czech"] = "CZ", ["denmark"] = "DK", ["finland"] = "FI", ["france"] = "FR",
        ["frankfurt"] = "DE", ["geneva"] = "CH", ["germany"] = "DE", ["hong kong"] = "HK",
        ["india"] = "IN", ["italy"] = "IT", ["japan"] = "JP", ["kazakhstan"] = "KZ",
        ["korea"] = "KR", ["london"] = "GB", ["los angeles"] = "US", ["moscow"] = "RU",
        ["netherlands"] = "NL", ["new york"] = "US", ["norway"] = "NO", ["poland"] = "PL",
        ["romania"] = "RO", ["russia"] = "RU", ["singapore"] = "SG", ["spain"] = "ES",
        ["sweden"] = "SE", ["switzerland"] = "CH", ["tokyo"] = "JP", ["turkey"] = "TR",
        ["ukraine"] = "UA", ["united kingdom"] = "GB", ["united states"] = "US"
    };

    private static readonly HashSet<string> CountryCodes = CountryTokens.Values
        .Append("BR").Append("CL").Append("EE").Append("ID").Append("IE").Append("IL").Append("LT")
        .Append("LV").Append("MX").Append("MY").Append("NZ").Append("PH").Append("PT").Append("RS")
        .Append("TH").Append("TW").Append("VN")
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static string GetFavoriteKey(Server server) => server.XrayProfile is { } profile
        ? "profile:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profile.GetRawText()))).ToLowerInvariant()
        : CreateFavoriteKey(server.Type, server.Hostname, server.Port);

    public static string CreateFavoriteKey(string? protocol, string? hostname, ushort port)
    {
        var normalized = $"{protocol?.Trim().ToUpperInvariant()}\n{NormalizeHostname(hostname)}\n{port}";
        return FavoriteKeyVersion + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }

    public static string? ResolveCountryCode(string? name, string? group, string? hostname)
    {
        var text = $"{name} {group}";
        var emojiCode = ResolveFlagEmoji(text);
        if (emojiCode is not null)
            return emojiCode;

        foreach (var (token, code) in CountryTokens)
            if (text.Contains(token, StringComparison.OrdinalIgnoreCase))
                return code;

        var suffix = NormalizeHostname(hostname).Split('.').LastOrDefault();
        if (suffix is { Length: 2 } && CountryCodes.Contains(suffix))
            return suffix.ToUpperInvariant();
        return GeoIpCountries.GetValueOrDefault(NormalizeHostname(hostname));
    }

    public static void CacheGeoIpCountry(string hostname, string countryCode)
    {
        if (countryCode.Length == 2 && countryCode.All(char.IsAsciiLetter))
            GeoIpCountries[NormalizeHostname(hostname)] = countryCode.ToUpperInvariant();
    }

    private static string NormalizeHostname(string? hostname) => hostname?.Trim().TrimEnd('.').ToLowerInvariant() ?? string.Empty;

    private static string? ResolveFlagEmoji(string value)
    {
        var regional = value.EnumerateRunes()
            .Where(rune => rune.Value is >= 0x1F1E6 and <= 0x1F1FF)
            .Take(2)
            .ToArray();
        if (regional.Length != 2)
            return null;

        return string.Create(2, regional, static (span, runes) =>
        {
            span[0] = (char)('A' + runes[0].Value - 0x1F1E6);
            span[1] = (char)('A' + runes[1].Value - 0x1F1E6);
        });
    }
}
