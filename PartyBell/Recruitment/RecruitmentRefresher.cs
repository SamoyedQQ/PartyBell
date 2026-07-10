using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.DalamudServices;
using ECommons.Throttlers;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using PartyBell.Discord;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace PartyBell.Recruitment;

/// <summary>
/// 追蹤自己發布的招募,在 60 分鐘到期前自動重新發布:
/// OpenPartyFinder(sig) 開啟自己的招募詳情 → JoinEdit 進入編輯 → Recruit 重新送出。
/// </summary>
public sealed class RecruitmentRefresher : IDisposable
{
    private const string OpenPartyFinderSig =
        "40 53 48 83 EC 20 48 8B D9 E8 ?? ?? ?? ?? 84 C0 74 07 C6 83 ?? ?? ?? ?? ?? 48 83 C4 20 5B C3 CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC 40 53";

    private const int ListingLifetimeMins = 60;
    private const int MaxConsecutiveFails = 3;

    private unsafe delegate void OpenPartyFinderDelegate(void* agentLfg, ulong contentId);

    private readonly Configuration config;
    private readonly WebhookClient webhook;
    private readonly OpenPartyFinderDelegate? openPartyFinder;

    private string? previousComment;
    private bool refreshInProgress;
    private DateTime nextAttemptUtc = DateTime.MinValue;
    private int failCount;

    /// <summary>本次招募開始(或上次刷新)時間;null = 未在招募。</summary>
    public DateTime? RecruitStartUtc { get; private set; }

    public int RefreshCount { get; private set; }
    public bool SigAvailable => openPartyFinder != null;
    public bool IsRecruiting => Svc.Condition[ConditionFlag.UsingPartyFinder];
    public bool RefreshHalted => failCount >= MaxConsecutiveFails;

    public DateTime? NextRefreshUtc
    {
        get
        {
            if (RecruitStartUtc == null)
                return null;
            var beforeMins = Math.Clamp(config.RefreshBeforeExpiryMins, 1, ListingLifetimeMins - 1);
            return RecruitStartUtc.Value.AddMinutes(ListingLifetimeMins - beforeMins);
        }
    }

    public RecruitmentRefresher(Configuration config, WebhookClient webhook)
    {
        this.config = config;
        this.webhook = webhook;

        try
        {
            var ptr = Svc.SigScanner.ScanText(OpenPartyFinderSig);
            openPartyFinder = Marshal.GetDelegateForFunctionPointer<OpenPartyFinderDelegate>(ptr);
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"[PartyBell] OpenPartyFinder signature 掃描失敗,自動刷新招募停用: {ex.Message}");
        }

