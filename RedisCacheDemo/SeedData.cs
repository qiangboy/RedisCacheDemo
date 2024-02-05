using Microsoft.EntityFrameworkCore;

namespace RedisCacheDemo;

public static class SeedData
{
    public static async Task InitDatabaseAsync(this BookDbContext dbContext)
    {
        if (!await dbContext.Books.AnyAsync())
        {
            Book[] books =
            [
                new Book("一本好书", 20),
                new Book("西游记", 13),
                new Book("水浒传", 99),
            ];

            await dbContext.Books.AddRangeAsync(books);

            await dbContext.SaveChangesAsync();
        }
    }
}