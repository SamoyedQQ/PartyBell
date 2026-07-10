using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using PartyBell.Discord;
using PartyBell.Util;
using System;
using System.Linq;

namespace PartyBell.Listeners;

/// <summary>
/// 轉發隊伍頻道訊息、密語,並從系統訊息比對出招募申請通知。
/// </summary>
public sealed class ChatListener : IDisposable
{
    private readonly Configuration config;
    private readonly WebhookClient webhook;

    public ChatListener(Configuration config, WebhookClient webhook)
    {
        this.config = config;
        this.webhook = webhook;
        Svc.Chat.ChatMessage += OnChatMessage;
    }

    private void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        if (!NotifyGate.ShouldNotify(config))
            return;

        switch (type)
        {
            case XivChatType.Party:
            case XivChatType.CrossParty:
            case XivChatType.Alliance:
                if (config.NotifyPartyChat && !IsSelf(sender))
                    webhook.Enqueue($"💬 {SenderName(sender)}", message.TextValue, WebhookClient.ColorChat, config.MentionOnPartyChat);
                break;

            case XivChatType.TellIncoming:
                if (config.NotifyTell)
                    webhook.Enqueue($"📨 密語:{SenderName(sender)}", message.TextValue, WebhookClient.ColorTell, config.MentionOnTell);
                break;
        }
    }

    private static string SenderName(SeString sender)
    {
        var player = sender.Payloads.OfType<PlayerPayload>().FirstOrDefault();
        if (player != null)
            return $"{player.PlayerName}@{player.World.ValueNullable?.Name.ExtractText()}";
        return CleanName(sender.TextValue);
    }

    private static bool IsSelf(SeString sender)
    {
        if (sender.Payloads.OfType<PlayerPayload>().Any())
            return false;
        return Player.Available && CleanName(sender.TextValue) == Player.Name;
    }

    // 去除隊伍序號等特殊字元(遊戲私有字元區)與空白
    private static string CleanName(string raw)
        => new(raw.Where(c => c < 0xE000 || c > 0xF8FF).ToArray().AsSpan().Trim());

    public void Dispose()
    {
        Svc.Chat.ChatMessage -= OnChatMessage;
    }
}
