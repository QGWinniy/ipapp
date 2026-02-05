using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;

public class IpData
{
    public string Country { get; set; } = "Неизвестно";
    public string City { get; set; } = "Неизвестно";
}

public class Program
{
    public static async Task<IpData> GetIpDataAsync(string ip, HttpClient client)
    {
        var response = await client.GetAsync($"https://ipinfo.io/{ip}/json");
        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync();

        if (json.Contains("<html", StringComparison.OrdinalIgnoreCase) ||
            json.Contains("You're using IPinfo's Legacy Free API", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"IP {ip}: получен HTML вместо JSON (требуется токен API)");
        }

        var fullData = JsonConvert.DeserializeObject<Dictionary<string, object?>>(json);
        if (fullData == null)
        {
            throw new JsonException($"IP {ip}: не удалось распарсить ответ как JSON");
        }

        string? countryRaw = null;
        string? cityRaw = null;

        if (fullData.TryGetValue("country", out var c) && c is string s1) countryRaw = s1;
        if (fullData.TryGetValue("city", out var ct) && ct is string s2) cityRaw = s2;

        return new IpData
        {
            Country = string.IsNullOrWhiteSpace(countryRaw) ? "Неизвестно" : countryRaw,
            City = string.IsNullOrWhiteSpace(cityRaw) ? "Неизвестно" : cityRaw
        };
    }

    public static async Task<List<IpData>> FetchAllIpDataAsync(string[] ips, HttpClient client)
    {
        var allData = new List<IpData>();
        foreach (string rawIp in ips)
        {
            string ip = rawIp.Trim();
            if (string.IsNullOrEmpty(ip)) continue;

            IpData data = await GetIpDataAsync(ip, client);
            allData.Add(data);
        }
        return allData;
    }

    public static Dictionary<string, int> BuildCountryCounts(List<IpData> data)
    {
        var counts = new Dictionary<string, int>();
        foreach (var item in data)
        {
            counts[item.Country] = counts.GetValueOrDefault(item.Country, 0) + 1;
        }
        return counts;
    }

    public static Dictionary<string, HashSet<string>> BuildCountryCities(List<IpData> data)
    {
        var cities = new Dictionary<string, HashSet<string>>();
        foreach (var item in data)
        {
            if (!cities.ContainsKey(item.Country))
                cities[item.Country] = new HashSet<string>();
            cities[item.Country].Add(item.City);
        }
        return cities;
    }

    public static async Task Main(string[] args)
    {
        if (!File.Exists("ips.txt"))
        {
            Console.Error.WriteLine("Ошибка: файл 'ips.txt' не найден.");
            Environment.Exit(1);
        }

        string[] ips = File.ReadAllLines("ips.txt");

        try
        {
            using var client = new HttpClient();
            List<IpData> allData = await FetchAllIpDataAsync(ips, client);

            var countryCounts = BuildCountryCounts(allData);
            var countryCities = BuildCountryCities(allData);

            var sortedCountries = countryCounts
                .OrderByDescending(kvp => kvp.Value)
                .ThenBy(kvp => kvp.Key)
                .ToList();

            var output = new List<string> { "Статистика по странам:" };
            foreach (var kvp in sortedCountries)
            {
                output.Add($"{kvp.Key} — {kvp.Value} IP;");
            }
            output.Add("");

            if (sortedCountries.Count > 0)
            {
                string topCountry = sortedCountries[0].Key;
                output.Add($"Страна с наибольшим числом IP: {topCountry}");
                output.Add($"{topCountry}: {string.Join(", ", countryCities[topCountry])}");
            }

            foreach (string line in output)
                Console.WriteLine(line);

            File.WriteAllLines("результат.txt", output);
            Console.WriteLine("\nРезультат сохранён в 'результат.txt'");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"КРИТИЧЕСКАЯ ОШИБКА: {ex.Message}");
            Environment.Exit(1);
        }
    }
}