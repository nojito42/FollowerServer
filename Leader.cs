using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;
using FollowerServer;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FollowerPlugin;


public abstract class PartyMember
{
    public string Name { get; set; }
    public PartyElementPlayerElement Element { get; set; }

    public PartyElementPlayerInfo Info => Core.Current.GameController.IngameState.IngameUi.PartyElement.Information.GetValueOrDefault(Name);


    public bool IsSameZone => (bool)!Info?.IsInDifferentZone;
    //public string CurrentArea => Element?.ChildCount == 4 ? Element.Children[2].Text : Core.Current.GameController.Area.CurrentArea.Name;
    public Entity Entity => Core.Current.GameController.EntityListWrapper.ValidEntitiesByType[EntityType.Player]
        .FirstOrDefault(entity => entity != null && entity.IsValid && entity.Type == EntityType.Player && entity.GetComponent<Player>()?.PlayerName == Name);
    public List<PlayerSkill> Skills { get; set; } = [];

}
public class Leader : PartyMember
{
    public Entity LastTargetedPortalOrTransition { get; set; }

}