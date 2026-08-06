namespace SiYuanSync.Core.Siyuan;

public sealed class SiyuanTransientException : Exception
{
    public SiyuanTransientException(string message, Exception? inner = null) : base(message, inner) { }
}
