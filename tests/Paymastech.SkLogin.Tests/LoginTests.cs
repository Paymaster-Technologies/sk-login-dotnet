using System.Text.Json;
using Paymastech.SkLogin.Crypto;

namespace Paymastech.SkLogin.Tests;

/// <summary>Fake app: exactly what sendSkLogin in lib/services/sk_login.dart does.</summary>
file sealed class FakeApp
{
    public IdentityKeys Keys { get; } = Identity.DeriveIdentityKeys(Identity.GenerateMnemonic());
    private readonly string _server;
    private readonly string _target;

    public FakeApp(string server, string target = LoginTests.Target)
    {
        _server = server;
        _target = target;
    }

    public string Request(string sid, int? challenge = null) =>
        Envelope.Encrypt(Keys, _server, string.Empty, SkLogin.LoginMeta(_target, "sk-login", sid, challenge ?? Context.ChallengeV1));

    public string Code(string sid, string code) => Envelope.Encrypt(Keys, _server, code, SkLogin.LoginMeta(_target, "sk-login-code", sid));
    public string Cancel(string sid) => Envelope.Encrypt(Keys, _server, string.Empty, SkLogin.LoginMeta(_target, "sk-login-cancel", sid));

    public (string Text, JsonElement Meta) Open(EnvelopeReply reply)
    {
        var armored = Assert.IsType<EnvelopeReply.Challenge>(reply).Armored;
        var content = Envelope.Decrypt(Keys.X25519Private, armored);
        return (content.Text, JsonDocument.Parse(content.Meta!).RootElement);
    }

    public string CodeFrom(EnvelopeReply reply, string sid)
    {
        var (text, meta) = Open(reply);
        Assert.Equal("sk-login-challenge", meta.GetProperty("type").GetString());
        Assert.Equal(sid, meta.GetProperty("data").GetProperty("sid").GetString());
        Assert.False(meta.GetProperty("data").TryGetProperty("challenge", out _));
        Assert.Matches(@"^\d{6}$", text);
        return text;
    }
}

public class LoginTests
{
    public const string Target = "demo";

    private long _now = 1_700_000_000_000;
    private readonly IdentityKeys _server = Identity.DeriveIdentityKeys(Identity.GenerateMnemonic());

    private SkLogin<string> Login(Action<SkLoginOptionsBuilder>? configure = null, string? hub = null)
    {
        var b = new SkLoginOptionsBuilder();
        configure?.Invoke(b);
        return new SkLogin<string>(new SkLoginOptions<string>
        {
            Identity = _server,
            Target = Target,
            Hub = hub,
            Access = b.Access ?? (address => ValueTask.FromResult(AccessDecision<string>.Granted("user:" + address))),
            Ttl = b.Ttl ?? SkLogin.DefaultSidTtl,
            Messages = b.Messages,
            Geo = b.Geo,
            Store = b.Store,
            Now = () => _now,
        });
    }

    private sealed class SkLoginOptionsBuilder
    {
        public AccessDecider<string>? Access;
        public TimeSpan? Ttl = null;
        public IReadOnlyDictionary<Lang, Messages>? Messages;
        public GeoLookup? Geo;
        public IPendingStore<string>? Store;
    }

    [Fact]
    public async Task IssuesPayloadTheAppCanParse()
    {
        var login = Login();
        var init = await login.InitAsync();
        var uri = new Uri(init.PayloadUrl);
        Assert.Equal("https://secretkeeper.net/auth", uri.GetLeftPart(UriPartial.Path));
        Assert.Contains($"sid={init.Sid}", init.PayloadUrl);
        Assert.Contains("target=demo", init.PayloadUrl);
        Assert.Contains("v=1", init.PayloadUrl);
        Assert.StartsWith("sk://auth?", init.SchemeUrl);
        Assert.Equal(_now + 120_000, init.ExpiresAt);
        Assert.Equal("new", (await login.PollAsync(init.Sid)).State);
    }

