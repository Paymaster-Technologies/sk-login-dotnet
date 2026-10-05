using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Paymastech.SkLogin.AspNetCore;
using Paymastech.SkLogin.Crypto;

namespace Paymastech.SkLogin.Tests;

public sealed class AspNetCoreTests : IAsyncLifetime
{
    private sealed record User(string Address, string Name);

    private IHost _host = null!;
    private HttpClient _client = null!;
    private SkLoginService<User> _sk = null!;
    private readonly HashSet<string> _blocked = new();
    private const string Owner = "sk1j5lsgzk3n2uvempjujk25xeqyx0nsch2k0tg2emg4hmcas4e659sqtsjdc";

    public async Task InitializeAsync()
    {
        _host = await new HostBuilder()
            .ConfigureWebHost(web => web.UseTestServer().ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSkLogin<User>(o =>
                {
                    o.Mnemonic = string.Join(' ', Identity.GenerateMnemonic());
                    o.Target = new SkLoginTarget { Id = "demo", PublicUrl = "https://api.example.com/", Owner = Owner };
                    o.Access = address => ValueTask.FromResult(_blocked.Contains(address)
                        ? AccessDecision<User>.Denied("blocked")
                        : AccessDecision<User>.Granted(new User(address, "Ann")));
                    o.OnAuthenticated = (user, http) =>
                    {
                        http.Response.Headers.SetCookie = $"session={user.Name}; Path=/; HttpOnly";
                        return ValueTask.FromResult<object?>(new { token = "t-" + user.Name });
                    };
                });
            }).Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(e => e.MapSkLogin<User>("/auth/sk"));
            }))
            .StartAsync();
        _client = _host.GetTestClient();
        _sk = _host.Services.GetRequiredService<SkLoginService<User>>();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;

    private IdentityKeys Phone { get; } = Identity.DeriveIdentityKeys(Identity.GenerateMnemonic());

    private Task<HttpResponseMessage> PostEnvelope(string type, string sid, string text = "", int challenge = 1) =>
        _client.PostAsync("/auth/sk/login", new StringContent(
            Envelope.Encrypt(Phone, _sk.ServerAddress, text, SkLogin.LoginMeta("demo", type, sid, challenge)),
            Encoding.UTF8, "text/plain"));

    [Fact]
    public async Task TargetDescribesTheService()
    {
        var t = await Json(await _client.GetAsync("/auth/sk/target"));
        Assert.Equal("demo", t.GetProperty("id").GetString());
        Assert.Equal(1, t.GetProperty("v").GetInt32());
        Assert.Equal("https://api.example.com/auth/sk/login", t.GetProperty("url").GetString());
        Assert.Equal(_sk.ServerAddress, t.GetProperty("serverAddress").GetString());
        Assert.Matches(@"^\d{5}( \d{5}){4}$", t.GetProperty("checkDigits").GetString());
        // Only the hash of the owner address is published; the hub catalog compares it with the signed-in address.
        Assert.Equal(SkLogin.OwnerHash(Owner), t.GetProperty("ownerHash").GetString());
        Assert.False(t.TryGetProperty("hub", out _));
    }

    [Fact]
    public async Task FullFlowThroughHttp()
    {
        var init = await Json(await _client.PostAsync("/auth/sk/init", null));
        var sid = init.GetProperty("sid").GetString()!;
        Assert.Contains("<svg", init.GetProperty("qrSvg").GetString());
        Assert.Contains($"sid={sid}", init.GetProperty("payloadUrl").GetString());

        Assert.Equal("new", (await Json(await _client.GetAsync($"/auth/sk/status?sid={sid}"))).GetProperty("state").GetString());

        var challengeRes = await PostEnvelope("sk-login", sid, challenge: 2);
        Assert.Equal(HttpStatusCode.OK, challengeRes.StatusCode);
        Assert.StartsWith("text/plain", challengeRes.Content.Headers.ContentType!.ToString());
        var content = Envelope.Decrypt(Phone.X25519Private, await challengeRes.Content.ReadAsStringAsync());
        var challenge = JsonDocument.Parse(content.Text).RootElement;
        var code = challenge.GetProperty("code").GetString()!;
        Assert.Contains(challenge.GetProperty("items").EnumerateArray(), i => i.GetProperty("name").GetString() == "Browser");

        Assert.Equal("challenged", (await Json(await _client.GetAsync($"/auth/sk/status?sid={sid}"))).GetProperty("state").GetString());

        var codeRes = await PostEnvelope("sk-login-code", sid, code);
        Assert.Equal(HttpStatusCode.OK, codeRes.StatusCode);
        Assert.True((await Json(codeRes)).GetProperty("sent").GetBoolean());

        var statusRes = await _client.GetAsync($"/auth/sk/status?sid={sid}");
        var status = await Json(statusRes);
        Assert.Equal("authenticated", status.GetProperty("state").GetString());
        Assert.Equal("t-Ann", status.GetProperty("token").GetString());
        Assert.Contains("session=Ann", statusRes.Headers.GetValues("set-cookie").Single());

        Assert.Equal("expired", (await Json(await _client.GetAsync($"/auth/sk/status?sid={sid}"))).GetProperty("state").GetString());
    }

    [Fact]
    public async Task ManualCodeAndErrors()
    {
        var sid = (await Json(await _client.PostAsync("/auth/sk/init", null))).GetProperty("sid").GetString()!;
        var challengeRes = await PostEnvelope("sk-login", sid);
        var code = Envelope.Decrypt(Phone.X25519Private, await challengeRes.Content.ReadAsStringAsync()).Text;

        var wrong = await _client.PostAsJsonAsync("/auth/sk/code", new { sid, code = "000000" });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal("code-invalid", (await Json(wrong)).GetProperty("error").GetString());

        var ok = await _client.PostAsJsonAsync("/auth/sk/code", new { sid, code });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var body = await Json(ok);
        Assert.True(body.GetProperty("ok").GetBoolean());
        Assert.Equal("t-Ann", body.GetProperty("token").GetString());

        var garbage = await _client.PostAsync("/auth/sk/login", new StringContent("hello", Encoding.UTF8, "text/plain"));
        Assert.Equal(HttpStatusCode.BadRequest, garbage.StatusCode);
        Assert.Equal("bad-envelope", (await Json(garbage)).GetProperty("error").GetString());

        var huge = await _client.PostAsync("/auth/sk/login", new StringContent(new string('a', 20_000), Encoding.UTF8, "text/plain"));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, huge.StatusCode);

        var noSid = await _client.GetAsync("/auth/sk/status");
        Assert.Equal(HttpStatusCode.BadRequest, noSid.StatusCode);
    }

    [Fact]
    public async Task DeniedAddressGets403WithLocalizedText()
    {
        _blocked.Add(Phone.Address);
        var sid = (await Json(await _client.PostAsync("/auth/sk/init", null))).GetProperty("sid").GetString()!;
        var challengeRes = await PostEnvelope("sk-login", sid);
        var code = Envelope.Decrypt(Phone.X25519Private, await challengeRes.Content.ReadAsStringAsync()).Text;

        // Via the app: 403 with the message in the request language, browser status is denied.
        var viaApp = new HttpRequestMessage(HttpMethod.Post, "/auth/sk/login")
        {
            Content = new StringContent(Envelope.Encrypt(Phone, _sk.ServerAddress, code, SkLogin.LoginMeta("demo", "sk-login-code", sid)), Encoding.UTF8, "text/plain"),
        };
        viaApp.Headers.AcceptLanguage.ParseAdd("ru-RU");
        var res = await _client.SendAsync(viaApp);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        var body = await Json(res);
        Assert.Equal("access-denied", body.GetProperty("error").GetString());
        Assert.Equal(Messages.Ru.AccessDenied, body.GetProperty("message").GetString());
        var status = await Json(await _client.GetAsync($"/auth/sk/status?sid={sid}"));
        Assert.Equal("denied", status.GetProperty("state").GetString());
        Assert.Equal("blocked", status.GetProperty("reason").GetString());

        // Via a manually typed code: 403 {denied, reason, message}.
        var sid2 = (await Json(await _client.PostAsync("/auth/sk/init", null))).GetProperty("sid").GetString()!;
        var code2 = Envelope.Decrypt(Phone.X25519Private, await (await PostEnvelope("sk-login", sid2)).Content.ReadAsStringAsync()).Text;
        var manual = await _client.PostAsJsonAsync("/auth/sk/code", new { sid = sid2, code = code2 });
        Assert.Equal(HttpStatusCode.Forbidden, manual.StatusCode);
        var m = await Json(manual);
        Assert.True(m.GetProperty("denied").GetBoolean());
        Assert.Equal("blocked", m.GetProperty("reason").GetString());
    }
}
