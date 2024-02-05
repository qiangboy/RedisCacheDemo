using System.Text.Json;
using Medallion.Threading;
using StackExchange.Redis;

namespace RedisCacheDemo;

public class DistributedRedisCache(IConnectionMultiplexer connectionMultiplexer, IDistributedLockProvider distributedLockProvider, ILogger<DistributedRedisCache> logger) : IDistributedRedisCache
{
    public async Task<T?> GetOrCreateAsync<T>(string key, Func<Task<T?>> factory, TimeSpan? expiry)
    {
        var db = GetDatabase();
        var stringValue = await db.StringGetAsync(key);

        // 获取到值直接返回
        if (!stringValue.IsNullOrEmpty)
        {
            logger.LogTrace("线程Id：{ ThreadId } 从缓存中取值", Environment.CurrentManagedThreadId);

            return JsonSerializer.Deserialize<T>(stringValue!);
        }

        // timeout默认为TimeSpan.Zero，即获取不到锁时不阻塞
        await using var lockHandle = await distributedLockProvider.TryAcquireLockAsync($"lock:{key}");

        // 获取到锁
        if (lockHandle is not null)
        {
            logger.LogTrace("线程Id：{ ThreadId } 获取到锁", Environment.CurrentManagedThreadId);
            var value = await factory();

            // 缓存
            await db.StringSetAsync(key, JsonSerializer.Serialize(value), expiry);

            return value;
        }

        logger.LogTrace("线程Id：{ ThreadId } ，锁已被抢走，重新执行", Environment.CurrentManagedThreadId);
        // 重试
        await Task.Delay(100);

        return await GetOrCreateAsync(key, factory, expiry);
    }

    private IDatabase GetDatabase()
    {
        return connectionMultiplexer.GetDatabase();
    }
}