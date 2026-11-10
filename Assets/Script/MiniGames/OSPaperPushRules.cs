using System;

public enum OSPaperPushMove { Defend, LightPush, HeavyPush }
public enum OSPaperPushPose { Idle, Defend, LightPush, HeavyPush, Windup, Panting, Straining }
public enum OSPaperPushActionState
{
    Ready, Pushing, Choosing, Guarding, GuardSucceeded, GuardBroken,
    WindingUp, Attacking, Stunned, KnockedDown, GettingUp, Panting, Out
}

public readonly struct OSPaperPushRound
{
    public readonly int Winner; // 1 player, -1 Character, 0 equal.
    public readonly float PushSteps;
    public readonly bool PlayerPanting;
    public readonly bool CharacterPanting;
    public OSPaperPushRound(int winner, float pushSteps, bool playerPanting, bool characterPanting)
    { Winner = winner; PushSteps = pushSteps; PlayerPanting = playerPanting; CharacterPanting = characterPanting; }
}

public static class OSPaperPushRules
{
    public const float OutSteps = 6f;
    public const float HopSeconds = .55f;
    public const float RecoveryPushPerHop = .6f;
    public const int RecoveryHops = 2;
    public const float PushIntervalSeconds = 3.2f;
    public const float Chapter02PushIntervalMultiplier = .75f;
    public const float Chapter02CounterPushMultiplier = 1.15f;
    public const float GuardSeconds = .5f;
    public const float HeavyWindupSeconds = 1f;
    public const float ReactionSeconds = .45f;
    public const float LightStartupSeconds = .05f;
    public const int Chapter02FirstMatchAssistAfterRound = 15;
    public const float Chapter02FirstMatchAssistChance = .6f;

    public static OSPaperPushRound Resolve(OSPaperPushMove player, OSPaperPushMove character, int chapter = 0)
    {
        if (!Enum.IsDefined(typeof(OSPaperPushMove), player) || !Enum.IsDefined(typeof(OSPaperPushMove), character))
            throw new ArgumentOutOfRangeException();
        if (player == character)
            return new OSPaperPushRound(0, 0f, player == OSPaperPushMove.HeavyPush, character == OSPaperPushMove.HeavyPush);
        bool win = Beats(player, character);
        OSPaperPushMove winningMove = win ? player : character;
        float steps = winningMove == OSPaperPushMove.Defend ? 1.5f : winningMove == OSPaperPushMove.LightPush ? 2f : 3.5f;
        if (chapter == 2) steps *= Chapter02CounterPushMultiplier;
        // Light interrupts the windup, before a heavy push has actually been released.
        return new OSPaperPushRound(win ? 1 : -1, steps,
            win && player == OSPaperPushMove.HeavyPush, !win && character == OSPaperPushMove.HeavyPush);
    }

    public static bool Beats(OSPaperPushMove a, OSPaperPushMove b) =>
        (a == OSPaperPushMove.Defend && b == OSPaperPushMove.LightPush)
        || (a == OSPaperPushMove.LightPush && b == OSPaperPushMove.HeavyPush)
        || (a == OSPaperPushMove.HeavyPush && b == OSPaperPushMove.Defend);

    public static OSPaperPushMove Counter(OSPaperPushMove move) => move == OSPaperPushMove.Defend
        ? OSPaperPushMove.HeavyPush : move == OSPaperPushMove.LightPush ? OSPaperPushMove.Defend : OSPaperPushMove.LightPush;

    public static OSPaperPushMove AssistChapter01(OSPaperPushMove player, OSPaperPushMove chosen,
        int chapter, int round, float playerX, float edge, float stepPixels)
    {
        if (chapter != 1) return chosen;
        var result = Resolve(player, chosen);
        float worstRetreat = result.Winner < 0 ? result.PushSteps * stepPixels
            : result.PlayerPanting && !result.CharacterPanting ? RecoveryHops * RecoveryPushPerHop * stepPixels : 0f;
        // Early rounds retain her habits. Periodic mistakes keep it easy; late rounds
        // prevent endless ties. Change her actual move before either move is revealed.
        if (round >= 7 || round % 3 == 0 || playerX - worstRetreat <= -edge + 12f)
            return Counter(Counter(player));
        return chosen;
    }

    public static OSPaperPushMove AssistChapter02FirstMatch(OSPaperPushMove player, OSPaperPushMove chosen,
        int round, float roll)
    {
        // Keep the first fifteen rounds intact, then mix random losing moves
        // into her usual echo. Resolve the lottery once, before saving/revealing.
        if (round > Chapter02FirstMatchAssistAfterRound && roll < Chapter02FirstMatchAssistChance)
            return Counter(Counter(player));
        return chosen;
    }

    public static OSPaperPushMove ChooseCharacter(int round, int lossStreak, int chapter, OSPaperPushMove previousPlayer)
    {
        // Called before the choices become interactive. It never receives this round's player move.
        if (chapter == 2) return previousPlayer;
        if (chapter > 2 && round > 1 && round % 3 != 0) return Counter(previousPlayer);
        if (lossStreak >= 2) return OSPaperPushMove.Defend;
        int habit = (round - 1) % 6;
        return habit == 2 || habit == 5 ? OSPaperPushMove.Defend
            : habit == 3 ? OSPaperPushMove.HeavyPush : OSPaperPushMove.LightPush;
    }
}
