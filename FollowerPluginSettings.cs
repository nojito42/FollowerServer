using ExileCore.Shared.Attributes;
using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;
using System.Text.Json.Serialization;

namespace FollowerServer;

public class FollowerPluginSettings : ISettings
{
    public ToggleNode Enable { get; set; } = new ToggleNode(false);
    public ServerSubMenu ServerSubMenu { get; set; } = new ServerSubMenu();
    public PartySubMenu PartySubMenu { get; set; } = new PartySubMenu();
}

[Submenu]
public class ServerSubMenu
{
    public ToggleNode ToggleLeaderServer { get; set; } = new ToggleNode(false);
    public TextNode ServerIP { get; set; } = new TextNode("192.168.1.120");
    public ButtonNode ConnectClient { get; set; } = new ButtonNode();

    public RangeNode<int> ServerTick { get; set; } = new RangeNode<int>(100, 10, 1000);
}

[Submenu]
public class PartySubMenu
{
    public ListNode PartyMembers { get; set; } = new ListNode();
    public ToggleNode Follow { get; set; } = new ToggleNode(false);
    public RangeNode<int> LeaderMaxDistance { get; set; } = new RangeNode<int>(80, 30, 200);
    public RangeNode<int> KeepLeaderInRange { get; set; } = new RangeNode<int>(10, 5, 30);
    public ToggleNode FollowInTown { get; set; } = new ToggleNode(false);
    public ToggleNode UseInputManager { get; set; } = new ToggleNode(true);
    public ToggleNode DodgeRollWithLeader { get; set; } = new ToggleNode(true);
    public RangeNode<int> screenOffsetAdjustementX { get; set; } = new RangeNode<int>(0, -200, 200);
    public RangeNode<int> screenOffsetAdjustementY { get; set; } = new RangeNode<int>(0, -200, 200);

}