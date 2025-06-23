using ExileCore.PoEMemory.Components;
using ExileCore;
using System.Collections.Generic;
using System;
using System.Linq;
using Vector3 = System.Numerics.Vector3;
using System.Numerics;
using FollowerPlugin;
using ExileCore.Shared.Enums;
using System.Threading;
using System.Windows.Forms;
using GameOffsets.Native;
using Shortcut = GameOffsets.Shortcut;
using ExileCore.Shared.Helpers;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Interfaces;
using System.Threading.Tasks;
using System.Diagnostics.Eventing.Reader;
using ExileCore.PoEMemory;
namespace FollowerServer;

public class MainPlugin : BaseSettingsPlugin<FollowerPluginSettings>
{
    public List<PlayerSkill> LocalPlayerSkills { get; set; } = [];


    bool IsTaskRunning = false;
    public Leader Leader { get; private set; }
    public PlayerSkill MoveSkill => LocalPlayerSkills.LastOrDefault(x => x.Skill.Skill.Id == 10505);
    public PlayerSkill AttackSkill => LocalPlayerSkills.FirstOrDefault(x => x.Skill.Skill.IsAttack || x.Skill.Skill.IsSpell);
    DateTime lastMoveCheck = DateTime.Now;
    float lastMoveDelayMS = 20f; //ms
    public IList<Shortcut> Shortcuts { get; set; }

    public PartyServer PartyServer { get; private set; }
    public PartyClient PartyClient { get; private set; }
    public DateTime LastLeaderInput { get; set; } = DateTime.Now;

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
            if(ar && IsTaskRunning == false)
            {
                PartyServer = new PartyServer(this);

                LogMessage("Starting leader server...", 0.5f);
                PartyServer.Start();
            }
            