    [Fact]
    public async Task HubModePointsThePayloadAtTheHubAndKeepsEnvelopesUnchanged()
    {
        var login = Login(hub: "auth_secretkeeper");
        var init = await login.InitAsync();
        var query = System.Web.HttpUtility.ParseQueryString(new Uri(init.PayloadUrl).Query);
        Assert.Equal("auth_secretkeeper", query["target"]);
        Assert.Equal("demo", query["destination"]);
        Assert.Equal(init.Sid, query["sid"]);
        // The envelopes the hub relays still carry the service id as target.
        var app = new FakeApp(login.ServerAddress);
        Assert.IsType<EnvelopeReply.Challenge>(await login.HandleEnvelopeAsync(app.Request(init.Sid)));
        Assert.Equal("challenged", (await login.PollAsync(init.Sid)).State);
        // An empty hub means direct mode.
        var direct = await Login(hub: "").InitAsync();
        Assert.Contains("target=demo", direct.PayloadUrl);
        Assert.DoesNotContain("destination", direct.PayloadUrl);
    }

    [Fact]
    public async Task TwoStepRequestChallengeCodeAuthenticatedOnce()
    {
        var login = Login();
        var app = new FakeApp(login.ServerAddress);
        var init = await login.InitAsync();

        var reply = await login.HandleEnvelopeAsync(app.Request(init.Sid), Lang.Ru);
        var code = app.CodeFrom(reply, init.Sid);
        Assert.Equal("challenged", (await login.PollAsync(init.Sid)).State);

        var accepted = Assert.IsType<EnvelopeReply.CodeAccepted>(await login.HandleEnvelopeAsync(app.Code(init.Sid, code)));
        Assert.Equal(app.Keys.Address, accepted.Address);

        var poll = await login.PollAsync(init.Sid);
        Assert.Equal("authenticated", poll.State);
        Assert.Equal("user:" + app.Keys.Address, poll.User);
        // The session is not issued a second time.
        Assert.Equal("expired", (await login.PollAsync(init.Sid)).State);
    }

    [Fact]
    public async Task RefusesUnknownAddressWithLocalizedMessage()
    {
        var login = Login(b => b.Access = _ => ValueTask.FromResult(AccessDecision<string>.Denied("unknown")));
        var app = new FakeApp(login.ServerAddress);
        var init = await login.InitAsync();
        var code = app.CodeFrom(await login.HandleEnvelopeAsync(app.Request(init.Sid)), init.Sid);

        var e = await Assert.ThrowsAsync<LoginException>(() => login.HandleEnvelopeAsync(app.Code(init.Sid, code), Lang.Ru));
        Assert.Equal(403, e.Status);
        Assert.Equal(RefusalCode.AccessDenied, e.Code);
        Assert.Equal("unknown", e.Reason);
        Assert.Equal(Messages.Ru.AccessDenied, e.Message);

        var poll = await login.PollAsync(init.Sid);
        Assert.Equal("denied", poll.State);
        Assert.Equal("unknown", poll.Reason);
    }

    [Fact]
    public async Task UsesDecisionMessageAndServiceOverrides()
    {
        var login = Login(b =>
        {
            b.Access = _ => ValueTask.FromResult(AccessDecision<string>.Denied("blocked", new Dictionary<Lang, string> { [Lang.En] = "Nope" }));
        });
        var app = new FakeApp(login.ServerAddress);
        var init = await login.InitAsync();
        var code = app.CodeFrom(await login.HandleEnvelopeAsync(app.Request(init.Sid)), init.Sid);
        var e = await Assert.ThrowsAsync<LoginException>(() => login.HandleEnvelopeAsync(app.Code(init.Sid, code), Lang.Ru));
        Assert.Equal("Nope", e.Message); // no ru, fall back to en

        var login2 = Login(b =>
        {
            b.Access = _ => ValueTask.FromResult(AccessDecision<string>.Denied("x"));
            b.Messages = new Dictionary<Lang, Messages> { [Lang.Ru] = Messages.Ru with { AccessDenied = "Members only" } };
        });
        var app2 = new FakeApp(login2.ServerAddress);
        var init2 = await login2.InitAsync();
        var code2 = app2.CodeFrom(await login2.HandleEnvelopeAsync(app2.Request(init2.Sid)), init2.Sid);
        var e2 = await Assert.ThrowsAsync<LoginException>(() => login2.HandleEnvelopeAsync(app2.Code(init2.Sid, code2), Lang.Ru));
        Assert.Equal("Members only", e2.Message);
    }

