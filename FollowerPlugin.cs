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

        

        if (!GameController.IngameState.InGame || MenuWindow.IsOpened || !GameController.Window.IsForeground()){ LogMessage("not checked well"); return; }
        LogMessage("Checking for flagged panels...", 0.5f);
        if (!GameController.CloseWindowIfOpen())
        {
            LogMessage("No flagged panels found, continuing...", 0.5f);
            if (pt != null)
            {
                LogMessage($"Found {pt.ChildCount} party members.", 0.5f);
                if (Settings.PartySubMenu.PartyMembers.Value != null)
                {
                    pt ??= GameController.Party();

                    var leaderElement = pt[0].Children.FirstOrDefault(child => child[0].Text == Settings.PartySubMenu.PartyMembers.Value);
                    if (leaderElement != null )
                    {
                        LogMessage($"Leader found: {leaderElement[0].Text}", 0.5f);
                        Leader = new Leader
                        {
                            LeaderName = leaderElement[0].Text,
                            Element = leaderElement,
                            
                            LastTargetedPortalOrTransition = null,
                           
                        };
                                                
                        if (!Leader.IsLeaderOnSameMap() && GameController.Area.CurrentArea.IsHideout)
                        {

                            LogMessage($"Leader {Leader.LeaderName} need reach it in map?.", 0.5f);

                            if (Settings.PartySubMenu.Follow)
                            {

                                var townPortals = GameController.EntityListWrapper.ValidEntitiesByType[EntityType.TownPortal]
                                .Where(x => x.IsValid && x.IsTargetable).OrderBy(e => e.DistancePlayer).ToList();
                                LogMessage($"Found {townPortals.Count} valid town portals.", 0.5f);
                                var firtstValidAndTargetableTP = townPortals.FirstOrDefault(tp => tp.IsValid && tp.IsTargetable);
                                if (townPortals.Count > 0 && firtstValidAndTargetableTP != null && Leader.LeaderCurrentArea == firtstValidAndTargetableTP.RenderName)
                                {
                                    var portalPosition = firtstValidAndTargetableTP.PosNum;
                                    var screenPos = GameController.IngameState.Data.GetWorldScreenPosition(portalPosition);
                                    if (screenPos != Vector2.Zero && GameController.Window.GetWindowRectangle().Contains(screenPos))
                                    {
                                        var rect = new SharpDX.RectangleF(
                                            (int)(screenPos.X - 25),
                                            (int)(screenPos.Y - 25),
                                            50,
                                            50
                                        );
                                        Graphics.DrawBox(rect, SharpDX.Color.Red);
                                        Input.SetCursorPos(screenPos);
                                        Input.Click(MouseButtons.Left);
                                        Thread.Sleep(1000);
                                        return;
                                    }
                                }
                                else
                                {
                                    // Si pas de portail, on clique sur le leader
                                    var leaderTpElement = Leader.Element.Children?[3];
                                    if (leaderTpElement != null && leaderTpElement.IsActive)
                                    {
                                        Graphics.DrawFrame(leaderTpElement.GetClientRect(), SharpDX.Color.Red, 2);
                                        Input.SetCursorPos(leaderTpElement.GetClientRect().Center.ToVector2Num());
                                        Input.Click(MouseButtons.Left);
                                        Input.KeyDown(Keys.Enter);
                                        Input.KeyUp(Keys.Enter);
                                        Thread.Sleep(1000);
                                    }
                                }
                            }
                        }

                        if (Leader.IsLeaderOnSameMap() && Leader.Entity.TryGetComponent<Actor>(out Actor leaderActor))
                        {
                            var t = leaderActor.CurrentAction?.Target;
                            if (t != null && (t.Type == EntityType.AreaTransition || t.Type == EntityType.Portal || t.Type == EntityType.TownPortal))
                            {
                                Leader.LastTargetedPortalOrTransition = t;
                            }
                        }

                        else if (Leader.LastTargetedPortalOrTransition != null && Leader.IsLeaderOnSameMap())
                        {
                            Entity MyTarget = null;

                            int maxtattempts = 10;
                            do
                            {


                                var portalPosition = Leader.LastTargetedPortalOrTransition.PosNum;

                                var screenPos = GameController.IngameState.Data.GetWorldScreenPosition(portalPosition);

                                if (screenPos != Vector2.Zero && GameController.Window.GetWindowRectangle().Contains(screenPos))
                                {
                                 
                                    var rect = new SharpDX.RectangleF(
                                        (int)(screenPos.X - 25),
                                        (int)(screenPos.Y - 25),
                                        50,
                                        50
                                    );
                                    Graphics.DrawBox(rect, SharpDX.Color.Red);

                                    Input.SetCursorPos(screenPos);

                                    Input.Click(MouseButtons.Left);
                                    MyTarget = GameController.Player.GetComponent<Actor>().CurrentAction?.Target;
                                    maxtattempts--;
                                    Thread.Sleep(100);

                                }
                            } while ((MyTarget == null || MyTarget != Leader.LastTargetedPortalOrTransition) && maxtattempts > 0);
                            return; // Sort de la méthode après avoir cliqué sur le portail/transition
                        }
                        // Sinon si le leader n'est pas sur la même map

                        else
                        {
                            ManageLeaderOnSameMap();
                        }

                    }
                }
            }
        }
        else
        {
            LogMessage("Flagged panels found, skipping follower behavior.", 0.5f);
        }
    }
    private DateTime lastActionTime = DateTime.MinValue;
    private const int ActionCooldownMS = 50;

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
                                    .GetMethod<Action<Vector2i, uint>>("MagicInput.CastSkillWithPosition");
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
                                    .GetMethod<Action<Entity, uint>>("MagicInput.CastSkillWithTarget");
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