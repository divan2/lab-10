using Microsoft.EntityFrameworkCore;

using Newtonsoft.Json;

public class Stock
{
    public int Id { get; set; }
    public string Ticker { get; set; }
    public DateTime Date { get; set; }
    public double Price { get; set; }
}

public class TodaysCondition
{
    public int Id { get; set; }
    public string Ticker { get; set; }
    public string Condition { get; set; }
}

public class StockContext : DbContext
{
    public DbSet<Stock> Stocks { get; set; }
    public DbSet<TodaysCondition> TodaysConditions { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Строка подключения к базе данных
        optionsBuilder.UseSqlServer("Server=localhost,1436;Database=StocksDB;User Id=SA;Password=LEGO1111;TrustServerCertificate=True");
    }
}

class Program
{
    static async Task Main(string[] args)
    {
        using var context = new StockContext();
        context.Database.EnsureCreated(); // Создаем базу данных, если ее нет

        var tickers = await LoadTickersFromFile("C:/Users/samar/YandexDisk/мгту/2 курс/АЯ/lab 9/lab 9/ticker.txt");
        await FetchAndSaveStockData(tickers);
        await UpdateTodaysCondition(); // Обновляем таблицу TodaysCondition
        await HandleUserInput(); // Запрашиваем ввод пользователя
    }

    // Загружает тикеры из файла
    static async Task<List<string>> LoadTickersFromFile(string filePath)
    {
        var tickers = new List<string>();
        using var reader = new StreamReader(filePath);
        string line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            tickers.Add(line);
        }
        return tickers;
    }

    // Получает и сохраняет данные по тикерам
    static async Task FetchAndSaveStockData(List<string> tickers)
    {
        var tasks = tickers.Select(GetDataForTicker);
        await Task.WhenAll(tasks);
    }

    // Получает данные для конкретного тикера
    static async Task GetDataForTicker(string ticker)
    {
        using var client = new HttpClient();
        string url = $"https://api.marketdata.app/v1/stocks/candles/D/{ticker}/?from=2024-12-10&to=2024-12-21&token=ZEFSZWVaNmFoenl0Wlh4NHAtekdVN3dCdnpHOElVU2cyNm5hWjVTeXZaVT0";
        var response = await client.GetAsync(url);
        var responseContent = await response.Content.ReadAsStringAsync();
        dynamic responseObject = JsonConvert.DeserializeObject(responseContent);

        if (responseObject?.t == null || responseObject?.h == null || responseObject?.l == null)
        {
            Console.WriteLine($"Ошибка при обработке {ticker}: Отсутствуют данные.");
            return;
        }

        List<long> timestamps = responseObject.t.ToObject<List<long>>();
        List<double> highs = responseObject.h.ToObject<List<double>>();
        List<double> lows = responseObject.l.ToObject<List<double>>();

        using var context = new StockContext();
        for (int i = 0; i < timestamps.Count; i++)
        {
            double averagePrice = (highs[i] + lows[i]) / 2;
            DateTime date = DateTimeOffset.FromUnixTimeSeconds(timestamps[i]).DateTime;
            await AddStockToDb(ticker, date, averagePrice, context);
        }
        Console.WriteLine($"Данные для {ticker} загружены в базу.");

    }

    // Добавляет данные о цене в базу
    private static async Task AddStockToDb(string ticker, DateTime date, double averagePrice, StockContext context)
    {
        var stock = new Stock { Ticker = ticker, Date = date, Price = averagePrice };
        context.Stocks.Add(stock);
        await context.SaveChangesAsync();
    }

    // Обновляет таблицу TodaysCondition на основе последних двух цен
    private static async Task UpdateTodaysCondition()
    {
        using var context = new StockContext();
        var tickers = context.Stocks.Select(s => s.Ticker).Distinct().ToList();

        foreach (var ticker in tickers)
        {
            var lastTwoPrices = await context.Stocks
                .Where(s => s.Ticker == ticker)
                .OrderByDescending(s => s.Date)
                .Take(2)
                .ToListAsync();

            if (lastTwoPrices.Count < 2) continue;

            double todayPrice = lastTwoPrices[0].Price;
            double yesterdayPrice = lastTwoPrices[1].Price;
            string condition = todayPrice > yesterdayPrice ? "выросла" : "упала";

            var existingCondition = await context.TodaysConditions.FirstOrDefaultAsync(tc => tc.Ticker == ticker);

            if (existingCondition == null)
            {
                context.TodaysConditions.Add(new TodaysCondition { Ticker = ticker, Condition = condition });
            }
            else
            {
                existingCondition.Condition = condition;
            }
        }

        await context.SaveChangesAsync();
        Console.WriteLine("Таблица TodaysCondition обновлена.");
    }

    // Обрабатывает ввод пользователя
    private static async Task HandleUserInput()
    {
        while (true)
        {
            Console.Write("Введите тикер акции (или 'exit' для выхода): ");
            string inputTicker = Console.ReadLine();
            if (inputTicker?.ToLower() == "exit") { break; }
            await GetStockCondition(inputTicker);
        }
    }
    // Выводит состояние акции
    private static async Task GetStockCondition(string ticker)
    {
        using var context = new StockContext();
        var condition = await context.TodaysConditions.FirstOrDefaultAsync(tc => tc.Ticker == ticker);

        if (condition != null)
        {
            Console.WriteLine($"Состояние акции {ticker}: {condition.Condition}");
        }
        else
        {
            Console.WriteLine($"Нет данных об изменении цены для {ticker}.");
        }
    }
}
