# Sign in with Secret Keeper for .NET (SK login)

Passwordless sign-in for a website: the user scans a QR code with the
Secret Keeper app (or opens it with a button on the phone), confirms in
the app, and the server receives their `sk1…` address and decides who
gets in. The server and the app exchange encrypted envelopes; the server
sees only the user's address.

This is the .NET implementation of the same protocol as
[paymastech/sk-login](https://github.com/paymastech/sk-login) (Node:
core, NestJS module and browser widget). The servers are interchangeable:
the widget from that repository works with this server unchanged, and
the cryptography is verified against the app with the same test vectors.

| Package | What it is |
| --- | --- |
| [`Paymastech.SkLogin`](src/Paymastech.SkLogin) | The protocol without any web framework dependency: envelope cryptography (X25519, XChaCha20-Poly1305, BIP-39, bech32), requests, challenge code, verification, request store. `net7.0`, `net8.0`. |
| [`Paymastech.SkLogin.AspNetCore`](src/Paymastech.SkLogin.AspNetCore) | `AddSkLogin` + `MapSkLogin`: `/sk/login` for the app, four browser endpoints for the widget, QR as SVG. Minimal APIs, also suitable for apps built on MVC controllers. |
| [`examples/AspNetCoreDemo`](examples/AspNetCoreDemo) | A working application: module + widget + cookie session, plus a phone emulation for development. |

## Quick start

```bash
dotnet add package Paymastech.SkLogin.AspNetCore
```

