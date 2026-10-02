using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Paymastech.SkLogin;

/// <summary>What the server knows about the browser that received the sid (for challenge v2).</summary>
/// <param name="Ip">Public IP of the browser.</param>
/// <param name="Ua">User-Agent.</param>
/// <param name="Platform">Sec-CH-UA-Platform without quotes, if the browser sent it.</param>
public sealed record RequestContext(string Ip, string Ua, string? Platform = null);

public sealed record ContextItem(string Name, string Value);

/// <summary>Lines for the consent sheet built from the context; localized on the server side.</summary>
public delegate IReadOnlyList<ContextItem> Describe(RequestContext ctx, Lang lang);

public sealed record GeoPlace(string? City, string? Country);

/// <summary>Geo by IP: city (in Latin script) and ISO 3166-1 country code; null if unknown.</summary>
public delegate GeoPlace? GeoLookup(string ip);

/// <summary>Framework-agnostic access to request headers (null if the header is absent).</summary>
public delegate string? HeaderGetter(string name);

/// <summary>
/// Request context for challenge v2 (secret_keeper/docs/protocol.md § 4.5).
/// v1: challenge plaintext = the code; v2 (the app sent challenge: 2):
/// plaintext = JSON {"code", "items": [{"name", "value"}]}.
/// </summary>
public static class Context
{
    public const int ChallengeV1 = 1;
    public const int ChallengeV2 = 2;

    /// <summary>Challenge version from the request's meta.data: a missing field or garbage means v1.</summary>
    public static int ChallengeVersion(JsonElement? raw)
    {
        if (raw is null) return ChallengeV1;
        var e = raw.Value;
        if (e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var n)) return n >= ChallengeV1 ? n : ChallengeV1;
        if (e.ValueKind == JsonValueKind.String && int.TryParse(e.GetString(), out var s)) return s >= ChallengeV1 ? s : ChallengeV1;
        return ChallengeV1;
    }

    /// <summary>
    /// Context from the incoming init request. <paramref name="remoteAddress"/> is needed
    /// only without a proxy. <paramref name="trustForwarded"/>: the proxy appends the client
    /// address to the end of X-Forwarded-For; without a proxy the header is ignored.
    /// </summary>
    public static RequestContext FromHeaders(HeaderGetter header, string? remoteAddress, bool trustForwarded = true)
    {
        var platform = header("sec-ch-ua-platform");
        return new RequestContext(
            ClientIp(trustForwarded ? header("x-forwarded-for") : null, remoteAddress),
            header("user-agent") ?? string.Empty,
            string.IsNullOrEmpty(platform) ? null : platform.Trim('"'));
    }

    /// <summary>The last element of X-Forwarded-For, otherwise the connection address.</summary>
    public static string ClientIp(string? forwardedFor, string? remoteAddress)
    {
        var last = (forwardedFor ?? string.Empty).Split(',').Select(s => s.Trim()).LastOrDefault(s => s.Length > 0);
        if (last is not null) return last;
        var ip = remoteAddress ?? string.Empty;
        return ip.StartsWith("::ffff:", StringComparison.Ordinal) ? ip[7..] : ip;
    }

    /// <summary>Plaintext and challenge version matching what the app understands.</summary>
    public static (string Plaintext, int Challenge) ChallengePlaintext(string code, RequestContext? ctx, Lang lang, int wants, Describe describe)
    {
        if (wants < ChallengeV2) return (code, ChallengeV1);
        var items = ctx is null ? Array.Empty<ContextItem>() : describe(ctx, lang);
        var json = JsonSerializer.Serialize(new { code, items = items.Select(i => new { name = i.Name, value = i.Value }) }, JsonOptions);
        return (json, ChallengeV2);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Location (city, country), IP, browser and OS. Lines without data are omitted.</summary>
    public static IReadOnlyList<ContextItem> Describe(RequestContext ctx, Lang lang, Messages t, GeoLookup? geo)
    {
        var items = new List<ContextItem>();
        var place = geo?.Invoke(ctx.Ip);
        if (place is not null)
        {
            var country = place.Country is null ? null : RegionName(place.Country);
            var where = string.Join(", ", new[] { place.City, country }.Where(s => !string.IsNullOrEmpty(s)));
            if (where.Length > 0) items.Add(new ContextItem(t.From, where));
        }
        if (!string.IsNullOrEmpty(ctx.Ip)) items.Add(new ContextItem(t.Ip, ctx.Ip));
        var (browser, os) = ParseUserAgent(ctx.Ua, ctx.Platform);
        items.Add(new ContextItem(t.Browser, string.Join(", ", new[] { browser ?? t.UnknownBrowser, os }.Where(s => !string.IsNullOrEmpty(s)))));
        return items;
    }

    /// <summary>Country name from its code, in English.</summary>
    private static string RegionName(string code)
    {
        try
        {
            return new RegionInfo(code).EnglishName;
        }
        catch (ArgumentException)
        {
            return code;
        }
    }

    /// <summary>Browser and OS from the User-Agent; the Client Hints platform takes precedence.</summary>
    public static (string? Browser, string? Os) ParseUserAgent(string ua, string? platform = null)
    {
        string? browser = null;
        if (Regex.IsMatch(ua, @"\bEdg(e|A|iOS)?/")) browser = "Edge";
        else if (Regex.IsMatch(ua, @"\bYaBrowser/")) browser = "Yandex Browser";
        else if (Regex.IsMatch(ua, @"\bOPR/") || Regex.IsMatch(ua, @"\bOpera\b")) browser = "Opera";
        else if (Regex.IsMatch(ua, @"\bSamsungBrowser/")) browser = "Samsung Internet";
        else if (Regex.IsMatch(ua, @"\bFirefox/") || Regex.IsMatch(ua, @"\bFxiOS/")) browser = "Firefox";
        else if (Regex.IsMatch(ua, @"\bChrome/") || Regex.IsMatch(ua, @"\bCriOS/")) browser = "Chrome";
        else if (Regex.IsMatch(ua, @"\bSafari/") && Regex.IsMatch(ua, @"\bVersion/")) browser = "Safari";

        string? os = null;
        if (!string.IsNullOrEmpty(platform)) os = platform switch { "Chrome OS" => "ChromeOS", "Chromium" => "Linux", _ => platform };
        else if (Regex.IsMatch(ua, @"\bWindows NT\b")) os = "Windows";
        else if (Regex.IsMatch(ua, @"\bAndroid\b")) os = "Android";
        else if (Regex.IsMatch(ua, @"\b(iPhone|iPad|iPod)\b")) os = "iOS";
        else if (Regex.IsMatch(ua, @"\bMac OS X\b")) os = "macOS";
        else if (Regex.IsMatch(ua, @"\bCrOS\b")) os = "ChromeOS";
        else if (Regex.IsMatch(ua, @"\bLinux\b")) os = "Linux";
        return (browser, os);
    }
}
