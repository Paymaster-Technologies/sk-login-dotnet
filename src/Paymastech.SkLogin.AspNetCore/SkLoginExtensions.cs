using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Paymastech.SkLogin.AspNetCore;

public static class SkLoginServiceCollectionExtensions
{
    /// <summary>Registers <see cref="SkLoginService{TUser}"/> (singleton). Options are read once, on first access.</summary>
    public static IServiceCollection AddSkLogin<TUser>(this IServiceCollection services, Action<SkLoginServiceOptions<TUser>> configure)
    {
        services.AddSingleton(sp =>
        {
            var options = new SkLoginServiceOptions<TUser>();
            configure(options);
            return new SkLoginService<TUser>(options);
        });
        return services;
    }

    /// <summary>Same, but with access to the container (configuration, user repository).</summary>
    public static IServiceCollection AddSkLogin<TUser>(this IServiceCollection services, Action<IServiceProvider, SkLoginServiceOptions<TUser>> configure)
    {
        services.AddSingleton(sp =>
        {
            var options = new SkLoginServiceOptions<TUser>();
            configure(sp, options);
            return new SkLoginService<TUser>(options);
        });
        return services;
    }
}

public static class SkLoginEndpointRouteBuilderExtensions
{
    public const string DefaultPrefix = "/api/sk";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// The five Secret Keeper sign-in routes under <paramref name="prefix"/>:
    /// POST init, POST login (text/plain, called by the app), GET status?sid=,
    /// POST code {sid, code}, GET target.
    /// </summary>
    public static RouteGroupBuilder MapSkLogin<TUser>(this IEndpointRouteBuilder endpoints, string prefix = DefaultPrefix)
    {
        var group = endpoints.MapGroup(prefix.TrimEnd('/'));

        group.MapPost("/init", async (HttpContext http, SkLoginService<TUser> sk, CancellationToken ct) =>
            Results.Json(await sk.InitAsync(http, ct), Json));

        group.MapPost("/login", async (HttpContext http, SkLoginService<TUser> sk, CancellationToken ct) =>
        {
            var body = await ReadBodyAsync(http.Request, sk.Options.MaxBodyBytes, ct);
            if (body is null) return Error(413, "bad-envelope", "envelope is too large");
            var lang = Messages.LangFromAcceptLanguage(http.Request.Headers.AcceptLanguage.ToString());
            try
            {
                var reply = await sk.HandleEnvelopeAsync(body, lang, ct);
                return reply switch
                {
                    EnvelopeReply.Challenge c => Results.Text(c.Armored, "text/plain", Encoding.UTF8),
                    EnvelopeReply.CodeAccepted => Results.Json(new { sent = true }, Json),
                    _ => Results.NoContent(),
                };
            }
            catch (LoginException e)
            {
                return Error(e.Status, e.Code, e.Message);
            }
        });

        group.MapGet("/status", async (HttpContext http, SkLoginService<TUser> sk, string? sid, CancellationToken ct) =>
        {
            if (string.IsNullOrEmpty(sid)) return Error(400, "bad-request", "sid is required");
            var result = await sk.PollAsync(sid, ct);
            var body = new Dictionary<string, object?> { ["state"] = result.State };
            if (result.Reason is not null) body["reason"] = result.Reason;
            if (result.State == "authenticated" && sk.Options.OnAuthenticated is not null)
                Merge(body, await sk.Options.OnAuthenticated(result.User!, http));
            return Results.Json(body, Json);
        });

        group.MapPost("/code", async (HttpContext http, SkLoginService<TUser> sk, CancellationToken ct) =>
        {
            CodeRequest? req;
            try
            {
                req = await http.Request.ReadFromJsonAsync<CodeRequest>(Json, ct);
            }
            catch (JsonException)
            {
                req = null;
            }
            if (req is null || string.IsNullOrEmpty(req.Sid) || string.IsNullOrEmpty(req.Code)) return Error(400, "bad-request", "sid and code are required");
            var lang = Messages.LangFromAcceptLanguage(http.Request.Headers.AcceptLanguage.ToString());
            try
            {
                var user = await sk.SubmitCodeAsync(req.Sid, req.Code, lang, ct);
                var body = new Dictionary<string, object?> { ["ok"] = true };
                if (sk.Options.OnAuthenticated is not null) Merge(body, await sk.Options.OnAuthenticated(user, http));
                return Results.Json(body, Json);
            }
            catch (LoginException e) when (e.Code == RefusalCode.AccessDenied)
            {
                return Results.Json(new { denied = true, reason = e.Reason, message = e.Message }, Json, statusCode: 403);
            }
            catch (LoginException e)
            {
                return Error(e.Status, e.Code, e.Message);
            }
        });

        group.MapGet("/target", (HttpContext http, SkLoginService<TUser> sk) =>
        {
            var origin = sk.Options.Target!.PublicUrl?.TrimEnd('/') ?? OriginOf(http.Request);
            var loginUrl = $"{origin}{prefix.TrimEnd('/')}/login";
            var t = sk.Target(loginUrl);
            var body = new Dictionary<string, object?>
            {
                ["id"] = t.Id,
                ["v"] = t.V,
                ["url"] = t.Url,
                ["serverAddress"] = t.ServerAddress,
                ["checkDigits"] = t.CheckDigits,
            };
            if (t.Hub is not null) body["hub"] = t.Hub;
            if (t.OwnerHash is not null) body["ownerHash"] = t.OwnerHash;
            return Results.Json(body, Json);
        });

        return group;
    }

    private sealed record CodeRequest(string? Sid, string? Code);

    private static IResult Error(int status, string code, string message) =>
        Results.Json(new { error = code, message }, Json, statusCode: status);

    /// <summary>Public origin behind a proxy: X-Forwarded-Proto and X-Forwarded-Host, otherwise from the request.</summary>
    private static string OriginOf(HttpRequest req)
    {
        var proto = req.Headers["x-forwarded-proto"].ToString().Split(',')[0].Trim();
        var host = req.Headers["x-forwarded-host"].ToString().Split(',')[0].Trim();
        if (proto.Length == 0) proto = req.Scheme;
        if (host.Length == 0) host = req.Host.Value;
        return $"{proto}://{host}";
    }

    /// <summary>Body as text with a limit; null if it exceeds the limit.</summary>
    private static async Task<string?> ReadBodyAsync(HttpRequest req, int maxBytes, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await req.Body.ReadAsync(buffer, ct)) > 0)
        {
            if (ms.Length + read > maxBytes) return null;
            ms.Write(buffer, 0, read);
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>Fields from the OnAuthenticated return value merged over the response (anonymous type, record, dictionary).</summary>
    private static void Merge(Dictionary<string, object?> body, object? extra)
    {
        if (extra is null) return;
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(extra, extra.GetType(), Json));
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return;
        foreach (var p in doc.RootElement.EnumerateObject()) body[p.Name] = p.Value.Clone();
    }
}
