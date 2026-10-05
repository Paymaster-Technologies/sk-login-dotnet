using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Paymastech.SkLogin.Crypto;

namespace Paymastech.SkLogin;

/// <summary>Refusal reason code in the JSON error returned to the app and the browser.</summary>
public static class RefusalCode
{
    public const string BadEnvelope = "bad-envelope";
    public const string BadMeta = "bad-meta";
    public const string InProgress = "in-progress";
    public const string SidExpired = "sid-expired";
    public const string CodeInvalid = "code-invalid";
    public const string AccessDenied = "access-denied";
}

/// <summary>Protocol error: HTTP status, reason code and a message for the user;
/// access-denied also carries the service's refusal reason (for the browser).</summary>
public sealed class LoginException : Exception
{
    public int Status { get; }
    public string Code { get; }
    public string? Reason { get; }

    public LoginException(int status, string code, string message, string? reason = null) : base(message)
    {
        Status = status;
        Code = code;
        Reason = reason;
    }
}

/// <summary>The service's decision on admitting a proven address.</summary>
public sealed class AccessDecision<TUser>
{
    public bool IsGranted { get; }
    public TUser? User { get; }
    /// <summary>Code for the service and the browser (for example unknown, blocked).</summary>
    public string? Reason { get; }
    /// <summary>Per-language message for the user, replacing the generic one.</summary>
    public IReadOnlyDictionary<Lang, string>? Message { get; }

    private AccessDecision(bool granted, TUser? user, string? reason, IReadOnlyDictionary<Lang, string>? message)
    {
        IsGranted = granted;
        User = user;
        Reason = reason;
        Message = message;
    }

    public static AccessDecision<TUser> Granted(TUser user) => new(true, user, null, null);
    public static AccessDecision<TUser> Denied(string reason, IReadOnlyDictionary<Lang, string>? message = null) => new(false, default, reason, message);
}

/// <summary>Who gets in. Called once per sign-in, after the code has been verified.</summary>
public delegate ValueTask<AccessDecision<TUser>> AccessDecider<TUser>(string address);

public sealed class SkLoginOptions<TUser>
{
    /// <summary>Server identity; its sk1… address is recorded in the app's target list.</summary>
    public required IdentityKeys Identity { get; init; }
    /// <summary>Target id: the service id in the app's skLoginTargets (direct mode) or its
    /// destination in the hub registry (hub mode). Envelopes with another target are rejected.</summary>
    public required string Target { get; init; }
    /// <summary>Hub mode: the hub's target id in the app (for example auth_secretkeeper). The QR then
    /// carries target=&lt;hub&gt;&amp;destination=&lt;Target&gt;, the app sends envelopes through the hub and
    /// the hub relays them to this server. The inner envelope and its meta do not change.</summary>
    public string? Hub { get; init; }
    /// <summary>sk1… address of the person who owns this service (their Secret Keeper app). Only its
    /// hash is published by GET target as ownerHash: the hub catalog lets exactly this address register
    /// and edit the service entry after signing in to the catalog.</summary>
    public string? Owner { get; init; }
    public required AccessDecider<TUser> Access { get; init; }
    public IPendingStore<TUser>? Store { get; init; }
    public TimeSpan Ttl { get; init; } = SkLogin.DefaultSidTtl;
    /// <summary>Context lines for challenge v2; by default IP, browser and OS, plus "Location" when Geo is given.</summary>
    public Describe? Describe { get; init; }
    public GeoLookup? Geo { get; init; }
    /// <summary>Per-language message overrides.</summary>
    public IReadOnlyDictionary<Lang, Messages>? Messages { get; init; }
    public Func<long>? Now { get; init; }
}

public sealed record InitResult(string Sid, string PayloadUrl, string SchemeUrl, long ExpiresAt, long TtlMs);

public abstract record EnvelopeReply
{
    /// <summary>Reply to sk-login: the challenge envelope, to be returned as text/plain.</summary>
    public sealed record Challenge(string Armored) : EnvelopeReply;
    /// <summary>Reply to sk-login-code: the code matched and the address was admitted.</summary>
    public sealed record CodeAccepted(string Address) : EnvelopeReply;
    /// <summary>Reply to sk-login-cancel.</summary>
    public sealed record Cancelled : EnvelopeReply;
}

/// <summary>State for the browser: new, challenged, authenticated, denied, cancelled, expired.</summary>
public sealed record PollResult<TUser>(string State, TUser? User = default, string? Reason = null);

