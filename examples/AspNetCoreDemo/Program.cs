// Minimal ASP.NET Core app with Secret Keeper sign-in: routes under /api/sk,
// a page with the widget at /, a cookie session, and /me that reads it.
//
//   SK_SERVER_MNEMONIC="word1 … word12" dotnet run --project examples/AspNetCoreDemo
//
// Without a mnemonic in the environment a random one is generated for the lifetime of the process.
// DEMO_FAKE_PHONE=1 enables POST /demo/phone?sid=…: an emulation of the app
// to complete a sign-in without a phone (development only).

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Paymastech.SkLogin;
using Paymastech.SkLogin.AspNetCore;
using Paymastech.SkLogin.Crypto;

if (args.Contains("--mnemonic"))
{
    // A new server mnemonic for SK_SERVER_MNEMONIC.
    Console.WriteLine(string.Join(' ', Identity.GenerateMnemonic()));
    return;
}

var builder = WebApplication.CreateBuilder(args);

var mnemonic = Environment.GetEnvironmentVariable("SK_SERVER_MNEMONIC") ?? string.Join(' ', Identity.GenerateMnemonic());
var sessions = new ConcurrentDictionary<string, User>();

builder.Services.AddSkLogin<User>(o =>
{
    o.Mnemonic = mnemonic;
    // SK_HUB=auth_secretkeeper switches the QR to hub mode (see README, "Hub").
    o.Target = new SkLoginTarget
    {
        Id = Environment.GetEnvironmentVariable("SK_TARGET") ?? "demo",
        Hub = Environment.GetEnvironmentVariable("SK_HUB"),
        // SK_OWNER_ADDRESS: your own sk1… address; its hash in GET target lets you register the service in the hub catalog.
        Owner = Environment.GetEnvironmentVariable("SK_OWNER_ADDRESS"),
    };
    // The demo admits everyone: a real service would look up the user by sk1… address, check an allowlist or link to an account here.
    o.Access = address => ValueTask.FromResult(AccessDecision<User>.Granted(new User(address)));
    // The browser learned about admission: issue a cookie session. The return value goes into the JSON (the widget passes it to onSuccess).
    o.OnAuthenticated = (user, http) =>
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        sessions[token] = user;
        http.Response.Cookies.Append("demo_session", token, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Lax, Path = "/" });
        return ValueTask.FromResult<object?>(new { address = user.Address });
    };
});

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapSkLogin<User>("/api/sk");

app.MapGet("/me", (HttpContext http) =>
{
    var token = http.Request.Cookies["demo_session"];
    var user = token is not null && sessions.TryGetValue(token, out var u) ? u : null;
    return Results.Json(new { user });
});

app.MapGet("/logout", (HttpContext http) =>
{
    var token = http.Request.Cookies["demo_session"];
    if (token is not null) sessions.TryRemove(token, out _);
    http.Response.Cookies.Delete("demo_session");
    return Results.Redirect("/");
});

if (Environment.GetEnvironmentVariable("DEMO_FAKE_PHONE") is not null)
{
    // Emulation of the Secret Keeper app: request, challenge parsing, code or cancel.
    var phone = Identity.DeriveIdentityKeys(Identity.GenerateMnemonic());
    app.MapPost("/demo/phone", async (string sid, string? show, string? cancel, SkLoginService<User> sk) =>
    {
        var target = sk.Options.Target!.Id;
        string Env(string type, string text = "", int challenge = 1) => Envelope.Encrypt(phone, sk.ServerAddress, text, SkLogin.LoginMeta(target, type, sid, challenge));
        try
        {
            var reply = await sk.HandleEnvelopeAsync(Env("sk-login", challenge: 2), Lang.Ru);
            var armored = ((EnvelopeReply.Challenge)reply).Armored;
            var content = Envelope.Decrypt(phone.X25519Private, armored);
            var challenge = JsonDocument.Parse(content.Text).RootElement;
            var code = challenge.GetProperty("code").GetString()!;
            var items = challenge.GetProperty("items").Clone();
            if (cancel is not null)
            {
                await sk.HandleEnvelopeAsync(Env("sk-login-cancel"), Lang.Ru);
                return Results.Json(new { cancelled = true, items });
            }
            if (show is not null) return Results.Json(new { code, items });
            await sk.HandleEnvelopeAsync(Env("sk-login-code", code), Lang.Ru);
            return Results.Json(new { sent = true, items, address = phone.Address });
        }
        catch (LoginException e)
        {
            return Results.Json(new { error = e.Code, message = e.Message }, statusCode: e.Status);
        }
    });
}

app.Lifetime.ApplicationStarted.Register(() =>
    Console.WriteLine($"demo: {app.Urls.FirstOrDefault() ?? "http://localhost:5000"}/  target: /api/sk/target"));
app.Run();

record User(string Address);