    [Fact]
    public async Task AcceptsCodeTypedIntoTheBrowser()
    {
        var login = Login();
        var app = new FakeApp(login.ServerAddress);
        var init = await login.InitAsync();
        var code = app.CodeFrom(await login.HandleEnvelopeAsync(app.Request(init.Sid)), init.Sid);

        var wrong = await Assert.ThrowsAsync<LoginException>(() => login.SubmitCodeAsync(init.Sid, "000000"));
        Assert.Equal(400, wrong.Status);
        Assert.Equal(RefusalCode.CodeInvalid, wrong.Code);

        Assert.Equal("user:" + app.Keys.Address, await login.SubmitCodeAsync(init.Sid, " " + code + " "));
        Assert.Equal("expired", (await login.PollAsync(init.Sid)).State);
        var again = await Assert.ThrowsAsync<LoginException>(() => login.SubmitCodeAsync(init.Sid, code));
        Assert.Equal(RefusalCode.SidExpired, again.Code);
    }

    [Fact]
    public async Task LimitsWrongCodeAttempts()
    {
        var login = Login();
        var app = new FakeApp(login.ServerAddress);
        var init = await login.InitAsync();
        app.CodeFrom(await login.HandleEnvelopeAsync(app.Request(init.Sid)), init.Sid);
        for (var i = 1; i < SkLogin.MaxCodeAttempts; i++)
            Assert.Equal(400, (await Assert.ThrowsAsync<LoginException>(() => login.SubmitCodeAsync(init.Sid, "000000"))).Status);
        var last = await Assert.ThrowsAsync<LoginException>(() => login.SubmitCodeAsync(init.Sid, "000000"));
        Assert.Equal(410, last.Status);
        Assert.Equal("expired", (await login.PollAsync(init.Sid)).State);
    }

    [Fact]
    public async Task RejectsOtherSenderAndSecondRequest()
    {
        var login = Login();
        var app = new FakeApp(login.ServerAddress);
        var other = new FakeApp(login.ServerAddress);
        var init = await login.InitAsync();
        var code = app.CodeFrom(await login.HandleEnvelopeAsync(app.Request(init.Sid)), init.Sid);

        var second = await Assert.ThrowsAsync<LoginException>(() => login.HandleEnvelopeAsync(other.Request(init.Sid)));
        Assert.Equal(409, second.Status);
        var stolen = await Assert.ThrowsAsync<LoginException>(() => login.HandleEnvelopeAsync(other.Code(init.Sid, code)));
        Assert.Equal(RefusalCode.InProgress, stolen.Code);
        Assert.IsType<EnvelopeReply.CodeAccepted>(await login.HandleEnvelopeAsync(app.Code(init.Sid, code)));
    }

