using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using ECommons;
using ECommons.DalamudServices;
using PartyBell.Discord;
using PartyBell.Listeners;
using PartyBell.Recruitment;
using PartyBell.Windows;

namespace PartyBell;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/pbell";

    private readonly WindowSystem windowSystem = new("PartyBell");
    private readonly Configuration config;
    private readonly WebhookClient webhook;
    private readonly PartyListener partyListener;
    private readonly ChatListener chatListener;
    private readonly DutyListener dutyListener;
    private readonly RecruitmentRefresher refresher;
    private readonly ConfigWindow configWindow;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        ECommonsMain.Init(pluginInterface, this);

        config = Svc.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        webhook = new WebhookClient(config);
        partyListener = new PartyListener(config, webhook);
        chatListener = new ChatListener(config, webhook);
        dutyListener = new DutyListener(config, webhook);
        refresher = new RecruitmentRefresher(config, webhook);

        configWindow = new ConfigWindow(config, webhook, refresher);
        windowSystem.AddWindow(configWindow);

        Svc.PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        Svc.PluginInterface.UiBuilder.OpenConfigUi += OpenConfig;
        Svc.PluginInterface.UiBuilder.OpenMainUi += OpenConfig;

        Svc.Commands.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "開啟 PartyBell 設定視窗",
        });
    }

    private void OnCommand(string command, string args) => OpenConfig();

    private void OpenConfig() => configWindow.IsOpen = true;

    public void Dispose()
    {
        Svc.Commands.RemoveHandler(CommandName);
        Svc.PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        Svc.PluginInterface.UiBuilder.OpenConfigUi -= OpenConfig;
        Svc.PluginInterface.UiBuilder.OpenMainUi -= OpenConfig;
        windowSystem.RemoveAllWindows();

        refresher.Dispose();
        dutyListener.Dispose();
        chatListener.Dispose();
        partyListener.Dispose();
        webhook.Dispose();

        ECommonsMain.Dispose();
    }
}
