namespace Canhoes.Api.Caching;

public sealed record CacheResult<T>(bool Success, T? Value, Exception? Error)
{
    public static CacheResult<T> Ok(T value) => new(true, value, null);
    public static CacheResult<T> Fail(Exception error) => new(false, default, error);
}
