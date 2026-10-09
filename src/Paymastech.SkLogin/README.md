# Paymastech.SkLogin

Server side of sign-in with Secret Keeper, without any web framework dependency
(`net7.0`, `net8.0`): envelope cryptography (X25519, XChaCha20-Poly1305,
HKDF, BIP-39, bech32 on BouncyCastle), requests keyed by sid, a challenge with a
6-digit code and browser context, code verification, the admission decision,
and the request store.

Ready-made adapter: `Paymastech.SkLogin.AspNetCore`. For another host, write
an adapter on top of this package:

```csharp
var sk = new SkLogin<User>(new SkLoginOptions<User>
{
    Identity = Identity.FromMnemonic(Environment.GetEnvironmentVariable("SK_SERVER_MNEMONIC")!),
    Site = "api.example.com",   // your public host: the QR carries it with the server address, the app posts to https://<host>/sk/login
    Access = async address => await users.Has(address) ? AccessDecision<User>.Granted(await users.Get(address)) : AccessDecision<User>.Denied("unknown"),
});

// POST init (browser): QR and sid
var init = await sk.InitAsync(Context.FromHeaders(name => headers[name], remoteIp));

// POST /sk/login (phone, text/plain)
try
{
    var reply = await sk.HandleEnvelopeAsync(bodyText, Messages.LangFromAcceptLanguage(acceptLanguage));
    // Challenge -> respond with reply.Armored as text/plain; CodeAccepted -> { sent: true }; Cancelled -> 204
}
catch (LoginException e) { /* e.Status, e.Code, e.Message -> { error, message } */ }

// GET status (browser)
var poll = await sk.PollAsync(sid);            // poll.State == "authenticated" -> session for poll.User

// POST code (browser, manual entry)
var user = await sk.SubmitCodeAsync(sid, code); // LoginException on a wrong code or refusal
```

Protocol, API and how the app finds your server: [repository README](https://github.com/Paymaster-Technologies/sk-login-dotnet#readme).
