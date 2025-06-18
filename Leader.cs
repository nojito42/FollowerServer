using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;
using System.Linq;

namespace FollowerPlugin;

public class Leader
{
    public string LeaderName { get; set; }
    public Element Element { get; set; }

    public Entity LastTargetedPortalOrTransition { get; set; }
    public string LeaderCurrentArea => Element?.ChildCount ==4 ? Element.Children[2].Text : Core.Current.GameController.Area.CurrentArea.Name;

    public Entity Entity => Core.Current.GameController.Entities
        .FirstOrDefault(entity => entity.IsValid && entity.Type == EntityType.Player && entity.GetComponent<Player>().PlayerName == Element[0].Text);
}