using UnityEngine;

/// <summary>
/// The practice round for new accounts (and from Settings > TUTORIAL): one round on Village as a Terrorist, against a
/// training bot that stands in front of the spawn, does not shoot and cannot die. Steps on screen, each ticked off when
/// done: walk, look around, jump, crouch, buy a rifle, hit the bot three times, plant the bomb. The round clock and the
/// buy time do not run out meanwhile; SKIP ends it at any time (GameManager.StartTutorial / EndTutorial, GameUI draws it).
/// </summary>
public class Tutorial
{
    public enum Step { Walk, Look, Jump, Crouch, Buy, Shoot, Plant, Done }

    public const string MapId = "village";
    public const int Hits = 3;
    public const int Steps = (int)Step.Done;

    public Step Current { get; private set; }
    public int HitCount { get; private set; }
    public float DoneAt { get; private set; } = -1f;

    Combatant dummy;
    Vector3 walkFrom;
    bool walkStarted;
    float lastYaw, turned;
    int jumpsAtStart = -1;

    /// <summary>The training bot was hit (it heals at once).</summary>
    public void OnDummyHit()
    {
        HitCount++;
        Debug.Log($"[Tutorial] The training bot was hit ({HitCount})");
    }

    public void Update(GameManager gm)
    {
        var player = gm.Player;
        if (player == null || !player.Self.IsAlive) return;
        if (dummy == null && gm.State == MatchState.Freeze) PlaceDummy(gm, player);
        if (gm.State != MatchState.Live || Current == Step.Done) return;

        var self = player.Self;
        float yaw = player.transform.eulerAngles.y;
        switch (Current)
        {
            case Step.Walk:
                if (!walkStarted)
                {
                    walkFrom = player.transform.position;
                    walkStarted = true;
                }
                Vector3 moved = player.transform.position - walkFrom;
                moved.y = 0f;
                if (moved.magnitude > 4f) Next(yaw, player);
                break;
            case Step.Look:
                turned += Mathf.Abs(Mathf.DeltaAngle(lastYaw, yaw));
                lastYaw = yaw;
                if (turned > 120f) Next(yaw, player);
                break;
            case Step.Jump:
                if (player.Jumps > jumpsAtStart) Next(yaw, player);
                break;
            case Step.Crouch:
                if (self.Height < Combatant.StandHeight - 0.4f) Next(yaw, player);
                break;
            case Step.Buy:
                if (self.Primary != null) Next(yaw, player);
                break;
            case Step.Shoot:
                if (HitCount >= Hits) Next(yaw, player);
                break;
            case Step.Plant:
                if (gm.Bomb.State == BombState.Planted) Next(yaw, player);
                break;
        }
    }

    void Next(float yaw, PlayerController player)
    {
        Current++;
        lastYaw = yaw;
        jumpsAtStart = player.Jumps;
        SoundFX.Play(SoundFX.Buy, Vector3.zero, 0.6f, 1.2f, false);
        if (Current == Step.Done) DoneAt = Time.unscaledTime;
        Debug.Log("[Tutorial] Step done; now " + Current);
    }

    // The training bot: 9 m in front of the player's spawn, facing it, standing still for the whole round.
    void PlaceDummy(GameManager gm, PlayerController player)
    {
        foreach (var c in gm.Combatants)
            if (c.Team != player.Self.Team && !c.IsHuman) dummy = c;
        if (dummy == null) return;
        Vector3 ahead = player.transform.position + player.transform.forward * 9f;
        if (UnityEngine.AI.NavMesh.SamplePosition(ahead, out var hit, 6f, UnityEngine.AI.NavMesh.AllAreas)) ahead = hit.position;
        Vector3 back = player.transform.position - ahead;
        float yaw = Mathf.Atan2(back.x, back.z) * Mathf.Rad2Deg;
        var bot = dummy.GetComponent<BotController>();
        if (bot != null)
        {
            bot.Respawn(ahead, yaw);
            bot.enabled = false;   // no thinking: it neither moves nor shoots
        }
        dummy.Dummy = true;
        dummy.Health = 100;
        lastYaw = player.transform.eulerAngles.y;
        jumpsAtStart = player.Jumps;
    }

    /// <summary>What to do now, for the screen (an English text; GameUI translates it).</summary>
    public string Instruction(bool touch)
    {
        switch (Current)
        {
            case Step.Walk: return touch ? "Walk around with the stick on the left" : "Walk around with W A S D";
            case Step.Look: return touch ? "Look around: drag on the right side of the screen" : "Look around: move the mouse";
            case Step.Jump: return touch ? "Jump: tap JUMP" : "Jump: press Space";
            case Step.Crouch: return touch ? "Crouch: tap CROUCH" : "Crouch: hold Ctrl";
            case Step.Buy: return touch ? "Tap BUY and buy a rifle (the AK-47)" : "Open the shop with B and buy a rifle (the AK-47)";
            case Step.Shoot: return touch ? "Shoot the training bot in front of you: hit it 3 times (FIRE)"
                                          : "Shoot the training bot in front of you: hit it 3 times (left mouse button)";
            case Step.Plant: return touch ? "You carry the bomb: walk to a bombsite (A or B on the map in the corner) and tap PLANT there"
                                          : "You carry the bomb: walk to a bombsite (A or B on the map in the corner) and hold E there";
            default: return "Well done! You know the basics. Every kill and every match won earns Tokens.";
        }
    }
}
