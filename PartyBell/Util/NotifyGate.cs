using ECommons.DalamudServices;

namespace PartyBell.Util;

internal static class NotifyGate
{
    private const uint AfkOnlineStatusId = 17;

    public static bool IsAfk => Svc.ClientState.LocalPlayer is { } p && p.OnlineStatus.RowId == AfkOnlineStatusId;

    public static bool ShouldNotify(Configuration config)
        => config.Enabled && (!config.OnlyWhenAfk || IsAfk);
}
