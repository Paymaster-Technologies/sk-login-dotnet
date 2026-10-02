namespace Paymastech.SkLogin;

/// <summary>Languages of human-facing messages, same as in the app.</summary>
public enum Lang
{
    En,
    Ru,
}

/// <summary>
/// Messages the server shows to a person: the access refusal (displayed by the
/// Secret Keeper app on behalf of the service and by the browser popup) and the
/// request context labels. The service can override any message via options.
/// </summary>
public sealed record Messages
{
    /// <summary>The address is proven but there is no access. One message for all reasons.</summary>
    public required string AccessDenied { get; init; }
    public required string From { get; init; }
    public required string Ip { get; init; }
    public required string Browser { get; init; }
    public required string UnknownBrowser { get; init; }

    public static readonly Messages Ru = new()
    {
        AccessDenied = "Вход подтверждён, доступ пока не открыт. Если вас здесь ждут, следующий вход пройдёт.",
        From = "Откуда",
        Ip = "IP-адрес",
        Browser = "Браузер",
        UnknownBrowser = "неизвестный браузер",
    };

    public static readonly Messages En = new()
    {
        AccessDenied = "Sign-in confirmed, but access is not open yet. If you are expected here, your next sign-in will go through.",
        From = "Location",
        Ip = "IP address",
        Browser = "Browser",
        UnknownBrowser = "unknown browser",
    };

    public static Messages DefaultFor(Lang lang) => lang == Lang.Ru ? Ru : En;

    /// <summary>Response language from Accept-Language: the first tag, ru* means ru, anything else is en.</summary>
    public static Lang LangFromAcceptLanguage(string? header)
    {
        var first = (header ?? string.Empty).Split(',')[0].Trim().ToLowerInvariant();
        return first == "ru" || first.StartsWith("ru-", StringComparison.Ordinal) ? Lang.Ru : Lang.En;
    }

    public static Lang ParseLang(string? code) => string.Equals(code, "ru", StringComparison.OrdinalIgnoreCase) ? Lang.Ru : Lang.En;
}