public static class SkLogin
{
    public const int Version = 1;
    public const string AuthUrl = "https://secretkeeper.net/auth";
    public const string AuthSchemeUrl = "sk://auth";
    public static readonly TimeSpan DefaultSidTtl = TimeSpan.FromMinutes(2);
    public const int MaxCodeAttempts = 5;
    internal const int CodeLength = 6;

    /// <summary>meta of login envelopes; challenge is written only for v2 so that v1 envelopes stay unchanged.</summary>
    public static string LoginMeta(string target, string type, string sid, int challenge = Context.ChallengeV1)
    {
        var data = new Dictionary<string, object> { ["target"] = target, ["v"] = Version, ["sid"] = sid };
        if (challenge > Context.ChallengeV1) data["challenge"] = challenge;
        return JsonSerializer.Serialize(new Dictionary<string, object> { ["type"] = type, ["data"] = data });
    }

    /// <summary>Hash of an owner address as published in GET target (ownerHash): base64url of SHA-256 over
    /// the address text, no padding. Same as ownerHash() in the Node core: the hub catalog compares it with
    /// the hash of the signed-in address, the address itself stays private.</summary>
    public static string OwnerHash(string address)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(address));
        return Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static string StateName(PendingState state) => state switch
    {
        PendingState.New => "new",
        PendingState.Challenged => "challenged",
        PendingState.Authenticated => "authenticated",
        PendingState.Denied => "denied",
        PendingState.Cancelled => "cancelled",
        _ => "used",
    };
}

/// <summary>
/// Sign-in via Secret Keeper, server side (secret_keeper/docs/protocol.md § 4.5).
/// Browser: Init -> QR -> Poll. App: HandleEnvelope(sk-login) -> challenge,
/// HandleEnvelope(sk-login-code) -> admission. Code typed in the browser: SubmitCode.
/// </summary>
public sealed class SkLogin<TUser>
{
    private readonly IdentityKeys _identity;
    private readonly AccessDecider<TUser> _access;
    private readonly IPendingStore<TUser> _store;
    private readonly Describe _describe;
    private readonly Func<long> _now;
    private readonly Dictionary<Lang, Messages> _messages;

    public string Target { get; }
    /// <summary>Hub target id in hub mode, otherwise null.</summary>
    public string? Hub { get; }
    /// <summary>Owner address when configured, otherwise null.</summary>
    public string? Owner { get; }
    /// <summary>What GET target publishes for the owner: <see cref="SkLogin.OwnerHash"/> of <see cref="Owner"/>, or null.</summary>
    public string? OwnerHash => Owner is null ? null : SkLogin.OwnerHash(Owner);
    public TimeSpan Ttl { get; }
    public string ServerAddress => _identity.Address;
    public IReadOnlyDictionary<Lang, Messages> Messages => _messages;

    public SkLogin(SkLoginOptions<TUser> options)
    {
        _identity = options.Identity;
        Target = options.Target;
        Hub = string.IsNullOrEmpty(options.Hub) ? null : options.Hub;
        Owner = string.IsNullOrEmpty(options.Owner) ? null : options.Owner;
        _access = options.Access;
        Ttl = options.Ttl;
        _now = options.Now ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        _store = options.Store ?? new MemoryPendingStore<TUser>(_now);
        _messages = new Dictionary<Lang, Messages>
        {
            [Lang.En] = options.Messages?.GetValueOrDefault(Lang.En) ?? global::Paymastech.SkLogin.Messages.En,
            [Lang.Ru] = options.Messages?.GetValueOrDefault(Lang.Ru) ?? global::Paymastech.SkLogin.Messages.Ru,
        };
        var geo = options.Geo;
        _describe = options.Describe ?? ((ctx, lang) => Context.Describe(ctx, lang, _messages[lang], geo));
    }

    /// <summary>Step 1: a new request; <paramref name="ctx"/> is the browser that opened it.</summary>
    public async Task<InitResult> InitAsync(RequestContext? ctx = null, CancellationToken ct = default)
    {
        var sid = Base64Url(RandomNumberGenerator.GetBytes(24));
        var createdAt = _now();
        var expiresAt = createdAt + (long)Ttl.TotalMilliseconds;
        await SaveAsync(new Pending<TUser> { Sid = sid, CreatedAt = createdAt, ExpiresAt = expiresAt, State = PendingState.New, Ctx = ctx }, ct);
        var query = Hub is null
            ? $"v={SkLogin.Version}&sid={Uri.EscapeDataString(sid)}&target={Uri.EscapeDataString(Target)}"
            : $"v={SkLogin.Version}&sid={Uri.EscapeDataString(sid)}&target={Uri.EscapeDataString(Hub)}&destination={Uri.EscapeDataString(Target)}";
        return new InitResult(sid, $"{SkLogin.AuthUrl}?{query}", $"{SkLogin.AuthSchemeUrl}?{query}", expiresAt, (long)Ttl.TotalMilliseconds);
    }

