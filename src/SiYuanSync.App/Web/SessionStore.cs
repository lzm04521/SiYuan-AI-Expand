using System.Security.Cryptography;

namespace SiYuanSync.App.Web;

public sealed class SessionStore
{
    private readonly Dictionary<string, DateTime> _sessions = new();
    private readonly object _lock = new();
    private readonly TimeSpan _lifetime;

    public SessionStore(TimeSpan? lifetime = null) => _lifetime = lifetime ?? TimeSpan.FromHours(8);

    public string Issue()
    {
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        lock (_lock) _sessions[id] = DateTime.UtcNow.Add(_lifetime);
        return id;
    }

    public bool IsValid(string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return false;
        lock (_lock)
        {
            if (_sessions.TryGetValue(sessionId, out var exp) && exp > DateTime.UtcNow) return true;
            _sessions.Remove(sessionId ?? "");
            return false;
        }
    }

    public void RevokeAll() { lock (_lock) _sessions.Clear(); }
}
