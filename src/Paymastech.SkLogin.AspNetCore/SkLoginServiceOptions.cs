using Microsoft.AspNetCore.Http;
using Paymastech.SkLogin.Crypto;

namespace Paymastech.SkLogin.AspNetCore;

/// <summary>
/// What the service does when the browser learns about admission (status with
/// authenticated, or a manually typed code): set a cookie, issue a token, etc.
/// The returned object (anonymous type, record or dictionary) is merged into the
/// JSON response to the browser; the widget passes it to onSuccess. null if there
/// is nothing to add.
/// </summary>
public delegate ValueTask<object?> OnAuthenticated<TUser>(TUser user, HttpContext http);

public sealed class SkLoginTarget
{
    /// <summary>Service id: the entry in the Secret Keeper app's skLoginTargets (direct mode) or the
    /// destination registered at the hub (hub mode).</summary>
    public required string Id { get; init; }
    /// <summary>Hub mode: the hub's target id in the app (for example auth_secretkeeper). The QR becomes
    /// target=&lt;hub&gt;&amp;destination=&lt;Id&gt;; the hub relays the app's envelopes to this server's login route.</summary>
    public string? Hub { get; init; }
    /// <summary>sk1… address of the service owner (their Secret Keeper app). Only its hash goes to GET target
    /// as ownerHash; the hub catalog lets this address register and manage the service entry. Not a secret.</summary>
    public string? Owner { get; init; }
    /// <summary>Public origin of the service for GET target (for example https://api.example.com);
    /// without it, built from the request's Host and X-Forwarded-Proto.</summary>
    public string? PublicUrl { get; init; }
}

public sealed class SkLoginServiceOptions<TUser>
{
    /// <summary>BIP-39 mnemonic of the server identity (a secret: env or vault). Alternatively <see cref="Identity"/>.</summary>
    public string? Mnemonic { get; set; }
    public IdentityKeys? Identity { get; set; }
    public SkLoginTarget? Target { get; set; }
    /// <summary>Who gets in. Called once per sign-in, after the code has been verified.</summary>
    public AccessDecider<TUser>? Access { get; set; }
    public OnAuthenticated<TUser>? OnAuthenticated { get; set; }
    /// <summary>Request store; in-process memory by default (single replica).</summary>
    public IPendingStore<TUser>? Store { get; set; }
    public TimeSpan Ttl { get; set; } = SkLogin.DefaultSidTtl;
    public Describe? Describe { get; set; }
    public GeoLookup? Geo { get; set; }
    public IReadOnlyDictionary<Lang, Messages>? Messages { get; set; }
    /// <summary>Trust X-Forwarded-For (its last element) when collecting the request context.</summary>
    public bool TrustProxy { get; set; } = true;
    /// <summary>Return the QR as SVG from init (for the widget).</summary>
    public bool Qr { get; set; } = true;
    /// <summary>Envelope body limit; real envelopes are a few hundred bytes.</summary>
    public int MaxBodyBytes { get; set; } = 16 * 1024;

    internal SkLoginOptions<TUser> ToCore()
    {
        var identity = Identity
            ?? (Mnemonic is not null ? Crypto.Identity.FromMnemonic(Mnemonic) : throw new InvalidOperationException("SkLogin: set Mnemonic or Identity"));
        if (Target is null) throw new InvalidOperationException("SkLogin: Target is required");
        if (Access is null) throw new InvalidOperationException("SkLogin: Access is required");
        return new SkLoginOptions<TUser>
        {
            Identity = identity,
            Target = Target.Id,
            Hub = Target.Hub,
            Owner = Target.Owner,
            Access = Access,
            Store = Store,
            Ttl = Ttl,
            Describe = Describe,
            Geo = Geo,
            Messages = Messages,
        };
    }
}
