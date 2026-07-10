using Dalamud.Interface.Windowing;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.PartyFunctions;
using ImGuiNET;
using PartyBell.Discord;
using PartyBell.Recruitment;
using PartyBell.Util;
using System;
using System.Numerics;
using System.Threading.Tasks;

namespace PartyBell.Windows;

public class ConfigWindow : Window
{
    private static readonly Vector4 WarnColor = new(1f, 0.4f, 0.4f, 1f);

    private readonly Configuration config;
    private readonly WebhookClient webhook;
    private readonly RecruitmentRefresher refresher;

    private bool hideWebhookUrl = true;
    private string testStatus = string.Empty;

    public ConfigWindow(Configuration config, WebhookClient webhook, RecruitmentRefresher refresher)
        : base("PartyBell 設定###PartyBellConfig")
    {
        this.config = config;
        this.webhook = webhook;
        this.refresher = refresher;
        Size = new Vector2(480, 440);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        if (!ImGui.BeginTabBar("##pbTabs"))
            return;

        if (ImGui.BeginTabItem("狀態"))
        {
            DrawStatusTab();
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("通知"))
        {
            DrawNotifyTab();
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("自動化"))
        {
            DrawAutomationTab();
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("Webhook"))
        {
            DrawWebhookTab();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private void DrawWebhookTab()
    {
        var enabled = config.Enabled;
        if (ImGui.Checkbox("啟用通知", ref enabled))
        {
            config.Enabled = enabled;
            config.Save();
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Webhook URL");
        var url = config.WebhookUrl;
        var flags = hideWebhookUrl ? ImGuiInputTextFlags.Password : ImGuiInputTextFlags.None;
        ImGui.SetNextItemWidth(-90);
        if (ImGui.InputText("##webhookUrl", ref url, 400, flags))
        {
            config.WebhookUrl = url.Trim();
            config.Save();
        }
        ImGui.SameLine();
        if (ImGui.Button(hideWebhookUrl ? "顯示" : "隱藏"))
            hideWebhookUrl = !hideWebhookUrl;

        ImGui.Spacing();
        var mention = config.MentionTarget;
        ImGui.SetNextItemWidth(220);
        if (ImGui.InputTextWithHint("Tag 目標", "Discord 使用者 ID / everyone / here", ref mention, 64))
        {
            config.MentionTarget = mention.Trim();
            config.Save();
        }
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("勾選「Tag」的通知會同時 @ 提及這個目標。\n使用者 ID:Discord 開發者模式 → 右鍵使用者 → 複製 ID");

        ImGui.Spacing();
        if (ImGui.Button("發送測試訊息"))
        {
            testStatus = "傳送中…";
            _ = Task.Run(async () =>
            {
                var ok = await webhook.SendTestAsync();
                testStatus = ok ? "✓ 發送成功,請檢查 Discord 頻道" : "✗ 發送失敗(詳見 /xllog)";
            });
        }
        if (!string.IsNullOrEmpty(testStatus))
        {
            ImGui.SameLine();
            ImGui.TextUnformatted(testStatus);
        }
    }

    private void DrawNotifyTab()
    {
        var onlyAfk = config.OnlyWhenAfk;
        if (ImGui.Checkbox("只在 AFK 狀態時通知", ref onlyAfk))
        {
            config.OnlyWhenAfk = onlyAfk;
            config.Save();
        }

        var muteInDuty = config.MuteInDuty;
        if (ImGui.Checkbox("進入副本後不通知", ref muteInDuty))
        {
            config.MuteInDuty = muteInDuty;
            config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("人在副本內時,一律不發送任何通知。");

        ImGui.Spacing();
        ImGui.TextDisabled("通知類型                          Tag = 同時 @ 提及");
        ImGui.Separator();

        DrawToggle("隊員加入/退出", ref config.NotifyPartyJoinLeave, ref config.MentionOnPartyJoinLeave);
        DrawToggle("隊伍滿員 (8人)", ref config.NotifyPartyFull, ref config.MentionOnPartyFull);
        DrawToggle("隊伍頻道訊息", ref config.NotifyPartyChat, ref config.MentionOnPartyChat);
        DrawToggle("密語 (Tell)", ref config.NotifyTell, ref config.MentionOnTell);
        DrawToggle("副本排到 (Duty Pop)", ref config.NotifyDutyPop, ref config.MentionOnDutyPop);
    }

    private void DrawAutomationTab()
    {
        ImGui.TextUnformatted("招募自動刷新(招募發布 60 分鐘後會過期)");
        ImGui.Separator();

        if (!refresher.SigAvailable)
        {
            ImGui.TextColored(WarnColor, "⚠ 遊戲函式定位失敗,自動刷新不可用(可能因遊戲版本更新)");
        }
        else
        {
            var autoRefresh = config.AutoRefreshEnabled;
            if (ImGui.Checkbox("啟用自動重新發布", ref autoRefresh))
            {
                config.AutoRefreshEnabled = autoRefresh;
                config.Save();
            }

            var beforeMins = config.RefreshBeforeExpiryMins;
            ImGui.SetNextItemWidth(120);
            if (ImGui.SliderInt("到期前幾分鐘刷新", ref beforeMins, 1, 30))
            {
                config.RefreshBeforeExpiryMins = beforeMins;
                config.Save();
            }

            var maxCount = config.MaxAutoRefreshCount;
            ImGui.SetNextItemWidth(120);
            if (ImGui.InputInt("刷新次數上限(-1 = 不限)", ref maxCount))
            {
                config.MaxAutoRefreshCount = Math.Max(-1, maxCount);
                config.Save();
            }

            var notifyRefresh = config.NotifyOnRefresh;
            if (ImGui.Checkbox("刷新成功/失敗時發 Discord 通知", ref notifyRefresh))
            {
                config.NotifyOnRefresh = notifyRefresh;
                config.Save();
            }

            var mentionHalt = config.MentionOnRefreshHalt;
            if (ImGui.Checkbox("連續失敗停止時 Tag", ref mentionHalt))
            {
                config.MentionOnRefreshHalt = mentionHalt;
                config.Save();
            }
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("副本自動接受");
        ImGui.Separator();

        var autoAccept = config.AutoAcceptDuty;
        if (ImGui.Checkbox("排到副本時自動按下「出發」", ref autoAccept))
        {
            config.AutoAcceptDuty = autoAccept;
            config.Save();
        }

        if (config.AutoAcceptDuty)
        {
            var delay = config.AutoAcceptDelaySecs;
            ImGui.SetNextItemWidth(120);
            if (ImGui.SliderInt("延遲秒數", ref delay, 1, 40))
            {
                config.AutoAcceptDelaySecs = delay;
                config.Save();
            }
            ImGui.TextDisabled("確認視窗出現後等待此秒數再自動接受;期間手動按掉則不動作。");
        }
    }

    private void DrawStatusTab()
    {
        DrawPartyStatus();
        if (NotifyGate.MutedByDuty(config))
            ImGui.TextColored(WarnColor, "⚠ 目前在副本內,通知已暫停");
        ImGui.Separator();

        if (!refresher.IsRecruiting)
        {
            ImGui.TextDisabled("未在招募中");
            return;
        }

        var start = refresher.RecruitStartUtc;
        if (start != null)
        {
            var elapsed = DateTime.UtcNow - start.Value;
            ImGui.TextUnformatted($"招募中|已經過 {(int)elapsed.TotalMinutes} 分鐘|已自動刷新 {refresher.RefreshCount} 次");
            if (refresher.NextRefreshUtc is { } next)
            {
                var remain = next - DateTime.UtcNow;
                ImGui.TextUnformatted(remain > TimeSpan.Zero
                    ? $"下次自動刷新:約 {(int)remain.TotalMinutes} 分 {remain.Seconds} 秒後"
                    : "即將刷新…");
            }
            if (refresher.RefreshHalted)
                ImGui.TextColored(WarnColor, "⚠ 自動刷新已因連續失敗而停止");
        }

        if (ImGui.Button("立即手動刷新招募"))
        {
            if (!refresher.TriggerManualRefresh())
                testStatus = "✗ 目前無法刷新";
        }
    }

    private static void DrawPartyStatus()
    {
        if (!Svc.ClientState.IsLoggedIn || !Player.Available)
        {
            ImGui.TextDisabled("未登入");
            return;
        }

        var count = UniversalParty.Length;
        if (count > 1)
        {
            var kind = UniversalParty.IsAlliance ? "跨界團隊" : UniversalParty.IsCrossWorldParty ? "跨界組隊" : "一般隊伍";
            ImGui.TextUnformatted($"隊伍中:{count} 人({kind}),隊員變動通知運作中");
        }
        else
        {
            ImGui.TextDisabled("不在隊伍中");
        }
    }

    private void DrawToggle(string label, ref bool value, ref bool mention)
    {
        if (ImGui.Checkbox(label, ref value))
            config.Save();
        if (value)
        {
            ImGui.SameLine(230);
            if (ImGui.Checkbox($"Tag##{label}", ref mention))
                config.Save();
        }
    }
}
