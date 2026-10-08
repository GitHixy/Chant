using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.System.Input;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace Chant.Tracking;

/// <summary>
/// Tells whether you're moving or about to: your character's position changing, a movement or jump
/// key held, or the left stick pushed. Only reads input the game has already received.
/// </summary>
public static unsafe class MovementSense
{
    private static readonly InputId[] MoveInputs =
    [
        InputId.MOVE_FORE,
        InputId.MOVE_BACK,
        InputId.MOVE_LEFT,
        InputId.MOVE_RIGHT,
        InputId.MOVE_STRIFE_L,
        InputId.MOVE_STRIFE_R,
        InputId.MOVE_AND_STEER,
        InputId.JUMP,
    ];

    /// <summary>Position change per frame that counts as moving, in yalms.</summary>
    private const float StepThreshold = 0.015f;
    private const float StickThreshold = 0.3f;

    private static Vector3 lastPosition;
    private static bool hasPosition;

    public static bool KeyHeld { get; private set; }
    public static bool StickHeld { get; private set; }
    public static bool Displaced { get; private set; }

    public static bool Moving => KeyHeld || StickHeld || Displaced;

    public static void Update(Vector3 position)
    {
        Displaced = hasPosition && Vector3.DistanceSquared(position, lastPosition) > StepThreshold * StepThreshold;
        lastPosition = position;
        hasPosition = true;

        KeyHeld = false;
        StickHeld = false;

        var input = UIInputData.Instance();
        if (input is null) return;

        foreach (var id in MoveInputs)
        {
            if (input->IsInputIdDown(id))
            {
                KeyHeld = true;
                break;
            }
        }

        var pad = input->GamepadInputs;
        StickHeld = pad.LeftStickLeft > StickThreshold || pad.LeftStickRight > StickThreshold
                    || pad.LeftStickUp > StickThreshold || pad.LeftStickDown > StickThreshold;
    }

    public static void Reset()
    {
        hasPosition = false;
        KeyHeld = StickHeld = Displaced = false;
    }
}
