using Dalamud.Configuration;
using ECommons.DalamudServices;
using System;

namespace PartyBell;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public string WebhookUrl = string.Empty;
    public bool Enabled = true;

    // 只在玩家掛 AFK 狀態時才發送通知
    public bool OnlyWhenAfk = false;

    // 偵測到變動時同時在遊戲內跳出通知視窗
    public bool PopupOnNotify = false;

    public bool NotifyPartyJoinLeave = true;
    public bool NotifyPartyChat = true;
    public bool NotifyTell = true;
    public bool NotifyDutyPop = true;
    public bool NotifyPartyFull = true;

    // Discord tag 目標:使用者 ID(數字)、everyone 或 here;留空 = 不 tag
    public string MentionTarget = string.Empty;
    // 各通知類型是否同時 tag
    public bool MentionOnPartyJoinLeave = false;
    public bool MentionOnPartyChat = false;
    public bool MentionOnTell = false;
    public bool MentionOnDutyPop = true;
    public bool MentionOnPartyFull = true;
    // 招募自動刷新連續失敗停止時 tag
    public bool MentionOnRefreshHalt = true;

    // 排到副本時延遲數秒後自動按下「出發」
    public bool AutoAcceptDuty = false;
    public int AutoAcceptDelaySecs = 5;

    public bool AutoRefreshEnabled = true;
    // 招募到期(60 分鐘)前幾分鐘執行自動重發
    public int RefreshBeforeExpiryMins = 5;
    // 自動重發次數上限,-1 = 不限
    public int MaxAutoRefreshCount = -1;
    public bool NotifyOnRefresh = true;

    public void Save() => Svc.PluginInterface.SavePluginConfig(this);
}
