using CanalSharp.Connections;
using CanalSharp.Protocol;

namespace RedisCacheDemo;

public class Canal(ILogger<SimpleCanalConnection> logger)
{
    public async Task StartAsync()
    {
        //var logger = loggerFactory.CreateLogger<SimpleCanalConnection>();

        var conn = new SimpleCanalConnection(new SimpleCanalOptions("192.168.0.191", 11111, "mysql"), logger);
        //连接到 Canal Server
        await conn.ConnectAsync();
        //订阅
        await conn.SubscribeAsync();

        while (true)
        {
            var msg = await conn.GetAsync(1024);
            PrintEntry(msg.Entries);
            await Task.Delay(300);
        }
    }

    private void PrintEntry(List<Entry> entries)
    {
        foreach (var entry in entries)
        {
            if (entry.EntryType is EntryType.Transactionbegin or EntryType.Transactionend)
            {
                continue;
            }

            RowChange? rowChange = null;

            try
            {
                rowChange = RowChange.Parser.ParseFrom(entry.StoreValue);
            }
            catch (Exception e)
            {
                logger.LogError(e.ToString());
            }

            if (rowChange != null)
            {
                EventType eventType = rowChange.EventType;

                logger.LogInformation(
                    $"================> binlog[{entry.Header.LogfileName}:{entry.Header.LogfileOffset}] , name[{entry.Header.SchemaName},{entry.Header.TableName}] , eventType :{eventType}");

                foreach (var rowData in rowChange.RowDatas)
                {
                    if (eventType == EventType.Delete)
                    {
                        PrintColumn(rowData.BeforeColumns.ToList());
                    }
                    else if (eventType == EventType.Insert)
                    {
                        PrintColumn(rowData.AfterColumns.ToList());
                    }
                    else
                    {
                        logger.LogInformation("-------> before");
                        PrintColumn(rowData.BeforeColumns.ToList());
                        logger.LogInformation("-------> after");
                        PrintColumn(rowData.AfterColumns.ToList());
                    }
                }
            }
        }
    }

    private static void PrintColumn(List<Column> columns)
    {
        foreach (var column in columns)
        {
            Console.WriteLine($"{column.Name} ： {column.Value}  update=  {column.Updated}");
        }
    }
}