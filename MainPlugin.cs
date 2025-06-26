using ExileCore.PoEMemory.Components;
using ExileCore;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Numerics;
using FollowerPlugin;
using ExileCore.Shared.Enums;
using System.Threading;
using System.Windows.Forms;
using GameOffsets.Native;
using Shortcut = GameOffsets.Shortcut;
using ExileCore.Shared.Helpers;
using ExileCore.PoEMemory.MemoryObjects;
using System.Threading.Tasks;
using System.Diagnostics;
using ExileCore.Shared;
namespace FollowerServer;

public class MainPlugin : BaseSettingsPlugin<FollowerPluginSettings>
{
    #region Variables
    public List<PlayerSkill> LocalPlayerSkills { get; set; } = [];
    public bool IsTaskRunning { get; set; } = false;
    public Leader PartyLeader { get; private set; }
    public PlayerSkill MoveSkill => LocalPlayerSkills.LastOrDefault(x => x.Skill.Skill.Id == 10505);
    public PlayerSkill AttackSkill => LocalPlayerSkills.FirstOrDefault(x => x.Skill.Skill.IsAttack || x.Skill.Skill.IsSpell);
    DateTime lastMoveCheck = DateTime.Now;
    float lastMoveDelayMS = 20f; //ms

    public IList<Shortcut> Shortcuts { get; set; }
    public PartyServer PartyServer { get; set; }
    public PartyClient PartyClient { get; set; }
    public DateTime LastLeaderInput { get; set; } = DateTime.Now;
    #endregion
    public override Job Tick()
    {
        var currentTarget = GameController.Player.GetComponent<Actor>().CurrentAction?.Target;

        if (currentTarget != null)      
            LogMessage($"Current Target: {currentTarget.RenderName} - Type: {currentTarget.Type} - Distance: {currentTarget.DistancePlayer}");
        if (SetLeader() == false)
           return null;       
        if (Settings.Server.ToggleLeaderServer.Value)
        {
            if (PartyServer != null && PartyServer.IsRunning)
                ServerTickForLeaderBroadcast();
        }
        else
        {
            if (!IsTaskRunning && Settings.Party.ConnectClient)
                this.ConnectTask();
            if (!Settings.Server.ToggleLeaderServer && Settings.Party.Follow)
                FollowerBehavior();
        }
        return null;
    }
    private void FollowerBehavior()
    {
        SetLeader();
        SetLocalSkillsAndShortCuts();
        if (!GameController.IngameState.InGame || MenuWindow.IsOpened || !GameController.Window.IsForeground() || GameController.IsLoading)
        {
            LogMessage("Game not in focus or menu opened, skipping.");
            return;
        }

        if (GameController.CloseWindowIfOpen())
        {
            LogMessage("Flagged panels found, skipping follower behavior.");
            return;
        }

        if (PartyLeader.Entity != null && (PartyLeader.Entity.GetComponent<Actor>().Action == ActionFlags.None && GameController.Player.Buffs.Any(b => b.Name.Equals("grace_period"))))
        {
            LogMessage(" is in grace period, skipping behaviors for now.");
            return;
        }
        LogMessage($"LeaderENTITY: {PartyLeader.Entity?.GetComponent<Player>()?.PlayerName} - Zone: {PartyLeader.IsSameZone} - Current Area: {GameController.Area.CurrentArea.Name}", 0.5f);
        // Cas 3 : Leader est sur la même map et utilise une transition ou un portail
        if (PartyLeader != null && PartyLeader.IsSameZone && PartyLeader.Entity != null &&
         PartyLeader.Entity.TryGetComponent<Actor>(out Actor leaderActor) &&
        leaderActor.CurrentAction != null &&
        leaderActor.CurrentAction.Target != null)
        {
            var t = leaderActor.CurrentAction.Target;

            if (t.Type == EntityType.AreaTransition ||
                t.Type == EntityType.Portal ||
                t.Type == EntityType.TownPortal ||
                (t.Metadata != null && t.Metadata.StartsWith("Metadata/Terrain/Leagues/Harvest/Objects/HarvestPortalToggleableReverse")))
            {
                PartyLeader.LastTargetedPortalOrTransition = t;
            }
        }

        // Cas 3 : Le leader vient de prendre un portail et on le suit
        if (PartyLeader.LastTargetedPortalOrTransition != null &&
            PartyLeader.IsSameZone)
        {
            var portal = PartyLeader.LastTargetedPortalOrTransition;
            var portalPosition = portal.BoundsCenterPosNum;
            var screenPos = GameController.IngameState.Data.GetWorldScreenPosition(portalPosition);
            this.TryDoAction(() =>
            {
                var castWithTarget = GameController.PluginBridge
                    .GetMethod<Action<Entity, uint>>("MagicInput.CastSkillWithTarget");
                castWithTarget(portal, 0x400);
                Thread.Sleep(100); // petite pause pour laisser le temps à l'action de se lancer
            });
            DateTime startTime = DateTime.Now;
            while (GameController.Player.GetComponent<Actor>()?.CurrentAction?.Target == portal || this.GetBuffs().Any(b => b.Name.Equals("grace_period")) ||GameController.IsLoading)
            {
                var MyTarget = GameController.Player.GetComponent<Actor>().CurrentAction?.Target;
                // Fix for the line causing multiple errors
                if ((DateTime.Now - startTime).TotalSeconds > 5)
                {
                    LogError("Timeout while trying to follow portal, aborting.");
                    PartyLeader.LastTargetedPortalOrTransition = null;
                    return;
                }


                MyTarget = GameController.Player.GetComponent<Actor>().CurrentAction?.Target;

                //if (this.GetBuffs().Any(b => b.Name.Equals("grace_period")) || GameController.IsLoading)
                //{
                //    LogMessage($"Successfully followed portal: {portal.RenderName}", 100);
                //    PartyLeader.LastTargetedPortalOrTransition = null;
                //    Thread.Sleep(800);
                //    return;
                //}

                //if (MyTarget != portal)
                //{
                //    LogMessage("Portal action completed, player is no longer targeting it.", 100);
                //    PartyLeader.LastTargetedPortalOrTransition = null;
                //    Thread.Sleep(800);
                //    return;
                //}

                Thread.Sleep(100); // petite pause entre les vérifications
            }
            PartyLeader.LastTargetedPortalOrTransition = null;
            LogError("Failed to follow portal after all attempts");
            return;
        }
        // Cas 1 : On est en hideout, et le leader est en map -------------------> A CHECKER
        if (GameController.Area.CurrentArea.IsHideout && (!PartyLeader.IsSameZone))
        {
            LogMessage($"cas 1 : en hidout et le leader est surment en map ou ailleurs? ");


            var townPortals = GameController.EntityListWrapper.ValidEntitiesByType[EntityType.TownPortal]
                .Where(x => x.IsValid && x.IsTargetable)
                .OrderBy(e => e.DistancePlayer)
                .ToList();

            var firstTP = townPortals.FirstOrDefault(tp => tp.RenderName == PartyLeader.Element.ZoneName);

            if (firstTP != null && firstTP.DistancePlayer < 50)
            {
                LogMessage($"Found town portal to follow: {firstTP.RenderName} distance{firstTP.DistancePlayer}", 0.5f);
                this.TryDoAction(() =>
                {
                    var castWithTarget = GameController.PluginBridge
                        .GetMethod<Action<Entity, uint>>("MagicInput.CastSkillWithTarget");
                    castWithTarget(firstTP, 0x400);
                });
                return;
            }
            else
            {

                var leaderTpElement = PartyLeader.Element.TeleportButton;
                if (leaderTpElement?.IsActive == true)
                {
                    Graphics.DrawFrame(leaderTpElement.GetClientRect(), SharpDX.Color.Red, 2);
                    Input.SetCursorPos(leaderTpElement.GetClientRect().Center.ToVector2Num());
                    Input.Click(MouseButtons.Left);
                    Input.KeyDown(Keys.Enter);
                    Input.KeyUp(Keys.Enter);
                    Thread.Sleep(1000);
                    PartyLeader.LastTargetedPortalOrTransition = null;
                    return;
                }
            }

        }
        //cas 2 : Leader n'est pas du tout sur la même map

        if (!PartyLeader.IsSameZone && !GameController.Area.CurrentArea.IsHideout)
        {
            LogMessage($"cas 2 : Leader n'est pas du tout sur la même map, on va essayer de le suivre");

            var ui = GameController.IngameState.IngameUi;
            if (PartyLeader.Element.TeleportButton?.IsActive == true)
            {
                Graphics.DrawFrame(PartyLeader.Element.TeleportButton.GetClientRect(), SharpDX.Color.Red, 2);
                Input.SetCursorPos(PartyLeader.Element.TeleportButton.GetClientRect().Center.ToVector2Num());
                Input.Click(MouseButtons.Left);
                Input.KeyDown(Keys.Enter);
                Input.KeyUp(Keys.Enter);
                Thread.Sleep(1000);
                PartyLeader.LastTargetedPortalOrTransition = null;
                return;
            }
        }
        // Cas 5 : fallback si rien d’autre ne s’est passé, gérer comportement normal
        LogMessage("Cas 5 : fallback, gérer comportement normal", 0.5f);
        PartyLeader.LastTargetedPortalOrTransition = null; // Reset last targeted portal or transition
        ManageLeaderOnSameMap();
    }
    private void ManageLeaderOnSameMap()
    {
        var leaderEntity = PartyLeader.Entity;
        var playerEntity = GameController.Player;
        SetLocalSkillsAndShortCuts();
        if (leaderEntity != null)
        {
            var leaderaction = leaderEntity.GetComponent<Actor>().CurrentAction;
            bool isTravelSkill = leaderaction != null && leaderaction.Skill != null && leaderaction.Skill.GetStat(GameStat.SkillIsTravelSkill) > 0;
            if (Settings.Party.UseSmartTPSkill && isTravelSkill)
            {
                var myTravelSkill = GameController.Player.GetComponent<Actor>().ActorSkills.FirstOrDefault(x => x.GetStat(GameStat.SkillIsTravelSkill) > 0 && x.IsOnSkillBar);
                LogError($"My Travel Skill: {myTravelSkill?.Name} {myTravelSkill?.SkillSlotIndex}");
                if (myTravelSkill != null)
                {

                    this.TryDoAction(() =>
                    {
                        var wts = GameController.IngameState.Data.GetGridScreenPosition(leaderaction.Destination);
                        Input.SetCursorPos(wts);
                        Thread.Sleep(50);

                        var scs = Shortcuts.Skip(7).Take(13).ToList()[myTravelSkill.SkillSlotIndex];
                        scs.PressShortCut(10);

                        LogError($"Pressed Travel Skill: {myTravelSkill.Name} {myTravelSkill.SkillSlotIndex} with shortcut {scs.MainKey} {scs.Modifier}");
                        return;
                    });
                }
            }
            if (Settings.Party.UseCriesAuto && leaderEntity.DistancePlayer < 20 && (GameController.Area.CurrentArea.IsHideout == false && GameController.Area.CurrentArea.IsTown == false))
            {

                if (GameController.Player.GetComponent<Actor>().Action == ActionFlags.Moving && Settings.Party.OnlyCryWhenIdle)
                {
                    LogMessage("Player is moving, skipping cries auto usage.");

                }
                else
                {
                    var crySkills = GameController.Player.GetComponent<Actor>().ActorSkills
                        .Where(x => x.IsCry && x.IsOnSkillBar && x.IsOnCooldown == false)
                        .ToList();
                    foreach (var crySkill in crySkills)
                    {
                        if(PartyLeader.Entity.DistancePlayer > 20)
                        {
                            LogMessage($"Leader is too far away ({PartyLeader.Entity.DistancePlayer} > {Settings.Party.KeepLeaderInRange.Value}), skipping cry skill: {crySkill.Name}");
                            break;
                        }
                        if (this.GetBuffs().Any(b => b.Name.Contains(crySkill.InternalName)))
                        {
                            continue;
                        }
                        else if (GameController.Player.GetComponent<Life>().CurMana < crySkill.Cost)
                        {
                            continue;
                        }
                        this.TryDoAction(() =>
                        {
                            var scs = Shortcuts.Skip(7).Take(13).ToList()[crySkill.SkillSlotIndex];
                            scs.PressShortCut(1);
                        });
                    }
                }
            }
            if (leaderEntity.DistancePlayer > Settings.Party.KeepLeaderInRange.Value && Settings.Party.Follow)
            {
                var moveSkill = MoveSkill;
                var playeraction = playerEntity.GetComponent<Actor>().Action;
                bool playerisattacking = playeraction == ActionFlags.UsingAbility;
                lastMoveDelayMS = playerisattacking ? 250f : 20f;
                if (moveSkill != null)
                {
                    if (lastMoveCheck.AddMilliseconds(lastMoveDelayMS) >= DateTime.Now)
                        return;
                    lastMoveCheck = DateTime.Now;
                    var pf = leaderEntity.GetComponent<Pathfinding>();

                    if (pf.PathingNodes.Count > 0)
                    {
                        var lastNode = pf.PathingNodes.Last();

                        this.TryDoAction(() =>
                        {
                            var castWithPos = GameController.PluginBridge
                                .GetMethod<Action<Vector2i, uint>>("MagicInput.CastSkillWithPosition");
                            castWithPos(lastNode, 0x400);
                        });
                    }
                    else if (!playerisattacking && playerEntity.DistancePlayer <= Settings.Party.KeepLeaderInRange.Value)
                    {
                        this.TryDoAction(() =>
                        {
                            LogError($"Casting skill with target: {moveSkill.Skill.Skill.Name}");
                            var castWithTarget = GameController.PluginBridge
                                .GetMethod<Action<Entity, uint>>("MagicInput.CastSkillWithTarget");
                            castWithTarget(leaderEntity, 0x400);
                        });
                    }
                }
            }
            else if (leaderEntity.DistancePlayer <= Settings.Party.KeepLeaderInRange.Value)
            {
                var opt = GameController.IngameState.IngameUi.ItemsOnGroundLabelElement.LabelsOnGroundVisible
                    .Where(c => c?.ItemOnGround != null && c.ItemOnGround.Metadata != null &&
                                c.ItemOnGround.Metadata.Contains("Metadata/Monsters/Mercenaries"))
                    .FirstOrDefault();
                if (opt != null)
                {
                    var dist = opt.ItemOnGround.DistancePlayer;
                    LogError($"Found item on ground: {opt} {dist} {opt.Label.ChildCount}");
                    if (opt.Label.ChildCount == 3 && opt.Label[2].IsVisible && dist <= 20)
                    {
                        var screenPos = opt.Label.GetClientRect().Center.ToVector2Num();
                        Graphics.DrawFrame(opt.Label[2].GetClientRect(), SharpDX.Color.Red, 1);
                         
                        Input.SetCursorPos(opt.Label[2].GetClientRect().Center.ToVector2Num());
                        Thread.Sleep(20);
                        Input.Click(MouseButtons.Left);
                        Thread.Sleep(100);
                    }
                    else
                    {
                        LogError("Item on ground label does not have the expected child count or visibility.");
                    }

                }
                else
                    LogError("opt == nujll", 100);

                if (Input.IsKeyDown((Keys)MoveSkill.Shortcut.MainKey))
                {
                    Input.KeyUp((Keys)MoveSkill.Shortcut.MainKey);
                }
            }
        }
    }
    private void SetLocalSkillsAndShortCuts()
    {
        var sc = Shortcuts.Skip(7).Take(13).ToList();
        LocalPlayerSkills.Clear();

        for (int i = 0; i < sc.Count; i++)
        {
            LocalPlayerSkills.Add(new PlayerSkill
            {
                Shortcut = sc[i],
                Skill = GameController.IngameState.IngameUi.SkillBar.Skills[i]
            });
        }
    }
    private void ToggleLeaderServer()
    {
        if (Settings.Server.ToggleLeaderServer.Value)
        {
            PartyServer ??= new PartyServer(this);

            if (Settings.Server.ToggleLeaderServer.Value && !PartyServer.IsRunning)
            {
                LogError("Starting server...");
                PartyServer.Start();
            }
            else
            {
                Settings.Server.ToggleLeaderServer.Value = false;
            }
        }
        else
        {
            if (PartyServer != null && PartyServer.IsRunning)
            {
                LogError("Stopping server...");
                PartyServer.Stop();
            }

        }
    }
    public bool SetLeader()
    {
        var pt = GameController.Party();
        if (pt == null)
            return false;
        Settings.Party.LeaderSelect.SetListValues(pt.ToList().Select(m => m.PlayerName.ToString()).ToList());

        var leaderElement = pt.FirstOrDefault(x => x.PlayerName == Settings.Party.LeaderSelect);
        if (leaderElement == null)
            return false;

        PartyLeader = new Leader
        {
            Name = leaderElement.PlayerName,
            Element = leaderElement,
            LastTargetedPortalOrTransition = null
        };
        return true;
    }
    private void ServerTickForLeaderBroadcast()
    {
        if (GameController.IsLoading) return;
        if (PartyServer == null)
        {
            LogMessage("PartyServer is null. Exiting Tick.");
            return;
        }

        if (!Settings.Server.ToggleLeaderServer.Value)
        {
            LogMessage("Not the leader or server host. Exiting Tick.");
            return;
        }


        var actor = GameController.Player.GetComponent<Actor>();
        var skillbar = GameController.IngameState.IngameUi.SkillBar;
        LocalPlayerSkills.Clear();
        LocalPlayerSkills.AddRange(actor.ActorSkills
            .Where(x => x.IsOnSkillBar && x.SkillSlotIndex >= 0 && x.SkillSlotIndex < 13)
            .Select(x => new PlayerSkill
            {
                Shortcut = Shortcuts.Skip(7).Take(13).ToList()[x.SkillSlotIndex],
                Skill = skillbar.Skills[x.SkillSlotIndex]
            }));
        var pressedSkills = LocalPlayerSkills.Where(x => x.Shortcut.IsShortCutPressed() /*&& Skills.IndexOf(x) > 0*/);
        if (pressedSkills == null)
        {
            LogMessage("FoeSkill is null. Exiting Tick.");
            return;
        }
        else
        {
            if (DateTime.Now.Subtract(LastLeaderInput).TotalMilliseconds < Settings.Server.ServerTick)
            {
                return;
            }


            LastLeaderInput = DateTime.Now; // Move this here, before processing multiple skills
            var mouseCoords = Input.MousePositionNum;
            var currentSkill = GameController.Player.GetComponent<Actor>().CurrentAction?.Skill;
            var window = GameController.Window.GetWindowRectangleTimeCache;
            if (GameController.Area.CurrentArea.IsHideout)
            {
                return;
            }

            LogMessage($"Broadcasting {pressedSkills.Count()} pressed skills to followers.", 0.5f);

            foreach (var foe in pressedSkills)
            {
                LeaderInput leaderInput = new()
                {
                    LeaderName = GameController.Player.GetComponent<Player>().PlayerName,
                    RawInput = foe.Skill.Skill.SkillSlotIndex.ToString(),
                    MouseCoords = new(mouseCoords.X / window.Width, mouseCoords.Y / window.Height),
                };
                PartyServer.BroadcastLeaderInput(leaderInput);
            }
        }
    }
    public override void Render()
    {
        base.Render();
        if (Settings.Server.ToggleLeaderServer && PartyServer != null && PartyServer.IsRunning)
        {
            Graphics.DrawText($"Server is running on {PartyServer.ServerIP}", new Vector2(100, 100));
            var curAction = GameController.Player.GetComponent<Actor>().Action;
            var curString = "None";
            if (curAction == ActionFlags.UsingAbility)
            {
                curString = GameController.Player.GetComponent<Actor>().CurrentAction.Skill.Name;
            }
            Graphics.DrawText($"Using Ability {curString}", new Vector2(100, 120));
            if (Settings.Server.DrawFollowersCircle)
            {
                var pt = GameController.IngameState.IngameUi.PartyElement.PlayerElements.ToList();
                if (pt == null || pt.Count == 0)
                {
                    LogMessage("No party members found to draw circles.", 100);
                    return;
                }
                int i = 0;
                pt.ForEach(pm =>

                {
                    SharpDX.Color[] colors = { SharpDX.Color.Red, SharpDX.Color.Green, SharpDX.Color.Blue, SharpDX.Color.Yellow, SharpDX.Color.Purple };
                    var e = GameController.EntityListWrapper.ValidEntitiesByType[EntityType.Player]
                        .FirstOrDefault(x => x.GetComponent<Player>()?.PlayerName == pm.PlayerName);
                    if (e == null)
                    {
                        LogMessage($"Entity for player {pm.PlayerName} not found.", 100);
                        return;
                    }
                    var gp = GameController.IngameState.Data.GetWorldScreenPosition(e.PosNum);
                    if (GameController.Window.GetWindowRectangleTimeCache.Contains(gp))
                    {
                        Graphics.DrawCircleInWorld(e.PosNum, Settings.Server.CircleRadius, colors[i] with { A = (byte)Settings.Server.CircleAlpha }, Settings.Server.CircleThickness);
                        i++;
                    }

                   
                });

              
            }
        }
        else if (Settings.Party.ConnectClient && PartyClient != null && PartyClient.IsConnected)
        {

           var leaderBox = PartyLeader?.Element?.GetClientRect();
            if (PartyLeader != null && leaderBox.Value.Top > 0.0f)
            {
                Graphics.DrawFrame(leaderBox.Value, SharpDX.Color.Green, 2);
                Graphics.DrawText($"Connected: {Settings.Party.ServerIP.Value}", new Vector2(0, PartyLeader.Element.GetClientRectCache.Top - 20), SharpDX.Color.Green);
            }
           
                     
        }
    }
    #region InitConnectDisposeClose
    public override bool Initialise()
    {
        IsTaskRunning = false;
        var mem = GameController.Memory;
        var sc = GameController.IngameState.ShortcutSettings.Shortcuts;
        Shortcuts = GameController.IngameState.ShortcutSettings.Shortcuts;// sc3;
        if (Shortcuts == null || Shortcuts.Count <= 5)
        {
            LogError("No shortcuts found. Please check your game settings.", 100);
            return false;
        }
        Settings.Party.ConnectClient.OnValueChanged += (v, a) =>
        {
            if (a)
            {
                LogMessage("Connecting to party server...", 0.5f);
                PartyClient = new PartyClient(this);
                ConnectToPartyServer();
            }
            else
            {
                LogMessage("Disconnecting from party server...", 0.5f);
                PartyClient?.Disconnect();
                PartyClient = null;
            }
        };
        Settings.Server.ToggleLeaderServer.OnValueChanged += (foe, ar) =>
        {
            if (ar && IsTaskRunning == false)
            {
                PartyServer = new PartyServer(this);

                LogMessage("Starting leader server...", 0.5f);
                PartyServer.Start();
            }

           ;
        };
        //Settings.Enable.OnValueChanged += (foe, ar) =>
        //{
        //    if (ar)
        //    {
        //        LogMessage("FollowerPlugin enabled.", 0.5f);
        //    }
        //    else
        //    {
        //        LogMessage("FollowerPlugin disabled.", 0.5f);
        //        this.DisconnectWithMessage("FollowerPlugin has been disabled. Disconnecting from party server.");
        //    }
        //};
        ToggleLeaderServer();
        if (IsTaskRunning == false && Settings.Party.ConnectClient)
            this.ConnectTask();
        return true;
    }

