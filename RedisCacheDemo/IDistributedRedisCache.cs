namespace RedisCacheDemo;

public interface IDistributedRedisCache
{
    Task<T?> GetOrCreateAsync<T>(string key, Func<Task<T?>> factory, TimeSpan? expiry);
}