Until the packages are published to NuGet, the `.nupkg` files are attached to the
[release](https://github.com/paymastech/sk-login-dotnet/releases/latest):
download both into a folder and add it as a source
(`dotnet nuget add source ./packages -n sk-login-local`).

```csharp
using Paymastech.SkLogin;
using Paymastech.SkLogin.AspNetCore;

builder.Services.AddSkLogin<User>(o =>
{
    o.Mnemonic = builder.Configuration["SK_SERVER_MNEMONIC"];   // 12 BIP-39 words, see "Secrets"
    o.Target = new SkLoginTarget
    {
        Site = "api.example.com",            // your public host: goes into the QR, see "How the app finds your server"
        PublicUrl = "https://api.example.com",
    };
    // Who gets in: called once after confirmation, receives the sk1… address
    o.Access = async address =>
    {
        var user = await users.FindByAddressAsync(address);
        return user is null ? AccessDecision<User>.Denied("unknown-address") : AccessDecision<User>.Granted(user);
    };
    // The browser learned about admission: issue a session. The return value is merged into the JSON response.
    o.OnAuthenticated = async (user, http) =>
    {
        await http.SignInAsync(principalFor(user));        // or your own cookie/JWT
        return new { userId = user.Id };
    };
});

var app = builder.Build();
app.MapSkLogin<User>("/api/sk");   // POST /sk/login for the app, browser routes under /api/sk
```

For access to the container there is an overload `AddSkLogin<User>((sp, o) => …)`.
The `/sk/login` endpoint reads the `text/plain` body itself; body size limits
and MVC binding do not apply to it.

Sign-in page: the widget comes from
[`@paymastech/sk-login-widget`](https://github.com/paymastech/sk-login/tree/main/packages/widget)
(npm package with an ESM build, or the single file `sk-login-widget.global.js`
with no framework; a copy of the latter lives in `examples/AspNetCoreDemo/wwwroot`):

```html
<script src="/sk-login-widget.js"></script>
<button id="login">Sign in</button>
<script>
  const login = SkLoginWidget.mountSkLogin({
    apiBase: '/api/sk',
    lang: 'en',
    onSuccess: (extra) => location.reload(),   // extra = what OnAuthenticated returned
  });
  document.getElementById('login').onclick = () => login.open();
</script>
```

## How the app finds your server

The QR (and the `sk://` link) carries the sign-in request together with
who you are: `https://secretkeeper.net/auth?v=1&sid=…&site=<host>&address=<sk1…>`.
The app shows `<host>` to the user, encrypts its envelopes to `<address>`
and posts them to `https://<host>/sk/login`: the endpoint is derived from
the host by convention (protocol § 4.5), there is no registry and no
release of the app per service. The address in the QR is treated like the
address in a contact's QR: it only says whom to encrypt to, and the two-step
challenge proves that the server holds the matching key.

So a service needs three things:

1. A server mnemonic (once, keep it as a secret):
   `string.Join(' ', Identity.GenerateMnemonic())` or from the demo:
   `dotnet run --project examples/AspNetCoreDemo -- --mnemonic`.
2. `Target = new SkLoginTarget { Site = "<host>" }`, where `<host>` is the
   public host of your site in the form the app accepts: lowercase ASCII,
   at least one dot, no scheme, port or path; IDN hosts in punycode.
   `localhost` and IP literals are not sites.
3. `POST https://<host>/sk/login` reachable from the internet over HTTPS
   (`MapSkLogin` maps it at the root, whatever prefix you choose for the
   browser routes). The app checks nothing else: `GET target` is a public
   description for people and tooling.

Apps built into Secret Keeper (whose URL and server address ship with the
app) use `Target = new SkLoginTarget { Id = "<id>" }` instead; the QR then
carries `target=<id>`. A site that moved from such an embedded entry to
`Site` lists the old id in `LegacyTargets`: older app builds still send it
to the old `<prefix>/login` route, which `MapSkLogin` keeps as an alias.

## Secrets and environment

- `SK_SERVER_MNEMONIC`: 12 words. The server keys are derived from it. A
  leak means the ability to impersonate the service to the app. Keep it in
  a secret manager / user-secrets, never log it. Instead of a mnemonic you
  can pass a ready `Identity` (`Identity.DeriveIdentityKeys`).
- The module makes no network calls: everything happens between your
  server, the browser and the user's phone.

## HTTP API

The app's route is fixed by the protocol; the browser routes live under the
prefix you pass to `MapSkLogin` (`/api/sk` by default).

| Method and path | Caller | Request | Response |
| --- | --- | --- | --- |
| `POST /sk/login` | app | `text/plain`, envelope | challenge envelope as `text/plain` (for `sk-login`), `{ sent: true }` (for `sk-login-code`), `204` (for `sk-login-cancel`) or `4xx { error, message }` |
| `POST <prefix>/login` | older app builds | same | same; alias of `/sk/login` for an embedded id listed in `LegacyTargets` |
| `POST <prefix>/init` | browser | empty | `{ sid, payloadUrl, schemeUrl, expiresAt, ttlMs, qrSvg? }` |
| `GET <prefix>/status?sid=` | browser | | `{ state, reason?, ...extra }`, `state`: `new`, `challenged`, `authenticated`, `denied`, `cancelled`, `expired` |
| `POST <prefix>/code` | browser | `{ sid, code }` | `{ ok: true, ...extra }`, `403 { denied, reason, message }` or `4xx { error, message }` |
| `GET <prefix>/target` | people | | `{ id, site?, v, url, serverAddress, checkDigits }` |

| `error` code | Status | When |
| --- | --- | --- |
| `bad-envelope` | 400 / 413 | the body is not an envelope, failed to decrypt, is addressed to another server, or is too large |
| `bad-meta` | 400 | meta lacks `type`/`sid`, the target is another service (neither `Site`/`Id` nor a `LegacyTargets` entry) or the type is unknown |
| `in-progress` | 409 | a request from another address is already in progress for this sid, or a code/cancel arrived without a request |
| `sid-expired` | 404 | sid not found or expired (2 minutes by default) |
| `code-invalid` | 400 / 410 | the code did not match; after 5 attempts the request is closed (410) |
| `access-denied` | 403 | `Access` returned `Denied`; `reason` carries its reason |

`OnAuthenticated` is called exactly once per request: on the first
`status` with `authenticated` or on a successful `code`. Further `status`
calls for that sid answer `expired`.

## Options

| Option | Default | Meaning |
| --- | --- | --- |
| `Mnemonic` or `Identity` | one is required | server identity |
| `Target.Site` | one of `Site`/`Id` | the site's host; goes into the QR with the server address and into `meta.data.target` |
| `Target.Id` | one of `Site`/`Id` | embedded target id for apps built into Secret Keeper; the QR carries `target=<id>` |
| `Target.LegacyTargets` | | other `meta.data.target` values to accept, e.g. the embedded id a site had before `Site` |
| `Target.PublicUrl` | from `Host` and `X-Forwarded-Proto` | origin of the `url` in `GET target` |
| `Access(address)` | required | `AccessDecision<T>.Granted(user)` or `.Denied(reason, message?)` |
| `OnAuthenticated(user, HttpContext)` | | session, cookie, token; the return value goes into the JSON for the browser |
| `Store` | `MemoryPendingStore<T>` | request store, see "Multiple replicas" |
| `Ttl` | 2 minutes | sid lifetime |
| `TrustProxy` | `true` | take the IP from the last `X-Forwarded-For` element |
| `Geo(ip)` | | city and country for the context shown in the app |
| `Describe(ctx, lang)` | built-in | custom context lines |
| `Messages` | ru/en | override messages (for example `AccessDenied`) |
| `Qr` | `true` | include `qrSvg` in `init` (QRCoder) |
| `MaxBodyBytes` | 16 KiB | envelope body limit |

`SkLoginService<TUser>` is registered in DI: through it you can call
`Core.InitAsync / PollAsync / SubmitCodeAsync` from your own controllers
if the standard endpoints do not fit (for example, to finish the sign-in
inside your own SSO pipeline).

## Multiple replicas

A request lives for 2 minutes and spans three calls from two clients (browser
and phone). With `MemoryPendingStore` they must land on the same process.
With several instances you need sticky sessions by `sid` or your own
`IPendingStore<TUser>` on top of Redis or a database:

```csharp
public interface IPendingStore<TUser>
{
    Task<Pending<TUser>?> GetAsync(string sid, CancellationToken ct = default);
    Task SetAsync(Pending<TUser> entry, TimeSpan ttl, CancellationToken ct = default);
    Task DeleteAsync(string sid, CancellationToken ct = default);
}
```

`Pending<TUser>` is serialized by `System.Text.Json` as is; `SetAsync`
is `SET sid json PX ttl`.

## Development

```bash
dotnet test                                   # 35 tests: app vectors, protocol, HTTP
dotnet run --project examples/AspNetCoreDemo  # http://localhost:5000, random mnemonic
DEMO_FAKE_PHONE=1 dotnet run --project examples/AspNetCoreDemo   # plus POST /demo/phone?sid=…
dotnet pack -c Release -o ./artifacts
```

Test vectors: `tests/Paymastech.SkLogin.Tests/Fixtures/test_vectors.json`
(generator `secret_keeper/tools/test_vectors`) and `app_fixtures.json`
(envelopes recorded by the app itself). Compatibility with the Node core
has also been verified with a live run: a "phone" built on
`@paymastech/sk-login-core` completes a sign-in against this server.

## License

MIT.
