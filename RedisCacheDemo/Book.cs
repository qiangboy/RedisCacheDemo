using System.ComponentModel.DataAnnotations;

namespace RedisCacheDemo;

public class Book(string name, int stock)
{
    public int Id { get; set; }

    public string Name { get; set; } = name;
    public int Stock { get; set; } = stock;

    [Timestamp]
    public byte[] Version { get; set; }
}