    //private void ConnectTask()
    //{
    //    IsTaskRunning = true;
    //    if (PartyClient != null && PartyClient.IsConnected)       
    //        return;    
    //    PartyServer = new PartyServer(this);
    //    LogMessage("Starting connection task to party server...", 0.5f);
    //    _ = Task.Run(async () =>
    //    {

    //        while (Settings.Party.ConnectClient && (PartyClient == null || PartyClient.IsConnected == false))

    //        {
    //            if (GameController.Party().Count <= 0)
    //                continue;

    //            LogMessage("Attempting to reconnect to party server...", 0.5f);

    //            if (PartyClient == null)
    //                PartyClient = new PartyClient(this);
    //            else
    //                ConnectToPartyServer();
    //            await Task.Delay(1000);
    //        }
    //    });
    //    LogError("task ended", 1.0f);
    //    IsTaskRunning = false;

    //}
    public void ConnectToPartyServer()
    {
        if (PartyClient.IsConnected && !Settings.Server.ToggleLeaderServer)
        {
            PartyClient.SendMessage(MessageType.Order, "I'm already connected.");
            return;
        }
        if (PartyClient == null)
        {
            PartyClient = new PartyClient(this);
        }
        if (!PartyClient.IsConnected)
            PartyClient.Connect();
    }
    public override void Dispose()
    {
        this.DisconnectWithMessage("Disposing FollowerPlugin.");
        base.Dispose();
    }
    public override void OnClose()
    {
        this.DisconnectWithMessage("Closing FollowerPlugin.");
        base.OnClose();
    }
    public override void OnPluginDestroyForHotReload()
    {
        this.DisconnectWithMessage("Destroying FollowerPlugin for hot reload.");
        base.OnPluginDestroyForHotReload();
    }
    public override void OnUnload()
    {
        this.DisconnectWithMessage("Unloading FollowerPlugin.");
        base.OnUnload();
    }
    #endregion
}

