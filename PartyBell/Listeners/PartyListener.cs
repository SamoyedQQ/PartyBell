using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.PartyFunctions;
using ECommons.Throttlers;
using PartyBell.Discord;
using PartyBell.Util;
using System;
using System.Collections.Generic;

namespace PartyBell.Listeners;

/// <summary>
/// 以 UniversalParty(跨界/本地隊伍統一列舉)定期快照比對,偵測隊員加入與退出。
/// </summary>
public sealed class PartyListener : IDisposable
{
    private readonly Configuration config;
    private readonly WebhookClient webhook;

    private record MemberSnapshot(string NameWithWorld, string Job);

    private Dictionary<string, MemberSnapshot>? snapshot;

    public PartyListener(Configuration config, WebhookClient webhook)
    {
        this.config = config;
        this.webhook = webhook;
        Svc.Framework.Update += OnUpdate;
        Svc.ClientState.Logout += OnLogout;
    }

    private void OnLogout(int type, int code) => snapshot = null;

    private void OnUpdate(IFramework framework)
    {
        if (!EzThrottler.Throttle("PartyBell.PartyPoll", 2000))
            return;
        if (!Svc.ClientState.IsLoggedIn || !Player.Available)
        {
            snapshot = null;
            return;
        }
        // 過圖中隊伍資料可能短暫不完整,跳過本次比對
        if (Svc.Condition[ConditionFlag.BetweenAreas] || Svc.Condition[ConditionFlag.BetweenAreas51])
            return;

        var current = new Dictionary<string, MemberSnapshot>();
        foreach (var m in UniversalParty.Members)
        {
            var key = m.ContentID != 0 ? m.ContentID.ToString() : m.NameWithWorld;
            current[key] = new MemberSnapshot(m.NameWithWorld, m.ClassJob.ToString());
        }

        if (snapshot == null)
        {
            // 登入/載入後第一次輪詢:建立基準,不發通知
            snapshot = current;
            return;
        }

        if (config.NotifyPartyFull && current.Count >= 8 && snapshot.Count < 8 && NotifyGate.ShouldNotify(config))
            webhook.Enqueue("🎉 隊伍滿員!(8/8)", "所有位置已補滿,可以出發了", WebhookClient.ColorJoin, config.MentionOnPartyFull);

        if (config.NotifyPartyJoinLeave && NotifyGate.ShouldNotify(config))
        {
            foreach (var (key, m) in current)
            {
                if (!snapshot.ContainsKey(key))
                    webhook.Enqueue($"✅ {m.NameWithWorld} 加入了隊伍", $"職業:{m.Job}|目前人數:{current.Count}", WebhookClient.ColorJoin, config.MentionOnPartyJoinLeave);
            }

            foreach (var (key, m) in snapshot)
            {
                if (!current.ContainsKey(key))
                    webhook.Enqueue($"❌ {m.NameWithWorld} 離開了隊伍", $"目前人數:{current.Count}", WebhookClient.ColorLeave, config.MentionOnPartyJoinLeave);
            }
        }

        snapshot = current;
    }

    public void Dispose()
    {
        Svc.Framework.Update -= OnUpdate;
        Svc.ClientState.Logout -= OnLogout;
    }
}
