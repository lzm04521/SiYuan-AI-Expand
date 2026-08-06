namespace SiYuanSync.App.Web;

public sealed class LoginRateLimiter
{
    private readonly Dictionary<string, List<DateTime>> _hits = new();
    private readonly object _lock = new();
    private readonly int _maxAttempts;
    private readonly TimeSpan _window;

    public LoginRateLimiter(int maxAttempts = 5, TimeSpan? window = null)
    { _maxAttempts = maxAttempts; _window = window ?? TimeSpan.FromMinutes(1); }

    public bool TryConsume(string ip)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            if (!_hits.TryGetValue(ip, out var list)) list = new();
            list.RemoveAll(t => now - t > _window);
            if (list.Count >= _maxAttempts) { _hits[ip] = list; return false; }
            list.Add(now); _hits[ip] = list;
            return true;
        }
    }
}