           ;
        };
       

        //changé récemment peut break???
        ToggleLeaderServer();
        

        if(IsTaskRunning == false && Settings.Party.ConnectClient)
            ConnectTask();



        return true;
    }
    private void ConnectTask()
    {
        IsTaskRunning = true;

        _ = Task.Run(async () =>
    {
        LogError("Starting ConnectTask...", 0.5f);

        while (Settings.Party.ConnectClient && (PartyClient == null || PartyClient.IsConnected == false))

        {
            if (GameController.Party().ChildCount <= 0)
                continue;

            LogMessage("Attempting to reconnect to party server...", 0.5f);

            if (PartyClient == null)
                PartyClient = new PartyClient(this);
            else
                ConnectToPartyServer();
            await Task.Delay(1000);
        }
    });
        LogError("task ended", 0.5f);
        IsTaskRunning = false;

    }
    private void ConnectToPartyServer()
    {
        if (PartyClient.IsConnected && !Settings.Server.ToggleLeaderServer)
        {
            PartyClient.SendMessage(MessageType.Order, "I'm already connected.");
            return;
        }
        if(PartyClient == null)
        {
            PartyClient = new PartyClient(this);
        }
        if(!PartyClient.IsConnected)
            PartyClient.Connect();
    }
    public override Job Tick()
    {

        LogMessage("FollowerPlugin Tick", 0.5f);
        SetLeader();
     
        if (Settings.Server.ToggleLeaderServer.Value)
        {
            if (PartyServer != null && PartyServer.IsRunning)
            {

                ServerTickForLeaderBroadcast();
            }
        }
        else
        {
            if (!IsTaskRunning && Settings.Party.ConnectClient)
                ConnectTask();

            if (!Settings.Server.ToggleLeaderServer && Settings.Party.Follow)
            {             
                FollowerBehavior();
            }
        }
        return null;
    }
    private void FollowerBehavior()
    {
        LogMessage("FollowerBehavior Tick", 0.5f);
        SetLeader();
        if(Leader == null)
        {
            
            LogMessage("Leader is not set, skipping follower behavior.");
            return;
        }

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
       
        if(Leader.Entity != null && (Leader.Entity.GetComponent<Actor>().Action == ActionFlags.None  && GameController.Player.Buffs.Any(b => b.Name.Equals("grace_period"))))
        {
            LogMessage(" is in grace period, skipping behaviors for now.");
            return;
        }


        // Cas 1 : On est en hideout, et le leader est en map -------------------> A CHECKER
        if (GameController.Area.CurrentArea.IsHideout && (Leader.CurrentArea != GameController.Area.CurrentArea.Name || Leader.Entity == null))
        {
            LogMessage($"Leader {Leader.Name} is in a different map.");

            if (Settings.Party.Follow)
            {
                var townPortals = GameController.EntityListWrapper.ValidEntitiesByType[EntityType.TownPortal]
                    .Where(x => x.IsValid && x.IsTargetable)
                    .OrderBy(e => e.DistancePlayer)
                    .ToList();

                var firstTP = townPortals.FirstOrDefault(tp => tp.RenderName == Leader.CurrentArea);

                if (firstTP != null)
                {
                    LogMessage($"Found town portal to follow: {firstTP.RenderName}", 0.5f);

                    if(Settings.Party.UseInputManager)
                    {
                        this.TryDoAction(() =>
                        {
                            var castWithTarget = GameController.PluginBridge
                                .GetMethod<Action<Entity, uint>>("MagicInput2.CastSkillWithTarget");
                            castWithTarget(firstTP, 0x400);
                        });
                    }
                    else
                    {
                        var screenPos = GameController.IngameState.Data.GetWorldScreenPosition(firstTP.BoundsCenterPosNum);
                        if (screenPos != Vector2.Zero && GameController.Window.GetWindowRectangle().Contains(screenPos))
                        {
                            Graphics.DrawBox(new SharpDX.RectangleF(screenPos.X - 25, screenPos.Y - 25, 50, 50), SharpDX.Color.Red);
                            Input.SetCursorPos(screenPos);
                            Input.Click(MouseButtons.Left);
                            Thread.Sleep(100);
                            return;
                        }
                    }
                  
                }
                else
                {
                    if (Leader == null)
                        SetLeader();
                    var leaderTpElement = Leader.Element.Children?[3];
                    if (leaderTpElement?.IsActive == true)
                    {
                        Graphics.DrawFrame(leaderTpElement.GetClientRect(), SharpDX.Color.Red, 2);
                        Input.SetCursorPos(leaderTpElement.GetClientRect().Center.ToVector2Num());
                        Input.Click(MouseButtons.Left);
                        Input.KeyDown(Keys.Enter);
                        Input.KeyUp(Keys.Enter);
                        Thread.Sleep(1000);
                        Leader.LastTargetedPortalOrTransition = null;
                        return;
                    }
                }
            }
        }
        //cas 5 : Leader n'est pas du tout sur la même map
        if (Leader != null && (Leader.Entity == null || Leader.CurrentArea != GameController.Area.CurrentArea.Name) && GameController.Area.CurrentArea.IsHideout == false)
        {
            var leaderTpElement = Leader.Element.Children?[3];

            if (false)//todo find it or ask instant sc to check it jajaajajaaj
            {
                this.TryDoAction(() =>
                {
                    var castWithTarget = GameController.PluginBridge
                        .GetMethod<Action<Element, Vector2i>>("MagicInput2.UiClick");
                    castWithTarget(leaderTpElement, leaderTpElement.GetClientRect().Center.ToVector2Num().RoundToVector2I());
                });
            }
            else
            { 
            if (leaderTpElement?.IsActive == true)
            {
                Graphics.DrawFrame(leaderTpElement.GetClientRect(), SharpDX.Color.Red, 2);
                Input.SetCursorPos(leaderTpElement.GetClientRect().Center.ToVector2Num());
                Input.Click(MouseButtons.Left);
                Input.KeyDown(Keys.Enter);
                Input.KeyUp(Keys.Enter);
                Thread.Sleep(1000);
                Leader.LastTargetedPortalOrTransition = null;
                return;
            }
            }
        }
        // Cas 2 : Leader est sur la même map et utilise une transition ou un portail
        if (Leader != null && Leader.IsLeaderOnSameMap() && Leader.Entity != null && Leader.Entity.TryGetComponent<Actor>(out Actor leaderActor))
        {
            var t = leaderActor.CurrentAction?.Target;
            if (t != null && (t.Type == EntityType.AreaTransition || t.Type == EntityType.Portal || t.Type == EntityType.TownPortal))
            {
                Leader.LastTargetedPortalOrTransition = t;
            }
        }

        // Cas 3 : Le leader vient de prendre un portail et on le suit
        if (Leader != null && Leader.Entity != null && Leader.LastTargetedPortalOrTransition != null &&
            Leader.CurrentArea == GameController.Area.CurrentArea.Name)
        {
            Entity MyTarget = null;
            int maxtattempts = 50;
            var portal = Leader.LastTargetedPortalOrTransition;

            do
            {
                var portalPosition = portal.BoundsCenterPosNum;
                var screenPos = GameController.IngameState.Data.GetWorldScreenPosition(portalPosition);

                if (Settings.Party.UseInputManager)
                {
                    this.TryDoAction(() =>
                    {
                        var castWithTarget = GameController.PluginBridge
                            .GetMethod<Action<Entity, uint>>("MagicInput2.CastSkillWithTarget");
                        castWithTarget(portal, 0x400);
                    });
                    Thread.Sleep(100);

                    break;
                }


                else
                {


                    // Check visible + click
                    if (!Settings.Party.UseInputManager && screenPos != Vector2.Zero && GameController.Window.GetWindowRectangle().Contains(screenPos) && GameController.IngameState.UIHover != null && GameController.IngameState.UIHover.Entity == portal)
                    {
                        Graphics.DrawBox(new SharpDX.RectangleF(screenPos.X - 25, screenPos.Y - 25, 50, 50), SharpDX.Color.Red);
                        Input.SetCursorPos(screenPos);
                        Input.Click(MouseButtons.Left);
                        Thread.Sleep(800); // plus réaliste que 20ms
                    }
                    else
                    {
                        LogError($"Portal not visible on screen: {portal.RenderName}, attempts left: {maxtattempts}", 100);
                    }

                    MyTarget = GameController.Player.GetComponent<Actor>().CurrentAction?.Target;

                    // Condition de sortie plus fiable
                    bool isSuccess = (this.GetBuffs().Any(b => b.Name.Equals("grace_period")) || GameController.IsLoading);
                    if (isSuccess)
                    {
                        LogMessage($"Successfully followed portal: {portal.RenderName}", 100);
                        Leader.LastTargetedPortalOrTransition = null;
                        Thread.Sleep(800); // attendre un peu pour laisser le temps de charger
                        return;
                    }

                    LogError($"Attempt failed. Attempts left: {maxtattempts}");
                    maxtattempts--;
                }

            } while (maxtattempts > 0);

            // Si on arrive ici, échec
            Leader.LastTargetedPortalOrTransition = null;
            LogError("Failed to follow portal after all attempts");
            return;
        }


        // Cas 4 : fallback si rien d’autre ne s’est passé, gérer comportement normal
        ManageLeaderOnSameMap();


    }
    private void ManageLeaderOnSameMap()
    {
        var leaderEntity = Leader.Entity;
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
                var crySkills = GameController.Player.GetComponent<Actor>().ActorSkills
                    .Where(x => x.IsCry && x.IsOnSkillBar && x.IsOnCooldown == false)
                    .ToList();
                foreach (var crySkill in crySkills)
                {
                    if (this.GetBuffs().Any(b => b.Name.Contains(crySkill.InternalName)))
                    {
                        LogError($"Cry Skill {crySkill.InternalName} is already active, skipping.");
                        continue;
                    }
                    else if(GameController.Player.GetComponent<Life>().CurMana < crySkill.Cost){
                        LogError($"Not enough mana to use Cry Skill: {crySkill.InternalName}, skipping.");
                        continue;
                    }
                    this.TryDoAction(() =>
                    {
                        LogError($"Using Cry Skill: {crySkill.Name} {crySkill.SkillSlotIndex}");
                        var scs = Shortcuts.Skip(7).Take(13).ToList()[crySkill.SkillSlotIndex];

                        scs.PressShortCut(1);
                    });
                }
            }
            if (leaderEntity.DistancePlayer > Settings.Party.LeaderMaxDistance.Value)
            {
                ReleaseKeys();
            }

            else if (leaderEntity.DistancePlayer > Settings.Party.KeepLeaderInRange.Value && Settings.Party.Follow)
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
                        if (Settings.Party.UseInputManager)
                        {
                            this.TryDoAction(() =>
                            {
                                var castWithPos = GameController.PluginBridge
                                    .GetMethod<Action<Vector2i, uint>>("MagicInput2.CastSkillWithPosition");
                                castWithPos(lastNode, 0x400);
                            });
                        }
                        else
                        {
                            var screenPos = GameController.IngameState.Camera.WorldToScreen(
                                GameController.IngameState.Data.ToWorldWithTerrainHeight(lastNode));
                            this.TryDoAction(() =>
                            {
                                Input.SetCursorPos(screenPos);
                                if (!Input.IsKeyDown((Keys)moveSkill.Shortcut.MainKey))
                                {
                                    Input.KeyDown((Keys)moveSkill.Shortcut.MainKey);
                                }
                            });
                        }
                    }
                    else if (!playerisattacking && playerEntity.DistancePlayer <= Settings.Party.KeepLeaderInRange.Value)
                    {
                        if (Settings.Party.UseInputManager)
                        {
                            this.TryDoAction(() =>
                            {
                                var castWithTarget = GameController.PluginBridge
                                    .GetMethod<Action<Entity, uint>>("MagicInput2.CastSkillWithTarget");
                                castWithTarget(leaderEntity, 0x400);
                            });
                        }
                        else
                        {
                            var playerscreenpos = GameController.IngameState.Camera.WorldToScreen(leaderEntity.PosNum);

                            this.TryDoAction(() =>
                            {
                                Input.SetCursorPos(playerscreenpos);
                                if (!Input.IsKeyDown((Keys)moveSkill.Shortcut.MainKey))
                                {
                                    Input.KeyDown((Keys)moveSkill.Shortcut.MainKey);
                                }
                            });
                        }
                    }
                }
            }
            else if (leaderEntity.DistancePlayer <= Settings.Party.KeepLeaderInRange.Value)
            {
                var opt = GameController.IngameState.IngameUi.ItemsOnGroundLabelElement[0].Children.Where(c => c.ChildCount == 3).FirstOrDefault();
                if (opt != null && opt[2] != null && opt[2].IsVisible && GameController.Window.GetWindowRectangleTimeCache.Contains(opt.GetClientRect().Center))
                {
                    LogError($"Found item on ground: {opt}", 100);
                    var screenPos = opt[2].GetClientRect().Center.ToVector2Num();
                    Graphics.DrawBox(new SharpDX.RectangleF(screenPos.X - 25, screenPos.Y - 25, 50, 50), SharpDX.Color.Red);
                    Input.SetCursorPos(screenPos);
                    Thread.Sleep(20);
                    Input.Click(MouseButtons.Left);
                    Thread.Sleep(100);

                }
                else
                    LogError("No items on ground found.", 100);

                if (Input.IsKeyDown((Keys)MoveSkill.Shortcut.MainKey))
                {
                    Input.KeyUp((Keys)MoveSkill.Shortcut.MainKey);
                }
                ReleaseKeys();
            }
        }
    }
    private void ReleaseKeys()
    {
        if (MoveSkill != null && Input.IsKeyDown((Keys)MoveSkill.Shortcut.MainKey))
        {
            Input.KeyUp((Keys)MoveSkill.Shortcut.MainKey);
        }
        else if (AttackSkill != null && Input.IsKeyDown((Keys)AttackSkill.Shortcut.MainKey))
        {
            Input.KeyUp((Keys)AttackSkill.Shortcut.MainKey);
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

    public void SetLeader()
    {
        var pt = GameController.Party();
        if (pt == null)
            return;
        Settings.Party.LeaderSelect.SetListValues(pt[0].Children.Select(child => child[0].Text).ToList());
        var leaderElement = pt[0].Children.FirstOrDefault(child => child[0].Text == Settings.Party.LeaderSelect.Value);
        if (leaderElement == null)
            return;

        Leader = new Leader
        {
            Name = leaderElement[0].Text,
            Element = leaderElement,
            LastTargetedPortalOrTransition = null
        };
    }
    //private void SetLeaderSkillAndShortCuts()
    //{
    //    var sc3 = Shortcuts;
    //    var sc = sc3[0].ToString().Contains("MoveUp") ? sc3.Skip(9).Take(13).ToList() : sc3.Skip(7).Take(13).ToList();//sc2.Skip(5).Take(13).ToList();
    //    Leader.Skills.Clear();
    //    for (int i = 0; i < sc.Count; i++)
    //    {
    //        var skillBarSkill = GameController.IngameState.IngameUi.SkillBar.Skills[i];
    //        Leader.Skills.Add(new PlayerSkill
    //        {
    //            Shortcut = sc[i],
    //            Skill = skillBarSkill
    //        });
    //    }
    //}
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
    }
    #region DisposeClose
    public override void Dispose()
    {
        DisconnectWithMessage("Disposing FollowerPlugin.");
        base.Dispose();
    }
    public override void OnClose()
    {
        DisconnectWithMessage("Closing FollowerPlugin.");
        base.OnClose();
    }
    public override void OnPluginDestroyForHotReload()
    {
        DisconnectWithMessage("Destroying FollowerPlugin for hot reload.");
        base.OnPluginDestroyForHotReload();
    }
    public override void OnUnload()
    {
        DisconnectWithMessage("Unloading FollowerPlugin.");
        base.OnUnload();
    }
    private void DisconnectWithMessage(string message)
    {
        LogError(message, 5);
        PartyClient?.Disconnect();
        PartyClient = null;
        PartyServer?.Stop();
        PartyServer = null;
        IsTaskRunning = false;
    }
    #endregion
}