        Svc.Framework.Update += OnUpdate;
    }

    private void OnUpdate(IFramework framework)
    {
        if (!EzThrottler.Throttle("PartyBell.RecruitTick", 1000))
            return;

        var recruiting = IsRecruiting;
        if (recruiting && RecruitStartUtc == null)
        {
            RecruitStartUtc = DateTime.UtcNow;
            RefreshCount = 0;
            failCount = 0;
            previousComment = null;
        }
        else if (!recruiting && RecruitStartUtc != null && !refreshInProgress)
        {
            RecruitStartUtc = null;
            RefreshCount = 0;
            failCount = 0;
        }

        if (!recruiting || refreshInProgress || !config.AutoRefreshEnabled || openPartyFinder == null)
            return;
        if (RefreshHalted)
            return;
        if (config.MaxAutoRefreshCount >= 0 && RefreshCount >= config.MaxAutoRefreshCount)
            return;
        if (DateTime.UtcNow < nextAttemptUtc)
            return;
        if (NextRefreshUtc == null || DateTime.UtcNow < NextRefreshUtc)
            return;

        // 戰鬥/過圖/過場時延後再試
        if (Svc.Condition[ConditionFlag.InCombat]
            || Svc.Condition[ConditionFlag.BetweenAreas]
            || Svc.Condition[ConditionFlag.BetweenAreas51]
            || Svc.Condition[ConditionFlag.OccupiedInCutSceneEvent])
        {
            nextAttemptUtc = DateTime.UtcNow.AddMinutes(1);
            return;
        }

        TriggerRefresh(manual: false);
    }

    /// <summary>手動觸發一次刷新(設定視窗按鈕用)。</summary>
    public bool TriggerManualRefresh()
    {
        if (!IsRecruiting || refreshInProgress || openPartyFinder == null)
            return false;
        TriggerRefresh(manual: true);
        return true;
    }

    private void TriggerRefresh(bool manual)
    {
        refreshInProgress = true;
        _ = Task.Run(async () =>
        {
            try
            {
                var ok = await RefreshSequence();
                if (ok)
                {
                    RecruitStartUtc = DateTime.UtcNow;
                    RefreshCount++;
                    failCount = 0;
                    Svc.Log.Info($"[PartyBell] 招募已重新發布(第 {RefreshCount} 次)");
                    if (config.NotifyOnRefresh)
                        webhook.Enqueue("🔄 招募已自動刷新", $"已重新發布招募(第 {RefreshCount} 次)", WebhookClient.ColorInfo);
                }
                else
                {
                    failCount++;
                    nextAttemptUtc = DateTime.UtcNow.AddMinutes(1);
                    Svc.Log.Warning($"[PartyBell] 招募刷新失敗(連續 {failCount} 次)");
                    if (config.NotifyOnRefresh || failCount >= MaxConsecutiveFails)
                    {
                        var halted = failCount >= MaxConsecutiveFails;
                        webhook.Enqueue("⚠️ 招募刷新失敗",
                            halted
                                ? $"已連續失敗 {failCount} 次,停止自動嘗試,請手動處理!"
                                : $"重新發布失敗(第 {failCount} 次),1 分鐘後重試",
                            WebhookClient.ColorSystem,
                            halted && config.MentionOnRefreshHalt);
                    }
                }
            }
            catch (Exception ex)
            {
                failCount++;
                nextAttemptUtc = DateTime.UtcNow.AddMinutes(1);
                Svc.Log.Error($"[PartyBell] 招募刷新發生例外: {ex}");
            }
            finally
            {
                refreshInProgress = false;
            }
        });
    }

    private async Task<bool> RefreshSequence()
    {
        // 步驟 1:修復被客戶端清空的招募留言,並開啟自己的招募詳情視窗
        await Svc.Framework.RunOnFrameworkThread(PrepareCommentAndOpenListing);

        // 步驟 2:等待詳情視窗,按下「加入條件變更」進入編輯
        if (!await PollOnFramework(static () =>
                GenericHelpers.TryGetAddonMaster<AddonMaster.LookingForGroupDetail>(out var detail)
                && detail.IsAddonReady
                && detail.JoinEdit()))
            return false;

        // 步驟 3:等待招募條件視窗,按下「招募隊員」重新發布
        if (!await PollOnFramework(static () =>
                GenericHelpers.TryGetAddonMaster<AddonMaster.LookingForGroupCondition>(out var cond)
                && cond.IsAddonReady
                && cond.Recruit()))
            return false;

        return true;
    }

    private unsafe void PrepareCommentAndOpenListing()
    {
        var agent = AgentLookingForGroup.Instance();
        var comment = agent->StoredRecruitmentInfo.CommentString;
        if (previousComment != null && comment != previousComment && string.IsNullOrWhiteSpace(comment))
        {
            Svc.Log.Info($"[PartyBell] 招募留言被清空,寫回:{previousComment}");
            agent->StoredRecruitmentInfo.CommentString = previousComment;
        }
        else
        {
            previousComment = comment;
        }

        openPartyFinder!(agent, Svc.ClientState.LocalContentId);
    }

    // 在主執行緒輪詢執行 action,成功回 true;逾時(約 5 秒)回 false
    private static async Task<bool> PollOnFramework(Func<bool> action)
    {
        for (var i = 0; i < 50; i++)
        {
            if (await Svc.Framework.RunOnFrameworkThread(action))
                return true;
            await Task.Delay(100);
        }
        return false;
    }

    public void Dispose()
    {
        Svc.Framework.Update -= OnUpdate;
    }
}
