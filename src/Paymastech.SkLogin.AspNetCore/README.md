# Paymastech.SkLogin.AspNetCore

Sign in with Secret Keeper for ASP.NET Core (`net7.0`, `net8.0`). `AddSkLogin`
registers the service, `MapSkLogin` mounts `POST /sk/login` for the Secret
Keeper app (the app derives it from the site host in the QR) and the `init`,
`status`, `code` and `target` endpoints for the browser widget under a prefix
(`/api/sk` by default).

```csharp
builder.Services.AddSkLogin<User>(o =>
{
    o.Mnemonic = builder.Configuration["SK_SERVER_MNEMONIC"];
    o.Target = new SkLoginTarget
    {
        Site = "api.example.com",            // your public host: goes into the QR with the server address
        PublicUrl = "https://api.example.com",
    };
    o.Access = async address =>
    {
        var user = await users.FindByAddressAsync(address);
        return user is null ? AccessDecision<User>.Denied("unknown-address") : AccessDecision<User>.Granted(user);
    };
    o.OnAuthenticated = async (user, http) =>
    {
        await http.SignInAsync(principalFor(user));
        return new { userId = user.Id };
    };
});

app.MapSkLogin<User>("/api/sk");
```

Browser side: the `@paymastech/sk-login-widget` widget (npm, or a single JS
file with no framework) from [paymastech/sk-login](https://github.com/paymastech/sk-login).

Full description of options, HTTP API, error codes, how the app finds your
server and running on multiple replicas: [repository README](https://github.com/paymastech/sk-login-dotnet#readme).