public static class ServerClientExtensions
{
    public static Coroutine LoginCoroutine { get; private set; }

    public static void ConnectTask(this MainPlugin p)
    {
        if (p.PartyClient != null && p.PartyClient.IsConnected)
            return;

        // Si une coroutine existe déjà et n'est pas terminée, on ne la relance pas
        if (LoginCoroutine != null && !LoginCoroutine.IsDone)
        {
            p.LogMessage("Coroutine already running. Skipping new start.", 0.5f);
            return;
        }

        // Si une coroutine existe mais est finie, on la clean
        if (LoginCoroutine != null && LoginCoroutine.IsDone)
        {
            
            LoginCoroutine = null;
        }

        p.LogMessage("Starting connection task to party server...", 0.5f);

        LoginCoroutine = new Coroutine(() =>
        {
            p.IsTaskRunning = true;

            while (p.PartyClient?.IsConnected == false)
            {
                // Si le plugin est désactivé ou si les paramètres ont changé
                if (!p.Settings.Enable || !p.Settings.Party.ConnectClient)
                {
                    p.LogMessage("Plugin disabled or settings turned off. Stopping coroutine.", 0.5f);
                    p.DisconnectWithMessage("FollowerPlugin is disabled, stopping connection task.");
                    break;
                }

                if (p.GameController.Party()?.Count > 0)
                {
                    p.LogMessage("Attempting to reconnect to party server...", 1.0f);
                    p.ConnectToPartyServer();
                }

                Thread.Sleep(1000); // Pour limiter la boucle
            }

            p.LogMessage("Connection coroutine ended.", 1.0f);
            p.IsTaskRunning = false;

        }, 2, p, "ConnectRoutine", true);

        Core.ParallelRunner.Run(LoginCoroutine);
    }
}
