using ExileCore;
using ExileCore.Shared.Attributes;
using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;
using ImGuiNET;
using System;
using System.Linq;
using System.Text.Json.Serialization;

namespace FollowerServer;

public class FollowerPluginSettings : ISettings
{
    public ToggleNode Enable { get; set; } = new ToggleNode(false);
    public ServerSubMenu Server { get; set; } = new ServerSubMenu();
    public PartySubMenu Party { get; set; } = new PartySubMenu();

    public MiscSettings Misc { get; set; } = new MiscSettings();

}

[Submenu]
public class ServerSubMenu
{
    [Menu(null, "Toggle The server if you plan to have followers mimic your skillBar slots by index \n /!\\/!\\ do not connect to your server /!\\/!\\")]
    public ToggleNode ToggleLeaderServer { get; set; } = new ToggleNode(false);
    public ToggleNode DrawFollowersCircle { get; set; } = new ToggleNode(true);
    public RangeNode<int> CircleRadius { get; set; } = new RangeNode<int>(50, 10, 200);
    public RangeNode<int> CircleThickness { get; set; } = new RangeNode<int>(3, 2, 10);
    public RangeNode<int> CircleAlpha { get; set; } = new RangeNode<int>(150, 10, 255);

    public TextNode Port { get; set; } = new TextNode("5051");

    public RangeNode<int> ServerTick { get; set; } = new RangeNode<int>(100, 10, 1000);

}

[Submenu]
public class PartySubMenu
{
    [Menu(null, "Enter the IP address shown in leader/host client, do it if you want to mimic leaders skillbar slot by index\n /!\\/!\\ do not forget to enter the same port in ServerSubMenu Section/!\\/!\\")]
    public TextNode ServerIP { get; set; } = new TextNode("192.168.1.120");
    public ToggleNode ConnectClient { get; set; } = new ToggleNode(false);

    public ListNode LeaderSelect { get; set; } = new ListNode();
    public ToggleNode Follow { get; set; } = new ToggleNode(false);
    public RangeNode<int> LeaderMaxDistance { get; set; } = new RangeNode<int>(80, 30, 200);
    public RangeNode<int> KeepLeaderInRange { get; set; } = new RangeNode<int>(10, 5, 30);
    public ToggleNode FollowInTown { get; set; } = new ToggleNode(false);
    public ToggleNode UseSmartTPSkill { get; set; } = new ToggleNode(true);
    public ToggleNode UseCriesAuto { get; set; } = new ToggleNode(true);
    public ToggleNode OnlyCryWhenIdle { get; set; } = new ToggleNode(true);
    public RangeNode<int> screenOffsetAdjustementX { get; set; } = new RangeNode<int>(0, -200, 200);
    public RangeNode<int> screenOffsetAdjustementY { get; set; } = new RangeNode<int>(0, -200, 200);

}


[Submenu]
public class MiscSettings
{
    public ToggleNode ShowDebugInfo { get; set; } = new ToggleNode(false);
    public ListNode LogLevel { get; set; } = new ListNode() { Values = [.. Enum.GetNames(typeof(LogLevel))] };
}