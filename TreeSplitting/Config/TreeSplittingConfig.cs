namespace TreeSplitting.Config;

/// <summary>
/// Lives in ModConfig/treesplitting.json. Everything except the swing speed is decided by the server.
/// </summary>
public class TreeSplittingConfig
{
    public const string FileName = "treesplitting.json";

    // Multiplies how much a finished recipe gives you, rounded, never less than 1
    public float OutputMultiplier { get; set; } = 1f;

    // Multiplies the durability every hit costs, 0 turns tool wear off
    public float ToolDurabilityMultiplier { get; set; } = 1f;

    // Durability per hit for each tool. The knife and chisel only take out one voxel so they wear faster
    public int AxeDurabilityCost { get; set; } = 1;
    public int SawDurabilityCost { get; set; } = 1;
    public int KnifeDurabilityCost { get; set; } = 2;
    public int ChiselDurabilityCost { get; set; } = 1;

    // If true, cutting away wood the recipe needs doesn't ruin the log, you just get the recipe once the rest is gone
    public bool ForgivingMode { get; set; } = false;

    // How long between swings when holding down attack, in milliseconds (client side)
    public int SwingIntervalMs { get; set; } = 450;

    // Normal axe hits it takes to get through a plane, 1 takes the whole plane every hit. Gets sent to players when they join
    public int HitsPerPlane { get; set; } = 2;

    // Heavy hits wear the axe this much more than a normal hit
    public float HeavyHitDurabilityMultiplier { get; set; } = 2f;
}
