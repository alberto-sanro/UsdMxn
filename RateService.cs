using System.Net;
using System.Text.Json;

namespace UsdMxn;

/// <summary>One reading of the USD→MXN rate.</summary>
/// <param name="Rate">Pesos per one US dollar.</param>
/// <param name="PrevClose">Previous daily close, when the source provides it.</param>
/// <param name="Month">Daily closes from the last 30 days, oldest first (may be empty).</param>
/// <param name="Year">Daily closes from the last 12 months, oldest first (may be empty).</param>
/// <param name="Live">True for the intraday market feed, false for the daily fallback.</param>
public sealed record RateSnapshot(double Rate, double? PrevClose, double[] Month, double[] Year, bool Live);

public static class RateService
{
    private const string LiveUrl =
        "https://query1.finance.yahoo.com/v8/finance/chart/MXN=X?interval=1d&range=1y";
    private const string DailyUrl = "https://open.er-api.com/v6/latest/USD";

    // One shared client. Idle connections are dropped after a minute by default,
    // so nothing stays open between the 5-minute refreshes.
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = false,
        })
        {
            Timeout = TimeSpan.FromSeconds(15),
        };
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
        return client;
    }

    /// <summary>Returns the live rate, the daily rate if the live feed fails, or null if both fail.</summary>
    public static async Task<RateSnapshot?> FetchAsync()
    {
        try { return await FetchLiveAsync().ConfigureAwait(false); }
        catch { /* fall through to the daily source */ }

        try { return await FetchDailyAsync().ConfigureAwait(false); }
        catch { return null; }
    }

    private static async Task<JsonDocument> GetJsonAsync(string url)
    {
        using var response = await Http
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
    }

    private static async Task<RateSnapshot> FetchLiveAsync()
    {
        using var doc = await GetJsonAsync(LiveUrl).ConfigureAwait(false);
        var result = doc.RootElement.GetProperty("chart").GetProperty("result")[0];
        double rate = result.GetProperty("meta").GetProperty("regularMarketPrice").GetDouble();
        if (!(rate > 0)) throw new InvalidDataException();

        // A year of daily closes paired with their timestamps; days without a close are skipped.
        var closes = new List<double>(270);
        var times = new List<long>(270);
        if (result.TryGetProperty("timestamp", out var tsArr) &&
            result.TryGetProperty("indicators", out var ind) &&
            ind.TryGetProperty("quote", out var quote) && quote.GetArrayLength() > 0 &&
            quote[0].TryGetProperty("close", out var closeArr))
        {
            var t = tsArr.EnumerateArray();
            var c = closeArr.EnumerateArray();
            while (t.MoveNext() && c.MoveNext())
            {
                if (c.Current.ValueKind != JsonValueKind.Number ||
                    t.Current.ValueKind != JsonValueKind.Number) continue;
                closes.Add(c.Current.GetDouble());
                times.Add(t.Current.GetInt64());
            }
        }

        // The last entry is today's (still moving) bar; the one before is the previous close.
        // Prefer the feed's own day-change figure so the number matches what quote sites show.
        double? prev = closes.Count >= 2 ? closes[^2] : null;
        if (result.GetProperty("meta").TryGetProperty("regularMarketChangePercent", out var pct) &&
            pct.ValueKind == JsonValueKind.Number && pct.GetDouble() > -100)
        {
            prev = rate / (1 + pct.GetDouble() / 100);
        }
        if (closes.Count > 0) closes[^1] = rate;

        double[] year = closes.ToArray();
        long monthStart = DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeSeconds();
        int first = times.FindIndex(ts => ts >= monthStart);
        double[] month = first < 0 ? [] : year[first..];
        return new RateSnapshot(rate, prev, month, year, Live: true);
    }

    private static async Task<RateSnapshot> FetchDailyAsync()
    {
        using var doc = await GetJsonAsync(DailyUrl).ConfigureAwait(false);
        double rate = doc.RootElement.GetProperty("rates").GetProperty("MXN").GetDouble();
        if (!(rate > 0)) throw new InvalidDataException();
        return new RateSnapshot(rate, null, [], [], Live: false);
    }
}
