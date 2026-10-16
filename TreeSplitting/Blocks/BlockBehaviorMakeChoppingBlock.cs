using System.Collections.Generic;
using TreeSplitting.Logic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace TreeSplitting.Blocks;

/// <summary>
/// Patched onto the vanilla logs. Sneak + hold right click on a standing placed log with an axe and a few swings
/// later it's a chopping block of the same wood. Sits on the block so it gets the click before the axe's ground storage does.
/// </summary>
public class BlockBehaviorMakeChoppingBlock : BlockBehavior
{
    private const int Swings = 4;
    private const int DurabilityCost = 10;

    // Last swing each player got to, so every swing only chops once
    private readonly Dictionary<string, int> lastSwing = new();

    public BlockBehaviorMakeChoppingBlock(Block block) : base(block)
    {
    }

    // Only player placed standing logs, trees are log-grown so they're left alone
    private bool IsStandingLog => block.Code.Path.StartsWith("log-placed-") && block.Code.Path.EndsWith("-ud");

    private bool Applies(IPlayer byPlayer) => IsStandingLog && byPlayer.Entity.Controls.ShiftKey &&
                                             byPlayer.InventoryManager.ActiveHotbarSlot.Itemstack?.Collectible?.Tool == EnumTool.Axe;

    private Block? ChoppingBlockFor(IWorldAccessor world) => world.GetBlock(new AssetLocation("treesplitting:choppingblock-" + block.Variant["wood"]));

    // The top half of the multiblock goes above it
    private static bool HasRoomAbove(IWorldAccessor world, BlockPos pos) => world.BlockAccessor.GetBlock(pos.UpCopy()).Replaceable >= 6000;

    private static float SwingInterval(IWorldAccessor world) => TreeSplittingModSystem.GetConfig(world.Api).SwingIntervalMs / 1000f;

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling)
    {
        if (!Applies(byPlayer) || ChoppingBlockFor(world) == null) return false;

        // Eat the click either way so the axe doesn't get put down on the log
        handling = EnumHandling.PreventDefault;

        if (!HasRoomAbove(world, blockSel.Position))
        {
            (world.Api as ICoreClientAPI)?.TriggerIngameError(this, "needsspace", Lang.Get("treesplitting:ingameerror-choppingblock-space"));
            return true;
        }

        lastSwing[byPlayer.PlayerUID] = -1;
        if (world.Side == EnumAppSide.Client) byPlayer.Entity.StartAnimation("axechop");
        return true;
    }

    public override bool OnBlockInteractStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling)
    {
        if (!Applies(byPlayer) || !HasRoomAbove(world, blockSel.Position) || !lastSwing.ContainsKey(byPlayer.PlayerUID)) return false;
        handling = EnumHandling.PreventDefault;

        int swing = (int)(secondsUsed / SwingInterval(world));
        if (swing >= Swings) return false;

        if (swing > lastSwing[byPlayer.PlayerUID])
        {
            lastSwing[byPlayer.PlayerUID] = swing;

            // Server plays it for everyone so it only plays once
            if (world.Side == EnumAppSide.Server)
            {
                BlockPos pos = blockSel.Position;
                world.PlaySoundAt(new AssetLocation("game:sounds/block/chop" + (swing % 3 + 1)), pos.X + 0.5, pos.Y + 1, pos.Z + 0.5, null, 0.9f + (float)world.Rand.NextDouble() * 0.2f);
                world.SpawnCubeParticles(pos.ToVec3d().Add(0.5, 1, 0.5), new ItemStack(block), 0.3f, 8, 0.5f);
            }
        }

        return true;
    }

    public override void OnBlockInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling)
    {
        if (world.Side == EnumAppSide.Client) byPlayer.Entity.StopAnimation("axechop");
        if (!lastSwing.Remove(byPlayer.PlayerUID)) return;
        handling = EnumHandling.PreventDefault;

        // Let go early and nothing happens
        if (world.Side != EnumAppSide.Server || secondsUsed < Swings * SwingInterval(world) - 0.05f) return;
        if (!Applies(byPlayer) || !HasRoomAbove(world, blockSel.Position) || ChoppingBlockFor(world) is not { } choppingBlock) return;

        BlockPos pos = blockSel.Position;
        world.BlockAccessor.SetBlock(choppingBlock.Id, pos);
        if (world.BlockAccessor.GetBlockEntity(pos) == null) world.BlockAccessor.SpawnBlockEntity(choppingBlock.EntityClass, pos);

        // SetBlock skips the placing hooks, the multiblock one puts the top half in
        foreach (BlockBehavior behavior in choppingBlock.BlockBehaviors)
        {
            EnumHandling h = EnumHandling.PassThrough;
            behavior.OnBlockPlaced(world, pos, ref h);
        }

        ItemSlot slot = byPlayer.InventoryManager.ActiveHotbarSlot;
        slot.Itemstack?.Collectible.DamageItem(world, byPlayer.Entity, slot, DurabilityCost);
        // Same payoff as finishing a recipe
        ChopSounds.Play(world, pos.DownCopy(), ChopSounds.Finish, null);
        world.SpawnCubeParticles(pos.ToVec3d().Add(0.5, 0.7, 0.5), new ItemStack(block), 0.4f, 40, 0.6f, null, new Vec3f(0, 2, 0));
    }

    public override bool OnBlockInteractCancel(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling)
    {
        if (world.Side == EnumAppSide.Client) byPlayer.Entity.StopAnimation("axechop");
        lastSwing.Remove(byPlayer.PlayerUID);
        return true;
    }

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer, ref EnumHandling handling)
    {
        // Only worth mentioning with an axe in hand
        if (!IsStandingLog || world.Api is not ICoreClientAPI capi ||
            forPlayer.InventoryManager.ActiveHotbarSlot.Itemstack?.Collectible?.Tool != EnumTool.Axe) return [];

        return
        [
            new WorldInteraction
            {
                ActionLangCode = "treesplitting:blockhelp-log-makechoppingblock",
                MouseButton = EnumMouseButton.Right,
                HotKeyCode = "shift",
                Itemstacks = ObjectCacheUtil.GetToolStacks(capi, EnumTool.Axe)
            }
        ];
    }
}
