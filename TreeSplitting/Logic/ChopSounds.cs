using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace TreeSplitting.Logic;

/// <summary>
/// Sounds for the axe, all in one spot so they're easy to swap out. Each one is a few of the game's own wood sounds
/// played together with their own pitch and volume, no audio files shipped. A split, a bite and a heavy hit should
/// be easy to tell apart without looking.
/// </summary>
public static class ChopSounds
{
    public record Layer(AssetLocation Location, float Pitch, float Volume);

    private static readonly AssetLocation Chop1 = new("game:sounds/block/chop1");
    private static readonly AssetLocation Chop2 = new("game:sounds/block/chop2");
    private static readonly AssetLocation Chop3 = new("game:sounds/block/chop3");
    private static readonly AssetLocation StickBreak = new("game:sounds/block/stickbreak");
    private static readonly AssetLocation WoodTool = new("game:sounds/block/wood-tool");
    private static readonly AssetLocation StickHit = new("game:sounds/block/stickhit");
    private static readonly AssetLocation Planks = new("game:sounds/block/planks");

    // Clean chop with a crack when a plane comes off
    public static readonly Layer[] Split =
    [
        new(Chop3, 1.0f, 1.0f),
        new(StickBreak, 0.6f, 0.35f)
    ];

    // Lower duller chop when it only gets partway through
    public static readonly Layer[] Bite =
    [
        new(Chop2, 0.78f, 0.7f)
    ];

    // Solid chop with a crack for the heavy hit, not a deep thud
    public static readonly Layer[] Heavy =
    [
        new(Chop3, 0.92f, 1.0f),
        new(Chop1, 1.0f, 0.6f),
        new(StickBreak, 0.75f, 0.45f)
    ];

    // One of the vanilla saw strokes, picked at random
    private static readonly Layer[][] Saw =
    [
        [new(new AssetLocation("game:sounds/tool/groundcrafting/saw1"), 1.0f, 0.8f)],
        [new(new AssetLocation("game:sounds/tool/groundcrafting/saw2"), 1.0f, 0.8f)],
        [new(new AssetLocation("game:sounds/tool/groundcrafting/saw3"), 1.0f, 0.8f)],
        [new(new AssetLocation("game:sounds/tool/groundcrafting/saw4"), 1.0f, 0.8f)]
    ];

    // Small and soft, the knife whittles and the chisel taps
    public static readonly Layer[] Knife = [new(WoodTool, 1.5f, 0.6f)];
    public static readonly Layer[] Chisel = [new(StickHit, 1.6f, 0.7f)];

    // Recipe done: the planks sound with a crack on top
    public static readonly Layer[] Finish =
    [
        new(Planks, 1.0f, 1.0f),
        new(Chop3, 1.1f, 0.6f),
        new(StickBreak, 0.9f, 0.4f)
    ];

    // Every layer gets nudged up to this much either way so hits don't all sound the same
    public const float PitchJitter = 0.05f;

    public static Layer[]? For(EnumChopResult result) => result switch
    {
        EnumChopResult.Split => Split,
        EnumChopResult.Bite => Bite,
        EnumChopResult.Heavy => Heavy,
        _ => null
    };

    /// <summary>
    /// Plays all the layers on the same tick so they sound like one hit. The swinging player plays it right away on
    /// their side, so the server passes them as dualCallByPlayer to skip sending it to them again
    /// </summary>
    public static Layer[]? ForTool(EnumTool tool, IWorldAccessor world) => tool switch
    {
        EnumTool.Saw => Saw[world.Rand.Next(Saw.Length)],
        EnumTool.Knife => Knife,
        EnumTool.Chisel => Chisel,
        _ => null
    };

    public static void Play(IWorldAccessor world, BlockPos pos, EnumChopResult result, IPlayer? dualCallByPlayer)
    {
        Play(world, pos, For(result), dualCallByPlayer);
    }

    public static void Play(IWorldAccessor world, BlockPos pos, Layer[]? layers, IPlayer? dualCallByPlayer)
    {
        if (layers == null) return;

        double x = pos.X + 0.5, y = pos.Y + 1, z = pos.Z + 0.5;
        foreach (Layer layer in layers)
        {
            float jitter = 1f + ((float)world.Rand.NextDouble() * 2f - 1f) * PitchJitter;
            world.PlaySoundAt(layer.Location, x, y, z, dualCallByPlayer, layer.Pitch * jitter, 32, layer.Volume);
        }
    }
}
