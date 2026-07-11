using ECommons.DalamudServices;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PartyBell.Discord;

public sealed class WebhookClient : IDisposable
{
    public const int ColorJoin = 0x2ECC71;   // 綠
    public const int ColorLeave = 0xE74C3C;  // 紅
    public const int ColorChat = 0x3498DB;   // 藍
    public const int ColorAlliance = 0x1ABC9C; // 青
    public const int ColorTell = 0x9B59B6;   // 紫
    public const int ColorSystem = 0xF1C40F; // 黃
    public const int ColorDuty = 0xE67E22;   // 橘
    public const int ColorInfo = 0x95A5A6;   // 灰

    private record EmbedData(string Title, string? Description, int Color, DateTime TimestampUtc, bool Mention);

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private readonly Configuration config;
    private readonly ConcurrentQueue<EmbedData> queue = new();
    private readonly SemaphoreSlim signal = new(0);
    private readonly CancellationTokenSource cts = new();

    public WebhookClient(Configuration config)
    {
        this.config = config;
        _ = Task.Run(WorkerLoop);
    }

    public void Enqueue(string title, string? description, int color, bool mention = false)
    {
        if (string.IsNullOrWhiteSpace(config.WebhookUrl))
            return;
        queue.Enqueue(new EmbedData(Truncate(title, 256), Sanitize(description), color, DateTime.UtcNow, mention));
        signal.Release();
    }

    public Task<bool> SendTestAsync()
        => SendAsync(config.WebhookUrl, [new EmbedData("🔔 PartyBell 測試訊息", "Webhook 設定成功!", ColorJoin, DateTime.UtcNow, Mention: true)]);

    private async Task WorkerLoop()
    {
        var token = cts.Token;
        while (!token.IsCancellationRequested)
        {
            try
            {
                await signal.WaitAsync(token);
                // 稍等讓同一波訊息累積,合併成一則 webhook(上限 10 embeds)
                await Task.Delay(500, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var embeds = new List<EmbedData>();
            while (embeds.Count < 10 && queue.TryDequeue(out var e))
                embeds.Add(e);
            if (embeds.Count == 0)
                continue;

            var url = config.WebhookUrl;
            if (!string.IsNullOrWhiteSpace(url))
                await SendAsync(url, embeds);

            try
            {
                // Discord webhook 節流:每則間隔至少 2 秒
                await Task.Delay(2000, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task<bool> SendAsync(string url, List<EmbedData> embeds)
    {
        try
        {
            var mention = embeds.Any(e => e.Mention) ? BuildMention() : null;
            var payload = new
            {
                username = "PartyBell",
                content = mention ?? string.Empty,
                embeds = embeds.Select(e => new
                {
                    title = e.Title,
                    description = e.Description ?? string.Empty,
                    color = e.Color,
                    timestamp = e.TimestampUtc.ToString("o"),
                }).ToArray(),
            };
            var json = JsonSerializer.Serialize(payload);
            var resp = await Http.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                Svc.Log.Error($"[PartyBell] Webhook 發送失敗 HTTP {(int)resp.StatusCode}: {body}");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"[PartyBell] Webhook 發送錯誤: {ex.Message}");
            return false;
        }
    }

    // 使用者 ID → <@id>;everyone / here → @everyone / @here;其他原樣送出
    private string? BuildMention()
    {
        var t = config.MentionTarget.Trim();
        if (t.Length == 0)
            return null;
        if (t.TrimStart('@').Equals("everyone", StringComparison.OrdinalIgnoreCase))
            return "@everyone";
        if (t.TrimStart('@').Equals("here", StringComparison.OrdinalIgnoreCase))
            return "@here";
        var digits = t.TrimStart('<', '@', '!').TrimEnd('>');
        return ulong.TryParse(digits, out var id) ? $"<@{id}>" : t;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    private static string? Sanitize(string? s)
    {
        if (s == null)
            return null;
        // 避免遊戲內文字觸發 Discord mention
        return Truncate(s.Replace("@", "@​"), 4000);
    }

    public void Dispose()
    {
        cts.Cancel();
        signal.Release();
        cts.Dispose();
    }
}