    /// <summary>Steps 3-4: an envelope from the app (request, code or cancel). <paramref name="lang"/>: language of the refusal message.</summary>
    /// <exception cref="LoginException">status, code and message for the JSON error</exception>
    public async Task<EnvelopeReply> HandleEnvelopeAsync(string body, Lang lang = Lang.En, CancellationToken ct = default)
    {
        string armored;
        try
        {
            armored = Envelope.ExtractArmor(body);
        }
        catch (FormatException)
        {
            throw new LoginException(400, RefusalCode.BadEnvelope, "body is not a Secret Keeper envelope");
        }
        EnvelopeContent content;
        try
        {
            content = Envelope.Decrypt(_identity.X25519Private, armored);
        }
        catch (FormatException)
        {
            throw new LoginException(400, RefusalCode.BadEnvelope, "envelope is not addressed to this server or is damaged");
        }
        // After a successful decryption the address from the header is authenticated:
        // the slot KEK includes an ECDH with the sender's static key.
        var sender = Envelope.SenderAddress(armored);
        var meta = ParseMeta(content.Meta);
        var entry = await LiveAsync(meta.Sid, ct);

        switch (meta.Type)
        {
            case "sk-login":
            {
                if (entry.State != PendingState.New)
                    throw new LoginException(409, RefusalCode.InProgress, "this sign-in request is already in progress");
                entry.State = PendingState.Challenged;
                entry.Sender = sender;
                entry.Code = GenerateCode();
                entry.ExpiresAt = _now() + (long)Ttl.TotalMilliseconds;
                var (plaintext, cv) = Context.ChallengePlaintext(entry.Code, entry.Ctx, lang, meta.Challenge, _describe);
                var challenge = Envelope.Encrypt(_identity, sender, plaintext, SkLogin.LoginMeta(Target, "sk-login-challenge", entry.Sid, cv), _now());
                await SaveAsync(entry, ct);
                return new EnvelopeReply.Challenge(challenge);
            }
            case "sk-login-code":
            {
                if (entry.State != PendingState.Challenged || entry.Sender != sender)
                    throw new LoginException(409, RefusalCode.InProgress, "no challenge is waiting for this sender");
                await CheckCodeAsync(entry, content.Text, ct);
                // The code matched: the address is proven, so decide on admission right here so that a refusal reaches the app.
                entry.State = PendingState.Authenticated;
                entry.User = await AdmitAsync(entry, sender, lang, ct);
                await SaveAsync(entry, ct);
                return new EnvelopeReply.CodeAccepted(sender);
            }
            case "sk-login-cancel":
            {
                // Only the sender of the request may cancel, and only while we are waiting for the code.
                if (entry.State != PendingState.Challenged || entry.Sender != sender)
                    throw new LoginException(409, RefusalCode.InProgress, "nothing to cancel for this sender");
                entry.State = PendingState.Cancelled;
                entry.Code = null;
                await SaveAsync(entry, ct);
                return new EnvelopeReply.Cancelled();
            }
            default:
                throw new LoginException(400, RefusalCode.BadMeta, $"unexpected envelope type: {meta.Type}");
        }
    }

    /// <summary>Code typed into the browser: the admitted user is returned immediately and the request is closed.</summary>
    /// <exception cref="LoginException">wrong code (400/410) or refusal (403)</exception>
    public async Task<TUser> SubmitCodeAsync(string sid, string code, Lang lang = Lang.En, CancellationToken ct = default)
    {
        var entry = await LiveAsync(sid, ct);
        if (entry.State != PendingState.Challenged || entry.Sender is null)
            throw new LoginException(409, RefusalCode.InProgress, "no challenge is waiting for this request");
        await CheckCodeAsync(entry, code, ct);
        entry.State = PendingState.Used;
        var user = await AdmitAsync(entry, entry.Sender, lang, ct);
        await _store.DeleteAsync(sid, ct);
        return user;
    }