    [Fact]
    public async Task ChallengeV2CarriesBrowserContext()
    {
        var login = Login(b => b.Geo = ip => ip == "77.88.8.8" ? new GeoPlace("Moscow", "RU") : null);
        var app = new FakeApp(login.ServerAddress);
        var ctx = new RequestContext("77.88.8.8", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36");
        var init = await login.InitAsync(ctx);

        var (text, meta) = app.Open(await login.HandleEnvelopeAsync(app.Request(init.Sid, Context.ChallengeV2), Lang.Ru));
        Assert.Equal(2, meta.GetProperty("data").GetProperty("challenge").GetInt32());
        var json = JsonDocument.Parse(text).RootElement;
        var code = json.GetProperty("code").GetString()!;
        Assert.Matches(@"^\d{6}$", code);
        var items = json.GetProperty("items").EnumerateArray().Select(i => (i.GetProperty("name").GetString()!, i.GetProperty("value").GetString()!)).ToList();
        Assert.Equal(new[] { ("Откуда", "Moscow, Russia"), ("IP-адрес", "77.88.8.8"), ("Браузер", "Chrome, macOS") }, items);

        Assert.IsType<EnvelopeReply.CodeAccepted>(await login.HandleEnvelopeAsync(app.Code(init.Sid, code)));
    }

    [Fact]
    public async Task V1StaysBareCodeAndV2WithoutContextHasEmptyItems()
    {
        var login = Login();
        var app = new FakeApp(login.ServerAddress);
        var init = await login.InitAsync(new RequestContext("1.2.3.4", "Mozilla/5.0 Firefox/130.0"));
        app.CodeFrom(await login.HandleEnvelopeAsync(app.Request(init.Sid)), init.Sid);

        var init2 = await login.InitAsync();
        var (text, _) = app.Open(await login.HandleEnvelopeAsync(app.Request(init2.Sid, 2)));
        Assert.Empty(JsonDocument.Parse(text).RootElement.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task RestartsTtlOnScanAndExpiresOtherwise()
    {
        var login = Login();
        var app = new FakeApp(login.ServerAddress);
        var init = await login.InitAsync();
        _now += 119_000;
        var code = app.CodeFrom(await login.HandleEnvelopeAsync(app.Request(init.Sid)), init.Sid);
        _now += 119_000; // 238 s since the request, but the scan restarted the TTL
        Assert.Equal("challenged", (await login.PollAsync(init.Sid)).State);
        _now += 2_000;
        Assert.Equal("expired", (await login.PollAsync(init.Sid)).State);
        var e = await Assert.ThrowsAsync<LoginException>(() => login.HandleEnvelopeAsync(app.Code(init.Sid, code)));
        Assert.Equal(404, e.Status);
        Assert.Equal(RefusalCode.SidExpired, e.Code);

        var stale = await login.InitAsync();
        _now += 121_000;
        Assert.Equal("expired", (await login.PollAsync(stale.Sid)).State);
    }

    [Fact]
    public async Task CustomTtlIsHonoured()
    {
        var login = Login(b => b.Ttl = TimeSpan.FromSeconds(30));
        var init = await login.InitAsync();
        Assert.Equal(30_000, init.TtlMs);
        _now += 29_000;
        Assert.Equal("new", (await login.PollAsync(init.Sid)).State);
        _now += 2_000;
        Assert.Equal("expired", (await login.PollAsync(init.Sid)).State);
    }

    [Fact]
    public async Task CancelsFromSameSenderOnlyVisibleOneMoreTtl()
    {
        var login = Login();
        var app = new FakeApp(login.ServerAddress);
        var other = new FakeApp(login.ServerAddress);
        var init = await login.InitAsync();
        app.CodeFrom(await login.HandleEnvelopeAsync(app.Request(init.Sid)), init.Sid);

        Assert.Equal(409, (await Assert.ThrowsAsync<LoginException>(() => login.HandleEnvelopeAsync(other.Cancel(init.Sid)))).Status);
        Assert.IsType<EnvelopeReply.Cancelled>(await login.HandleEnvelopeAsync(app.Cancel(init.Sid)));
        Assert.Equal("cancelled", (await login.PollAsync(init.Sid)).State);
        _now += 125_000;
        Assert.Equal("cancelled", (await login.PollAsync(init.Sid)).State);
        _now += 120_000;
        Assert.Equal("expired", (await login.PollAsync(init.Sid)).State);
    }

    [Fact]
    public async Task RejectsForeignEnvelopesAndGarbage()
    {
        var login = Login();
        var app = new FakeApp(login.ServerAddress);
        var init = await login.InitAsync();

        var garbage = await Assert.ThrowsAsync<LoginException>(() => login.HandleEnvelopeAsync("hello"));
        Assert.Equal(RefusalCode.BadEnvelope, garbage.Code);

        var otherServer = Identity.DeriveIdentityKeys(Identity.GenerateMnemonic());
        var foreign = Envelope.Encrypt(app.Keys, otherServer.Address, "", SkLogin.LoginMeta(Target, "sk-login", init.Sid));
        Assert.Equal(RefusalCode.BadEnvelope, (await Assert.ThrowsAsync<LoginException>(() => login.HandleEnvelopeAsync(foreign))).Code);

        var otherTarget = new FakeApp(login.ServerAddress, "another");
        Assert.Equal(RefusalCode.BadMeta, (await Assert.ThrowsAsync<LoginException>(() => login.HandleEnvelopeAsync(otherTarget.Request(init.Sid)))).Code);

        var noMeta = Envelope.Encrypt(app.Keys, login.ServerAddress, "hi");
        Assert.Equal(RefusalCode.BadMeta, (await Assert.ThrowsAsync<LoginException>(() => login.HandleEnvelopeAsync(noMeta))).Code);

        var badVersion = Envelope.Encrypt(app.Keys, login.ServerAddress, "", "{\"type\":\"sk-login\",\"data\":{\"target\":\"demo\",\"v\":2,\"sid\":\"" + init.Sid + "\"}}");
        Assert.Equal(RefusalCode.BadMeta, (await Assert.ThrowsAsync<LoginException>(() => login.HandleEnvelopeAsync(badVersion))).Code);

        var unknownType = Envelope.Encrypt(app.Keys, login.ServerAddress, "", SkLogin.LoginMeta(Target, "sk-data", init.Sid));
        Assert.Equal(RefusalCode.BadMeta, (await Assert.ThrowsAsync<LoginException>(() => login.HandleEnvelopeAsync(unknownType))).Code);

        var unknownSid = Envelope.Encrypt(app.Keys, login.ServerAddress, "", SkLogin.LoginMeta(Target, "sk-login", "nope"));
        Assert.Equal(RefusalCode.SidExpired, (await Assert.ThrowsAsync<LoginException>(() => login.HandleEnvelopeAsync(unknownSid))).Code);
    }

    /// <summary>A store that round-trips entries through JSON, like Redis.</summary>
    private sealed class JsonStore : IPendingStore<string>
    {
        private readonly Dictionary<string, string> _items = new();
        public Task<Pending<string>?> GetAsync(string sid, CancellationToken ct = default) =>
            Task.FromResult(_items.TryGetValue(sid, out var json) ? JsonSerializer.Deserialize<Pending<string>>(json) : null);
        public Task SetAsync(Pending<string> entry, TimeSpan ttl, CancellationToken ct = default)
        {
            Assert.True(ttl > TimeSpan.Zero);
            _items[entry.Sid] = JsonSerializer.Serialize(entry);
            return Task.CompletedTask;
        }
        public Task DeleteAsync(string sid, CancellationToken ct = default)
        {
            _items.Remove(sid);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task WorksThroughJsonRoundTrippingStore()
    {
        var login = Login(b => b.Store = new JsonStore());
        var app = new FakeApp(login.ServerAddress);
        var init = await login.InitAsync(new RequestContext("1.2.3.4", "UA", "macOS"));
        var code = app.CodeFrom(await login.HandleEnvelopeAsync(app.Request(init.Sid)), init.Sid);
        Assert.IsType<EnvelopeReply.CodeAccepted>(await login.HandleEnvelopeAsync(app.Code(init.Sid, code)));
        var poll = await login.PollAsync(init.Sid);
        Assert.Equal("authenticated", poll.State);
        Assert.Equal("user:" + app.Keys.Address, poll.User);
    }
}

public class ContextTests
{
    [Fact]
    public void ParsesCommonUserAgents()
    {
        Assert.Equal(("Chrome", "macOS"), Context.ParseUserAgent("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36"));
        Assert.Equal(("Safari", "iOS"), Context.ParseUserAgent("Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1"));
        Assert.Equal(("Firefox", "Windows"), Context.ParseUserAgent("Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:130.0) Gecko/20100101 Firefox/130.0"));
        Assert.Equal(("Yandex Browser", "Android"), Context.ParseUserAgent("Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 YaBrowser/24.10 Mobile Safari/537.36"));
        Assert.Equal(("Chrome", "Windows"), Context.ParseUserAgent("Mozilla/5.0 (Macintosh) Chrome/140", "Windows"));
        Assert.Equal((null, null), Context.ParseUserAgent("curl/8.0"));
    }

    [Fact]
    public void TakesLastForwardedForAndStripsIpv6Prefix()
    {
        Assert.Equal("8.8.8.8", Context.ClientIp("1.1.1.1, 8.8.8.8", "10.0.0.1"));
        Assert.Equal("10.0.0.1", Context.ClientIp(null, "::ffff:10.0.0.1"));
        Assert.Equal("::1", Context.ClientIp("", "::1"));
        var ctx = Context.FromHeaders(n => n switch { "x-forwarded-for" => "9.9.9.9", "user-agent" => "UA", "sec-ch-ua-platform" => "\"macOS\"", _ => null }, "127.0.0.1");
        Assert.Equal(new RequestContext("9.9.9.9", "UA", "macOS"), ctx);
        Assert.Equal("127.0.0.1", Context.FromHeaders(n => n == "x-forwarded-for" ? "9.9.9.9" : null, "127.0.0.1", trustForwarded: false).Ip);
    }

    [Fact]
    public void LangFromAcceptLanguage()
    {
        Assert.Equal(Lang.Ru, Messages.LangFromAcceptLanguage("ru-RU,ru;q=0.9,en;q=0.8"));
        Assert.Equal(Lang.En, Messages.LangFromAcceptLanguage("en-US,ru"));
        Assert.Equal(Lang.En, Messages.LangFromAcceptLanguage(null));
    }
}
