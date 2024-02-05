using System.Diagnostics;
using System.Text.Json;
using BloomFilter;
using Medallion.Threading;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using StackExchange.Redis;

namespace RedisCacheDemo.Controllers;

[Route("api/[controller]")]
[ApiController]
public class ValuesController(IDistributedCache distributedCache,
    ILogger<ValuesController> logger,
    IDistributedLockProvider distributedLockProvider,
    IBloomFilter bloomFilter,
    IDistributedRedisCache distributedRedisCache,
    IHttpClientFactory httpClientFactory,
    BookDbContext dbContext,
    IConnectionMultiplexer connectionMultiplexer) : ControllerBase
{
    [HttpGet]
    public async Task<string> Test(int id, int num, Stopwatch stopwatch)
    {
        var exists = await bloomFilter.ContainsAsync(id);

        if (!exists)
        {
            stopwatch.Stop();

            return $"请求 {num}" + "不存在" + $"耗时:{stopwatch.ElapsedMilliseconds}毫秒";
        }

        const string key = "key";

        var value = await distributedCache.GetStringAsync(key);

        // 获取到值直接返回
        if (value is not null)
        {
            logger.LogInformation("请求 {num} 走缓存", num);

            stopwatch.Stop();

            return  $"请求 {num}" + value + $"耗时:{stopwatch.ElapsedMilliseconds}毫秒";
        }

        await using var lockHandle = await distributedLockProvider.TryAcquireLockAsync($"lock_{id}");

        // 获取到锁
        if (lockHandle is not null)
        {
            logger.LogInformation("请求 {num} 拿到锁，请求数据库", num);

            var book = await dbContext.Books.FindAsync(id);
            // 模拟数据库查询操作
            await Task.Delay(200);
            value = JsonSerializer.Serialize(book);

            // 缓存
            await distributedCache.SetStringAsync(key, value);
        }
        else
        {
            // 重试
            //logger.LogWarning("请求 {num} 锁被抢走，重试", num);
            //await Task.Delay(50);
            return await Test(id, num, stopwatch);
        }
        stopwatch.Stop();

        return $"请求 {num}" + value + $"耗时:{stopwatch.ElapsedMilliseconds}毫秒";
    }

    [HttpGet("Test2")]
    public async Task<ActionResult<Book>> Test2(int id)
    {
        var exists = await bloomFilter.ContainsAsync(id);

        if (!exists)
        {
            return NotFound();
        }

        var book = await distributedRedisCache.GetOrCreateAsync($"book:{id}", async () => await dbContext.Books.FindAsync(id), TimeSpan.FromSeconds(20));

        if (book is null)
        {
            return NotFound();
        }

        return book;
    }

    [HttpPost]
    public async Task CallApi()
    {
        logger.LogInformation("调用开始");
        await distributedCache.RemoveAsync("key");
        var tasks = new List<Task<string>>();
        for (int i = 0; i < 5; i++)
        {
            tasks.Add(Test(10, i, Stopwatch.StartNew()));
        }
        await Task.WhenAll(tasks);

        foreach (var task in tasks)
        {
            logger.LogInformation("任务 {index}  | 结果 {result} | is null {isnull}", tasks.IndexOf(task), task.Result, string.IsNullOrWhiteSpace(task.Result));
        }
    }

    [HttpPost("CallApi2")]
    public async Task CallApi2()
    {
        logger.LogInformation("调用开始");
        var tasks = new List<Task<string>>();
        for (int i = 0; i < 20; i++)
        {
            tasks.Add(Test(30, i, Stopwatch.StartNew()));
        }
        await Task.WhenAll(tasks);

        foreach (var task in tasks)
        {
            logger.LogInformation("任务 {index}  | 结果 {result} | is null {isnull}", tasks.IndexOf(task), task.Result, string.IsNullOrWhiteSpace(task.Result));
        }
    }

    [HttpPost("CallApi3")]
    public async Task CallApi3()
    {
        logger.LogInformation("调用开始");
        var tasks = new List<Task>();
        for (int i = 0; i < 20; i++)
        {
            tasks.Add(CallTest2());
        }
        await Task.WhenAll(tasks);

        async Task CallTest2()
        {
            var httpClient = httpClientFactory.CreateClient();
            httpClient.BaseAddress = new Uri("http://localhost:5219");
            await httpClient.GetAsync("api/Values/Test2?id=2");
        }
    }

    [HttpPost("CallReduceStock")]
    public async Task CallReduceStock()
    {
        logger.LogInformation("调用开始");
        var tasks = new List<Task>();
        for (int i = 0; i < 20; i++)
        {
            tasks.Add(CallReduceStock1());
        }
        await Task.WhenAll(tasks);

        async Task CallReduceStock1()
        {
            var httpClient = httpClientFactory.CreateClient();
            httpClient.BaseAddress = new Uri("http://localhost:5219");
            await httpClient.GetAsync("api/Values/ReduceStock?id=2");
        }
    }

    [HttpGet("ReduceStock")]
    public async Task<IActionResult> ReduceStock(int id/*, string name*/)
    {
        //await using (await distributedLockProvider.AcquireLockAsync($"lock:book:reduce_stock:{id}"))
        //{
        try
        {
            var book = await dbContext.Books.FindAsync(id);

            if (book is null)
            {
                return NotFound();
            }

            //book.Name = name;
            book.Stock -= 1;

            await dbContext.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException e)
        {
            var entry = e.Entries.First();
            var dbValues = await entry.GetDatabaseValuesAsync();
            string newOwner = dbValues!.GetValue<string>(nameof(Book.Name));
            return BadRequest($"并发冲突，房子已经被{newOwner}抢走");
        }
            
        //}

        return Ok(new { message = "ok" });
    }

    [HttpGet("SetStock")]
    public async Task<IActionResult> SetStock()
    {
        var book = await dbContext.Books.FindAsync(1);

        if (book is null)
        {
            return NotFound();
        }

        var db = connectionMultiplexer.GetDatabase();
        await db.StringSetAsync($"book:stock:{book.Id}", book.Stock);

        return Ok();
    }

    [HttpGet("TestIncr")]
    public async Task<IActionResult> TestIncr()
    {
        var book = await dbContext.Books.FindAsync(1);

        if (book is null)
        {
            return NotFound();
        }

        var db = connectionMultiplexer.GetDatabase();
        await db.StringDecrementAsync($"book:stock:{book.Id}");

        return Ok();
    }

    [HttpPost("CallTestIncr")]
    public async Task CallTestIncr()
    {
        logger.LogInformation("调用开始");
        var tasks = new List<Task>();
        for (int i = 0; i < 20; i++)
        {
            tasks.Add(CallReduceStock1());
        }
        await Task.WhenAll(tasks);

        async Task CallReduceStock1()
        {
            var httpClient = httpClientFactory.CreateClient();
            httpClient.BaseAddress = new Uri("http://localhost:5219");
            await httpClient.GetAsync("api/Values/TestIncr");
        }
    }
}