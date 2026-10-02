using System.Collections.Concurrent;

namespace Paymastech.SkLogin;

public enum PendingState
{
    New,
    Challenged,
    Authenticated,
    Denied,
    Cancelled,
    Used,
}

/// <summary>Sign-in request keyed by sid. A flat object, serialized to JSON for an external store.</summary>
public sealed class Pending<TUser>
{
    public required string Sid { get; set; }
    public long CreatedAt { get; set; }
    /// <summary>Request deadline; a resolved request lives one more TTL so the browser can pick up the outcome.</summary>
    public long ExpiresAt { get; set; }
    public PendingState State { get; set; }
    /// <summary>The browser that received the sid (for challenge v2).</summary>
    public RequestContext? Ctx { get; set; }
    /// <summary>Authenticated address of the request sender.</summary>
    public string? Sender { get; set; }
    public string? Code { get; set; }
    public int CodeAttempts { get; set; }
    /// <summary>Admitted user (Authenticated): for the session.</summary>
    public TUser? User { get; set; }
    /// <summary>Why access was refused (Denied).</summary>
    public string? Denied { get; set; }
}

/// <summary>Request store. In-process memory by default; for several replicas implement it on top of Redis or a database.</summary>
public interface IPendingStore<TUser>
{
    Task<Pending<TUser>?> GetAsync(string sid, CancellationToken ct = default);
    /// <summary><paramref name="ttl"/>: how long until the entry can be dropped (with a margin for polling).</summary>
    Task SetAsync(Pending<TUser> entry, TimeSpan ttl, CancellationToken ct = default);
    Task DeleteAsync(string sid, CancellationToken ct = default);
}

/// <summary>In-process memory: expired entries are swept on every access.</summary>
public sealed class MemoryPendingStore<TUser> : IPendingStore<TUser>
{
    private readonly ConcurrentDictionary<string, (Pending<TUser> Entry, long DropAt)> _items = new();
    private readonly Func<long> _now;

    public MemoryPendingStore(Func<long>? now = null)
    {
        _now = now ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    public int Count => _items.Count;

    public Task<Pending<TUser>?> GetAsync(string sid, CancellationToken ct = default)
    {
        Sweep();
        return Task.FromResult(_items.TryGetValue(sid, out var item) ? item.Entry : null);
    }

    public Task SetAsync(Pending<TUser> entry, TimeSpan ttl, CancellationToken ct = default)
    {
        Sweep();
        _items[entry.Sid] = (entry, _now() + (long)ttl.TotalMilliseconds);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string sid, CancellationToken ct = default)
    {
        _items.TryRemove(sid, out _);
        return Task.CompletedTask;
    }

    private void Sweep()
    {
        var now = _now();
        foreach (var (sid, item) in _items)
            if (item.DropAt <= now) _items.TryRemove(sid, out _);
    }
}
