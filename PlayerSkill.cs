using ExileCore.PoEMemory.Elements;
using Newtonsoft.Json;
using System.Numerics;
using System.Text.Json;

namespace FollowerServer;
public class PlayerSkill
{
    public GameOffsets.Shortcut Shortcut { get; set; }
    public SkillElement Skill { get; set; }
    public override string ToString() => $"{Shortcut} : {Skill.Skill.Name}";
}
public class LeaderInput
{
    public string RawInput { get; set; }
    public string LeaderName { get; set; }
    public Vector2 MouseCoords { get; set; }
    public bool Pressed { get; set; }
}

public enum MessageType
{
    Connect,
    Input,
    Order,
    Moving
}

public class Message
{
    public MessageType MessageType { get; set; }
    public string Content { get; set; }
    public LeaderInput Input { get; set; }

    public string Serialize()
    {
        return JsonConvert.SerializeObject(this);
    }

    public static Message DeserializeMessage(string json)
    {
        try
        {
            return JsonConvert.DeserializeObject<Message>(json);
        }
        catch
        {
            return null;
        }
    }
}
