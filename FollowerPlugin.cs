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
namespace FollowerServer;

public class FollowerPlugin : BaseSettingsPlugin<FollowerPluginSettings>
{
    public List<PlayerSkill> FollowerSkills = [];
    public Entity LastTargetedPortalOrTransition { get; set; } = null;
    public Leader Leader { get; private set; }
    public PlayerSkill MoveSkill => FollowerSkills.LastOrDefault(x => x.Skill.Skill.Id == 10505);
    public PlayerSkill AttackSkill => FollowerSkills.FirstOrDefault(x => x.Skill.Skill.IsAttack || x.Skill.Skill.IsSpell);
    DateTime lastMoveCheck = DateTime.Now;
    float lastMoveDelayMS = 20f; //ms
    public IList<Shortcut> shortcuts;

    public List<PlayerSkill> LeaderSkills { get; set; } = [];
    public PartyServer PartyServer { get; private set; }
    public PartyClient PartyClient { get; private set; }
    public DateTime LastLeaderInput { get; set; } = DateTime.Now;

    public override bool Initialise()
    {
        var mem = GameController.Memory;
        var sc = GameController.IngameState.ShortcutSettings.Shortcuts;

        if (sc == null || sc.Count <= 5)
        {
            var address = GameController.IngameState.ShortcutSettings.Address;
            //int maxTries = 10000;
            //int tries = 0;
            //IList<Shortcut> sc2 = new List<Shortcut>();
            //StdVector vec = new StdVector();
            //while ((sc2.Count <= 0 || sc2.Count > 1000) && tries < maxTries)
            //{
            //    vec = mem.Read<StdVector>(address + (792 + tries));
            //    sc2 = mem.ReadStdVector<Shortcut>(vec);
            //    tries++;
            //}
            //LogMessage((792 + tries - 1).ToString());

            var vec2 = mem.Read<StdVector>(address + 784);
            IList<Shortcut> sc3 = mem.ReadStdVector<Shortcut>(vec2);
            shortcuts = GameController.IngameState.ShortcutSettings.Shortcuts;// sc3;


        }


        else
            shortcuts = GameController.IngameState.ShortcutSettings.Shortcuts;// sc3;
        if (shortcuts == null || shortcuts.Count <= 5)
        {
            LogError("No shortcuts found. Please check your game settings.", 100);
            return false;
        }

        PartyServer = new PartyServer(this);
        PartyClient = new PartyClient(this);
        var buttonConnect = Settings.ServerSubMenu.ConnectClient;
        buttonConnect.OnPressed += () =>
        {
            ConnectToPartyServer();
        };

        Settings.ServerSubMenu.ToggleLeaderServer.OnValueChanged += (foe, ar) =>
        {
            ToggleLeaderServer();
        };
        if (Settings.ServerSubMenu.ToggleLeaderServer)
        {
            ToggleLeaderServer();
        }
        return true;
    }
    public override Job Tick()
    {
        LogMessage("FollowerPlugin Tick", 0.5f);
        var pt = GameController.Party();

        Settings.PartySubMenu.PartyMembers.SetListValues(pt[0].Children.Select(child => child[0].Text).ToList());


        if (Settings.ServerSubMenu.ToggleLeaderServer.Value)
        {
            if (PartyServer != null && PartyServer.IsRunning)
            {

                ServerTickForLeaderBroadcast();
            }
        }
        else
        {

            if (PartyClient != null && PartyClient.IsConnected)
            {
                FollowerBehavior();
            }
        }
        return null;
    }
    private void FollowerBehavior()
    {


        LogMessage("FollowerBehavior Tick", 0.5f);
        var pt = GameController.Party();
        Settings.PartySubMenu.PartyMembers.SetListValues(pt[0].Children.Select(child => child[0].Text).ToList());

        SetFollowerSkillsAndShortcuts();

        if (!GameController.IngameState.InGame || MenuWindow.IsOpened || !GameController.Window.IsForeground())
        {
            LogMessage("Game not in focus or menu opened, skipping.", 0.5f);
            return;
        }

        if (GameController.CloseWindowIfOpen())
        {
            LogMessage("Flagged panels found, skipping follower behavior.", 0.5f);
            return;
        }

        if (pt == null || Settings.PartySubMenu.PartyMembers.Value == null)
            return;

        var leaderElement = pt[0].Children.FirstOrDefault(child => child[0].Text == Settings.PartySubMenu.PartyMembers.Value);
        if (leaderElement == null)
            return;

        Leader = new Leader
        {
            LeaderName = leaderElement[0].Text,
            Element = leaderElement,
            LastTargetedPortalOrTransition = null
        };

        // Cas 1 : On est en hideout, et le leader est en map
        if (GameController.Area.CurrentArea.IsHideout && Leader.LeaderCurrentArea != GameController.Area.CurrentArea.Name)
        {
            LogMessage($"Leader {Leader.LeaderName} is in a different map.");

            if (Settings.PartySubMenu.Follow)
            {
                var townPortals = GameController.EntityListWrapper.ValidEntitiesByType[EntityType.TownPortal]
                    .Where(x => x.IsValid && x.IsTargetable)
                    .OrderBy(e => e.DistancePlayer)
                    .ToList();

                var firstTP = townPortals.FirstOrDefault(tp => tp.RenderName == Leader.LeaderCurrentArea);

                if (firstTP != null)
                {
                    LogMessage($"Found town portal to follow: {firstTP.RenderName}", 0.5f);
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
                else
                {
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
        if (Leader != null && Leader.Entity == null && Leader.LeaderCurrentArea != GameController.Area.CurrentArea.Name && GameController.Area.CurrentArea.IsHideout == false)
        {


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
        if (Leader != null && Leader.Entity != null && Leader.LastTargetedPortalOrTransition != null && Leader.LeaderCurrentArea == GameController.Area.CurrentArea.Name)
        {
            Entity MyTarget = null;
            int maxtattempts = 10;
            do
            {
                var portalPosition = Leader.LastTargetedPortalOrTransition.BoundsCenterPosNum;
                var screenPos = GameController.IngameState.Data.GetWorldScreenPosition(portalPosition);
                if (screenPos != Vector2.Zero && GameController.Window.GetWindowRectangle().Contains(screenPos))
                {
                    Graphics.DrawBox(new SharpDX.RectangleF(screenPos.X - 25, screenPos.Y - 25, 50, 50), SharpDX.Color.Red);
                    Input.SetCursorPos(screenPos);
                    Input.Click(MouseButtons.Left);
                    MyTarget = GameController.Player.GetComponent<Actor>().CurrentAction?.Target;
                    maxtattempts--;
                    Thread.Sleep(100);
                }
            } while ((MyTarget == null || MyTarget != Leader.LastTargetedPortalOrTransition) && maxtattempts > 0);
            return;
        }


        // Cas 4 : fallback si rien d’autre ne s’est passé, gérer comportement normal
        ManageLeaderOnSameMap();


    }

    private DateTime lastActionTime = DateTime.MinValue;
    private const int ActionCooldownMS = 2000;

    private bool TryDoAction(Action act)
    {
        if ((DateTime.Now - lastActionTime).TotalMilliseconds < ActionCooldownMS)
            return false;

        lastActionTime = DateTime.Now;
        act();
        return true;
    }

    private void ManageLeaderOnSameMap()
    {
        var leaderEntity = Leader.Entity;
        var playerEntity = GameController.Player;
        SetFollowerSkillsAndShortcuts();

        if (leaderEntity != null)
        {
            var leaderActor = leaderEntity.GetComponent<Actor>();
            if (Settings.PartySubMenu.UseSmartTPSkill && leaderActor.CurrentAction != null && leaderActor.Action == ActionFlags.UsingAbility && leaderActor.CurrentAction.Skill.GetStat(GameStat.SkillIsTravelSkill) > 0)
            {

                var destination = leaderActor.CurrentAction.Destination;
                var screenPos = GameController.IngameState.Camera.WorldToScreen(
                    GameController.IngameState.Data.ToWorldWithTerrainHeight(destination));

              

                TryDoAction(() =>
                {
                    Input.SetCursorPos(screenPos);
                    Thread.Sleep(10);
                    var skillOnBar = GameController.Player.GetComponent<Actor>().ActorSkills.FirstOrDefault(x => x.IsOnSkillBar && x.GetStat(GameStat.SkillIsTravelSkill) > 0);

                    if(skillOnBar != null)
                    {
                        var sc = this.shortcuts.Skip(7).Take(13).ToList()[skillOnBar.SkillSlotIndex];

                        sc.PressShortCut(10);
                        LogMessage($"Pressed skill {skillOnBar.Name} with shortcut {sc.MainKey} at position {screenPos}");
                        return;
                    }
                
                });


            }
            if (leaderEntity.DistancePlayer > Settings.PartySubMenu.LeaderMaxDistance.Value)
            {
                ReleaseKeys();
            }
            else if (leaderEntity.DistancePlayer > Settings.PartySubMenu.KeepLeaderInRange.Value && Settings.PartySubMenu.Follow)
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
                    var leaderaction = leaderEntity.GetComponent<Actor>().Action;

                    if (pf.PathingNodes.Count > 0)
                    {
                        var lastNode = pf.PathingNodes.Last();

                        if (Settings.PartySubMenu.UseInputManager)
                        {
                            TryDoAction(() =>
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

                            TryDoAction(() =>
                            {
                                Input.SetCursorPos(screenPos);
                                if (!Input.IsKeyDown((Keys)moveSkill.Shortcut.MainKey))
                                {
                                    Input.KeyDown((Keys)moveSkill.Shortcut.MainKey);
                                }
                            });
                        }
                    }
                    else if (!playerisattacking && playerEntity.DistancePlayer <= Settings.PartySubMenu.KeepLeaderInRange.Value)
                    {
                        if (Settings.PartySubMenu.UseInputManager)
                        {
                            TryDoAction(() =>
                            {
                                var castWithTarget = GameController.PluginBridge
                                    .GetMethod<Action<Entity, uint>>("MagicInput2.CastSkillWithTarget");
                                castWithTarget(leaderEntity, 0x400);
                            });
                        }
                        else
                        {
                            var playerscreenpos = GameController.IngameState.Camera.WorldToScreen(leaderEntity.PosNum);

                            TryDoAction(() =>
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
            else if (leaderEntity.DistancePlayer <= Settings.PartySubMenu.KeepLeaderInRange.Value)
            {
                var opt = GameController.IngameState.IngameUi.ItemsOnGroundLabelElement[0].Children.Where(c => c.ChildCount == 3).FirstOrDefault();
                if (opt != null && opt[2] != null && opt[2].IsVisible)
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
    private void SetFollowerSkillsAndShortcuts()
    {
        var sc = shortcuts.Skip(7).Take(13).ToList();
        FollowerSkills.Clear();

        for (int i = 0; i < sc.Count; i++)
        {
            FollowerSkills.Add(new PlayerSkill
            {
                Shortcut = sc[i],
                Skill = GameController.IngameState.IngameUi.SkillBar.Skills[i]
            });
        }
    }
    private void ConnectToPartyServer()
    {
        if (PartyClient.IsConnected && !Settings.ServerSubMenu.ToggleLeaderServer)
        {
            PartyClient.SendMessage(MessageType.Order, "I'm already connected.");
            return;
        }
        PartyClient.Connect();
    }
    private void ToggleLeaderServer()
    {
        if (Settings.ServerSubMenu.ToggleLeaderServer.Value)
        {
            if (Settings.ServerSubMenu.ToggleLeaderServer.Value && !PartyServer.IsRunning)
            {
                LogError("Starting server...");
                PartyServer.Start();
            }
            else
            {
                Settings.ServerSubMenu.ToggleLeaderServer.Value = false;
            }
        }
        else
        {
            PartyServer.Stop();
        }
    }
    private void SetLeaderSkillAndShortCuts()
    {
        var sc3 = shortcuts;
        var sc = sc3[0].ToString().Contains("MoveUp") ? sc3.Skip(9).Take(13).ToList() : sc3.Skip(7).Take(13).ToList();//sc2.Skip(5).Take(13).ToList();
        LeaderSkills.Clear();
        for (int i = 0; i < sc.Count; i++)
        {
            var skillBarSkill = GameController.IngameState.IngameUi.SkillBar.Skills[i];
            LeaderSkills.Add(new PlayerSkill
            {
                Shortcut = sc[i],
                Skill = skillBarSkill
            });
        }
    }
    private void ServerTickForLeaderBroadcast()
    {
        if (PartyServer == null)
        {
            LogMessage("PartyServer is null. Exiting Tick.");
            return;
        }

        if (!Settings.ServerSubMenu.ToggleLeaderServer.Value)
        {
            LogMessage("Not the leader or server host. Exiting Tick.");
            return;
        }

        SetLeaderSkillAndShortCuts();
        var actor = GameController.Player.GetComponent<Actor>();

        if (LeaderSkills == null)
        {
            LogMessage("LeaderSkills is null. Exiting Tick.");
            return;
        }
        var foeSkill = LeaderSkills.Where(x => x.Shortcut.IsShortCutPressed() /*&& LeaderSkills.IndexOf(x) > 0*/);
        if (foeSkill == null)
        {
            LogMessage("FoeSkill is null. Exiting Tick.");
            return;
        }
        else
        {
            if (DateTime.Now.Subtract(LastLeaderInput).TotalMilliseconds < Settings.ServerSubMenu.ServerTick)
            {
                return;
            }

            LastLeaderInput = DateTime.Now; // Move this here, before processing multiple skills
            var mouseCoords = Input.MousePositionNum;
            var currentSkill = GameController.Player.GetComponent<Actor>().CurrentAction?.Skill;
            var window = GameController.Window.GetWindowRectangleTimeCache;

           
            //if (GameController.Player.GetComponent<Actor>().Action == (ActionFlags)16386 ||
            //    (currentSkill != null && currentSkill.Name.Contains("BlinkPlayer")))
            //{
            //    LeaderInput rollInput = new()
            //    {
            //        LeaderName = GameController.Player.GetComponent<Player>().PlayerName,
            //        RawInput = "Roll",
            //        MouseCoords = new(mouseCoords.X / window.Width, mouseCoords.Y / window.Height),
            //    };
            //    PartyServer.BroadcastLeaderInput(rollInput);

            //    return;
            //}
            if (GameController.Area.CurrentArea.IsHideout)
            {
                return;
            }
            foreach (var foe in foeSkill)
            {


                LeaderInput leaderInput = new()
                {
                    LeaderName = GameController.Player.GetComponent<Player>().PlayerName,
                    RawInput = LeaderSkills.IndexOf(foe).ToString(),
                    MouseCoords = new(mouseCoords.X / window.Width, mouseCoords.Y / window.Height),
                };

                PartyServer.BroadcastLeaderInput(leaderInput);
            }

        }
    }
    public override void Render()
    {
        base.Render();
        if (Settings.ServerSubMenu.ToggleLeaderServer && PartyServer != null && PartyServer.IsRunning)
        {
            Graphics.DrawText($"Server is running on {PartyServer.ServerIP}", new Vector2(100, 100));
            var curAction = GameController.Player.GetComponent<Actor>().Action;
            var curString = "None";
            if (curAction == ActionFlags.UsingAbility)
            {
                curString = GameController.Player.GetComponent<Actor>().CurrentAction.Skill.Name;
            }

            Graphics.DrawText($"Using Ability {curString}", new Vector2(100, 120));

        }
    }
    public override void Dispose()
    {
        PartyClient?.Disconnect();
        PartyServer?.Stop();
        base.Dispose();
    }

}