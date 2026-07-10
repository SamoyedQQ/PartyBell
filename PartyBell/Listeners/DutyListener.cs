using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using ECommons;
using ECommons.DalamudServices;
using ECommons.UIHelpers.AddonMasterImplementations;
using Lumina.Excel.Sheets;
using PartyBell.Discord;
using PartyBell.Util;
using System;
using System.Threading.Tasks;

namespace PartyBell.Listeners;

/// <summary>
/// 副本排到通知與自動接受。偵測雙保險:
/// 1. IClientState.CfPop — 任務搜尋器排到(可取得副本名)
/// 2. ContentsFinderConfirm 視窗開啟 — 涵蓋招募板人齊出發等 CfPop 沒觸發的情境
/// </summary>
public sealed class DutyListener : IDisposable
{
    private const string ConfirmAddonName = "ContentsFinderConfirm";

    private readonly Configuration config;
    private readonly WebhookClient webhook;
    private DateTime lastNotifyUtc = DateTime.MinValue;
    private bool acceptInProgress;

    public DutyListener(Configuration config, WebhookClient webhook)
    {
        this.config = config;
        this.webhook = webhook;
        Svc.ClientState.CfPop += OnCfPop;
        Svc.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, ConfirmAddonName, OnConfirmAddon);
    }

    private void OnCfPop(ContentFinderCondition cfc) => Notify(cfc.Name.ExtractText());

    private void OnConfirmAddon(AddonEvent type, AddonArgs args)
    {
        Notify(null);

        if (config.AutoAcceptDuty && !acceptInProgress)
        {
            acceptInProgress = true;
            _ = Task.Run(AutoAcceptAsync);
        }
    }

    private void Notify(string? dutyName)
    {
        if (!config.NotifyDutyPop || !NotifyGate.ShouldNotify(config))
            return;
        // CfPop 與確認視窗可能相繼觸發,10 秒內只通知一次
        if (DateTime.UtcNow - lastNotifyUtc < TimeSpan.FromSeconds(10))
            return;
        lastNotifyUtc = DateTime.UtcNow;

        var desc = string.IsNullOrEmpty(dutyName) ? "參加任務確認視窗已開啟" : dutyName;
        if (config.AutoAcceptDuty)
            desc += $"|{Math.Clamp(config.AutoAcceptDelaySecs, 1, 40)} 秒後自動接受";

        webhook.Enqueue("⚔️ 副本排到了!", desc, WebhookClient.ColorDuty, config.MentionOnDutyPop);
    }

    private async Task AutoAcceptAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(config.AutoAcceptDelaySecs, 1, 40)));

            var clicked = false;
            // 視窗還在就按「出發」;最多再等 5 秒(按鈕可能尚未啟用)
            for (var i = 0; i < 25 && !clicked; i++)
            {
                clicked = await Svc.Framework.RunOnFrameworkThread(static () =>
                {
                    if (GenericHelpers.TryGetAddonMaster<AddonMaster.ContentsFinderConfirm>(out var cf) && cf.IsAddonReady)
                    {
                        cf.Commence();
                        return true;
                    }
                    return false;
                });
                if (!clicked)
                    await Task.Delay(200);
            }

            if (clicked && config.NotifyDutyPop && NotifyGate.ShouldNotify(config))
                webhook.Enqueue("✅ 已自動接受副本", null, WebhookClient.ColorJoin);
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"[PartyBell] 自動接受副本失敗: {ex.Message}");
        }
        finally
        {
            acceptInProgress = false;
        }
    }

    public void Dispose()
    {
        Svc.ClientState.CfPop -= OnCfPop;
        Svc.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, ConfirmAddonName, OnConfirmAddon);
    }
}
