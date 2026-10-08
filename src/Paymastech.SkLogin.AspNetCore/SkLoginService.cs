using Microsoft.AspNetCore.Http;
using Paymastech.SkLogin.Crypto;
using QRCoder;

namespace Paymastech.SkLogin.AspNetCore;

/// <summary>What GET target returns: a public description of the service. The protocol does not need it
/// (the app takes the host and the server address from the QR); it is for people and tooling.</summary>
/// <param name="Id">Service id: the site host or the embedded target id; what meta.data.target carries.</param>
/// <param name="Site">The site host when configured with Site, otherwise null (omitted from JSON).</param>
/// <param name="V">Protocol version.</param>
/// <param name="Url">Login endpoint URL the app posts envelopes to.</param>
/// <param name="ServerAddress">sk1… address of the server identity.</param>
/// <param name="CheckDigits">Check digits of the address for visual comparison.</param>
public sealed record TargetInfo(string Id, string? Site, int V, string Url, string ServerAddress, string CheckDigits);

/// <summary>
/// Core wrapper for ASP.NET Core: request context from HttpContext, QR for the
/// widget, target description. Registered by AddSkLogin, used by the MapSkLogin
/// endpoints; also available to your own controllers.
/// </summary>
public sealed class SkLoginService<TUser>
{
    public SkLoginServiceOptions<TUser> Options { get; }
    public SkLogin<TUser> Core { get; }
    public string ServerAddress => Core.ServerAddress;

    public SkLoginService(SkLoginServiceOptions<TUser> options)
    {
        Options = options;
        Core = new SkLogin<TUser>(options.ToCore());
    }

    /// <summary>Step 1 for the browser: sid, links and (by default) the QR as SVG.</summary>
    public async Task<Dictionary<string, object?>> InitAsync(HttpContext http, CancellationToken ct = default)
    {
        var ctx = Context.FromHeaders(
            name => http.Request.Headers.TryGetValue(name, out var v) ? v.ToString() : null,
            http.Connection.RemoteIpAddress?.ToString(),
            Options.TrustProxy);
        var init = await Core.InitAsync(ctx, ct);
        var result = new Dictionary<string, object?>
        {
            ["sid"] = init.Sid,
            ["payloadUrl"] = init.PayloadUrl,
            ["schemeUrl"] = init.SchemeUrl,
            ["expiresAt"] = init.ExpiresAt,
            ["ttlMs"] = init.TtlMs,
        };
        if (Options.Qr) result["qrSvg"] = QrSvg(init.PayloadUrl);
        return result;
    }

    public Task<EnvelopeReply> HandleEnvelopeAsync(string body, Lang lang, CancellationToken ct = default) => Core.HandleEnvelopeAsync(body, lang, ct);
    public Task<TUser> SubmitCodeAsync(string sid, string code, Lang lang, CancellationToken ct = default) => Core.SubmitCodeAsync(sid, code, lang, ct);
    public Task<PollResult<TUser>> PollAsync(string sid, CancellationToken ct = default) => Core.PollAsync(sid, ct);

    /// <summary>Public description of the service for GET target.</summary>
    public TargetInfo Target(string loginUrl) =>
        new(Core.Target, Core.Site, SkLogin.Version, loginUrl, ServerAddress, Identity.KeyCheckDigits(Core_X25519Public()));

    private byte[] Core_X25519Public() => Identity.DecodeIdentityAddress(ServerAddress);

    /// <summary>QR without quiet zones, error correction level H (the widget logo sits in the center), sized via viewBox.</summary>
    public static string QrSvg(string payload)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.H);
        return new SvgQRCode(data).GetGraphic(1, "#000000", "#ffffff", drawQuietZones: false, sizingMode: SvgQRCode.SizingMode.ViewBoxAttribute);
    }
}
