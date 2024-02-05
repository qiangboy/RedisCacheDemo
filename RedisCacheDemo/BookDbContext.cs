using Microsoft.EntityFrameworkCore;

namespace RedisCacheDemo;

public class BookDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<Book> Books => Set<Book>();
}