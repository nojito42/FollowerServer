using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;
using System;
using System.Linq;

namespace FollowerPlugin;


public abstract class PartyMember
{
    public string Name { get; set; }
    public Element Element { get; set; }
    public string CurrentArea => Element?.ChildCount == 4 ? Element.Children[2].Text : Core.Current.GameController.Area.CurrentArea.Name;
    public Entity Entity => Core.Current.GameController.EntityListWrapper.ValidEntitiesByType[EntityType.Player]
        .FirstOrDefault(entity => entity != null && entity.IsValid && entity.Type == EntityType.Player && entity.GetComponent<Player>()?.PlayerName == Element[0].Text);
}
public class Leader : PartyMember
{
    public Entity LastTargetedPortalOrTransition { get; set; }

}