    /// <summary>Polling from the browser. authenticated is returned once (with the user): the request is closed.</summary>
    public async Task<PollResult<TUser>> PollAsync(string sid, CancellationToken ct = default)
    {
        var entry = await _store.GetAsync(sid, ct);
        var now = _now();
        var ttl = (long)Ttl.TotalMilliseconds;
        if (entry is null || entry.ExpiresAt + ttl < now || entry.State == PendingState.Used) return new PollResult<TUser>("expired");
        var open = entry.State is PendingState.New or PendingState.Challenged;
        if (open && entry.ExpiresAt < now) return new PollResult<TUser>("expired");
        if (entry.State == PendingState.Authenticated)
        {
            await _store.DeleteAsync(sid, ct);
            return new PollResult<TUser>("authenticated", entry.User);
        }
        if (entry.State == PendingState.Denied) return new PollResult<TUser>("denied", default, entry.Denied);
        return new PollResult<TUser>(SkLogin.StateName(entry.State));
    }

    private async Task CheckCodeAsync(Pending<TUser> entry, string code, CancellationToken ct)
    {
        var expected = Encoding.UTF8.GetBytes(entry.Code ?? string.Empty);
        var given = Encoding.UTF8.GetBytes(code.Trim());
        var ok = given.Length == expected.Length && CryptographicOperations.FixedTimeEquals(given, expected);
        if (!ok)
        {
            entry.CodeAttempts += 1;
            if (entry.CodeAttempts >= SkLogin.MaxCodeAttempts)
            {
                await _store.DeleteAsync(entry.Sid, ct);
                throw new LoginException(410, RefusalCode.CodeInvalid, "too many wrong codes, request a new QR");
            }
            await SaveAsync(entry, ct);
            throw new LoginException(400, RefusalCode.CodeInvalid, "wrong code");
        }
        entry.Code = null;
    }

    private async Task<TUser> AdmitAsync(Pending<TUser> entry, string address, Lang lang, CancellationToken ct)
    {
        var decision = await _access(address);
        if (decision.IsGranted) return decision.User!;
        entry.State = PendingState.Denied;
        entry.Denied = decision.Reason;
        await SaveAsync(entry, ct);
        var message = decision.Message?.GetValueOrDefault(lang) ?? decision.Message?.GetValueOrDefault(Lang.En) ?? _messages[lang].AccessDenied;
        throw new LoginException(403, RefusalCode.AccessDenied, message, decision.Reason);
    }

    private async Task<Pending<TUser>> LiveAsync(string sid, CancellationToken ct)
    {
        var entry = await _store.GetAsync(sid, ct);
        if (entry is null || entry.ExpiresAt < _now() || entry.State == PendingState.Used)
            throw new LoginException(404, RefusalCode.SidExpired, "sign-in request expired, refresh the QR code");
        return entry;
    }

    /// <summary>The entry lives one TTL past its deadline: the browser still has to pick up a resolved request.</summary>
    private Task SaveAsync(Pending<TUser> entry, CancellationToken ct) =>
        _store.SetAsync(entry, TimeSpan.FromMilliseconds(Math.Max(0, entry.ExpiresAt + (long)Ttl.TotalMilliseconds - _now())), ct);

    private (string Type, string Sid, int Challenge) ParseMeta(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) throw new LoginException(400, RefusalCode.BadMeta, "envelope has no meta");
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(raw);
        }
        catch (JsonException)
        {
            throw new LoginException(400, RefusalCode.BadMeta, "envelope meta is not JSON");
        }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object
                || !data.TryGetProperty("sid", out var sid) || sid.ValueKind != JsonValueKind.String)
                throw new LoginException(400, RefusalCode.BadMeta, "envelope meta lacks type or sid");
            var targetOk = data.TryGetProperty("target", out var target) && target.ValueKind == JsonValueKind.String && target.GetString() == Target;
            var versionOk = data.TryGetProperty("v", out var v) && NumberOf(v) == SkLogin.Version;
            if (!targetOk || !versionOk)
                throw new LoginException(400, RefusalCode.BadMeta, "envelope is meant for another service or protocol version");
            var challenge = data.TryGetProperty("challenge", out var c) ? Context.ChallengeVersion(c) : Context.ChallengeV1;
            return (type.GetString()!, sid.GetString()!, challenge);
        }
    }

    private static int? NumberOf(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Number when e.TryGetInt32(out var n) => n,
        JsonValueKind.String when int.TryParse(e.GetString(), out var s) => s,
        _ => null,
    };

    private static string GenerateCode()
    {
        var sb = new StringBuilder(SkLogin.CodeLength);
        for (var i = 0; i < SkLogin.CodeLength; i++) sb.Append((char)('0' + RandomNumberGenerator.GetInt32(10)));
        return sb.ToString();
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
