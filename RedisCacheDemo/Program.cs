using BloomFilter;
using BloomFilter.Redis.Configurations;
using Medallion.Threading;
using Medallion.Threading.Redis;
using Microsoft.EntityFrameworkCore;
using RedisCacheDemo;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddHttpClient();

builder.Services.AddDbContext<BookDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("Mysql");
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
});

builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
    options.InstanceName = "SampleInstance";
});

builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis")!));

// ·Ö²¼Ê½Ëø
builder.Services.AddSingleton<IDistributedLockProvider>(sp =>
{
    var connectionMultiplexer = sp.GetRequiredService<IConnectionMultiplexer>();

    return new RedisDistributedSynchronizationProvider(connectionMultiplexer.GetDatabase());
});

builder.Services.AddBloomFilter(options =>
{
    options.UseRedis(new FilterRedisOptions
    {
        Name = "Redis1",
        RedisKey = "BloomFilter1",
        Endpoints = new[] { "localhost" }.ToList()
    });
});

builder.Services.AddSingleton<IDistributedRedisCache, DistributedRedisCache>();

builder.Services.AddSingleton<Canal>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var bloomFilter = scope.ServiceProvider.GetRequiredService<IBloomFilter>();
    var dbContext = scope.ServiceProvider.GetRequiredService<BookDbContext>();

    await dbContext.Database.MigrateAsync();
    //await dbContext.Database.EnsureCreatedAsync();
    await dbContext.InitDatabaseAsync();

    var bookIds = await dbContext.Books.Select(b => b.Id).ToListAsync();
    bookIds.ForEach(id => bloomFilter.Add(id));

    // canal
    var canal = scope.ServiceProvider.GetRequiredService<Canal>();
    await canal.StartAsync();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();

app.MapControllers();

app.Run();
