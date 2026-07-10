using ECommons.DalamudServices;
using ECommons.GameHelpers;

namespace PartyBell.Util;

internal static class NotifyGate
{
    private const uint AfkOnlineStatusId = 17;

    public static bool IsAfk => Svc.ClientState.LocalPlayer is { } p && p.OnlineStatus.RowId == AfkOnlineStatusId;

    public static bool InDuty => Player.Available && Player.IsInDuty;

    /// <summary>人在副本內且已開啟「副本內靜音」。</summary>
    public static bool MutedByDuty(Configuration config) => config.MuteInDuty && InDuty;

    public static bool ShouldNotify(Configuration config)
        => config.Enabled && !MutedByDuty(config) && (!config.OnlyWhenAfk || IsAfk);
}
