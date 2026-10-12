using System;
using System.Collections.Generic;
using System.Linq;
using TreeSplitting.Blocks;
using TreeSplitting.Gui;
using TreeSplitting.Network;
using TreeSplitting.Recipes;
using TreeSplitting.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using TreeSplitting.Enums;

namespace TreeSplitting.BlockEntities;

public enum EnumWoodMaterial : byte
{
    Empty = 0,
    Heartwood = 1,
    Sapwood = 2,
    Bark = 3,
}

public class BEChoppingBlock : BlockEntity
{
    public byte[,,] Voxels = new byte[16, 16, 16];
    // What the recipe wants left over, null if no recipe is picked yet
    public bool[,,]? TargetVoxels => SelectedRecipe?.Voxels;

    public ItemStack? WorkItemStack;
    public AssetLocation? SelectedRecipeCode;
    public HewingRecipe? SelectedRecipe; // Runtime only

    // Index 0 is the stump, the rest are voxels. Top* are the same boxes moved down a block for the part above the stump
    public Cuboidf[] SelectionBoxes = [];
    public Cuboidf[] TopSelectionBoxes = [];
    public Cuboidf[] CollisionBoxes = [];
    public Cuboidf[] TopCollisionBoxes = [];
    // Which voxel each selection box is, same order as SelectionBoxes
    private Vec3i?[] SelectionVoxels = [];
    WoodWorkItemRenderer? renderer;
    GuiDialogRecipeSelector? dialog;

    // Which columns are inside the round log, and which of those are bark. Same for every log so only work it out once
    private static readonly bool[,] InsideLog = new bool[16, 16];
    private static readonly bool[,] IsBark = new bool[16, 16];

    static BEChoppingBlock()
    {
        for (int x = 0; x < 16; x++)
        for (int z = 0; z < 16; z++)
        {
            double dist = Math.Sqrt((x - 7.5) * (x - 7.5) + (z - 7.5) * (z - 7.5));
            InsideLog[x, z] = dist <= 7.5;
            IsBark[x, z] = dist > 6.0 && dist <= 7.5;
        }
    }


    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);

        if (SelectedRecipeCode != null) SelectedRecipe = FindRecipe(api, SelectedRecipeCode);

        if (api is ICoreClientAPI capi)
        {
            renderer = new WoodWorkItemRenderer(this, Pos, capi);
        }

        // Server needs these too, otherwise you fall through the log
        RegenSelectionBoxes();

        if (api.Side == EnumAppSide.Server) ConvertLegacyTopBlock();
    }

    public static readonly AssetLocation ProxyBlockCode = new("game:multiblock-monolithic-0-p1-0");

    /// <summary>
    /// Gets the chopping block from either the stump, the multiblock block above it or an old top block.
    /// </summary>
    public static BEChoppingBlock? AtPosition(IBlockAccessor blockAccessor, BlockPos pos)
    {
        Block block = blockAccessor.GetBlock(pos);
        BlockPos stumpPos = block switch
        {
            BlockChoppingBlockTop => pos.DownCopy(),
            IMultiblockOffset proxy => proxy.GetControlBlockPos(pos),
            _ => pos
        };
        return blockAccessor.GetBlockEntity(stumpPos) as BEChoppingBlock;
    }

    /// <summary>
    /// Old worlds still have our own choppingblocktop above the stump, so swap it for the vanilla multiblock one.
    /// </summary>
    private void ConvertLegacyTopBlock()
    {
        BlockPos upPos = Pos.UpCopy();
        if (Api.World.BlockAccessor.GetBlock(upPos) is not BlockChoppingBlockTop) return;

        Block? proxy = Api.World.GetBlock(ProxyBlockCode);
        if (proxy == null) return;

        // Can't do it while the chunk is loading, so wait a tick
        Api.World.RegisterCallback(_ =>
        {
            if (Api.World.BlockAccessor.GetBlock(upPos) is BlockChoppingBlockTop)
            {
                Api.World.BlockAccessor.SetBlock(proxy.Id, upPos);
            }
        }, 1);
    }


    private static HewingRecipe? FindRecipe(ICoreAPI api, AssetLocation code)
    {
        return TreeSplittingModSystem.GetRecipes(api).FirstOrDefault(x => x.Code.Equals(code));
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetItemstack("workItem", WorkItemStack);

        // byte[,,] is laid out x, y, z in memory, so we can just copy it straight into a flat array
        byte[] flatVoxels = new byte[16 * 16 * 16];
        Buffer.BlockCopy(Voxels, 0, flatVoxels, 0, flatVoxels.Length);
        tree.SetBytes("voxels", flatVoxels);

        if (SelectedRecipeCode != null) tree.SetString("recipeCode", SelectedRecipeCode.ToString());
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        WorkItemStack = tree.GetItemstack("workItem");

        if (WorkItemStack != null)
        {
            WorkItemStack.ResolveBlockOrItem(worldAccessForResolve);

            byte[] flatVoxels = tree.GetBytes("voxels");
            if (flatVoxels is { Length: 4096 })
            {
                Voxels = new byte[16, 16, 16];
                Buffer.BlockCopy(flatVoxels, 0, Voxels, 0, flatVoxels.Length);
            }

            string code = tree.GetString("recipeCode");
            if (code != null)
            {
                SelectedRecipeCode = new AssetLocation(code);

                // Api is null when loading from disk, Initialize() picks the recipe up then
                if (Api != null) SelectedRecipe = FindRecipe(Api, SelectedRecipeCode);
            }
            else
            {
                SelectedRecipeCode = null;
                SelectedRecipe = null;
            }
        }
        else
        {
            Voxels = new byte[16, 16, 16];
            SelectedRecipeCode = null;
            SelectedRecipe = null;
        }

        // We draw the log ourselves so there's no need to redraw the chunk here
        RegenSelectionBoxes();
    }

    public void SetSelectedRecipe(AssetLocation code)
    {
        SelectedRecipeCode = code;
        SelectedRecipe = FindRecipe(Api, SelectedRecipeCode);
        if (SelectedRecipe != null)
        {
            RegenSelectionBoxes();
            MarkDirty();
        }
    }

    public bool OnInteract(IPlayer byPlayer, BlockSelection blockSel)
    {
        if (WorkItemStack == null && byPlayer.Entity.Controls.ShiftKey) return TryPutLog(byPlayer);
        if (WorkItemStack != null && byPlayer.Entity.Controls.ShiftKey) return TryTakeLog(byPlayer);

        if (WorkItemStack != null && SelectedRecipe == null)
        {
            OpenRecipeDialog(byPlayer);
            return true;
        }

        return false;
    }

    private void OpenRecipeDialog(IPlayer player)
    {
        var capi = Api as ICoreClientAPI;
        if (capi == null || WorkItemStack == null) return;

        var matching = new List<HewingRecipe>();
        var stacks = new List<ItemStack>();

        foreach (var recipe in TreeSplittingModSystem.GetRecipes(Api))
        {
            if (recipe.Matches(Api.World, WorkItemStack))
            {
                ItemStack outStack = recipe.GenerateOutput(WorkItemStack, Api.World);

                if (outStack != null)
                {
                    matching.Add(recipe);
                    stacks.Add(outStack);
                }
            }
        }

        if (matching.Count == 0) return;

        if (dialog?.IsOpened() == true) dialog.TryClose();

        dialog = new GuiDialogRecipeSelector(capi, stacks,
            (selectedIndex) =>
            {
                if (selectedIndex >= 0 && selectedIndex < matching.Count)
                {
                    var recipe = matching[selectedIndex];
                    capi.Network.GetChannel("treesplitting").SendPacket(new RecipeSelectPacket()
                    {
                        Pos = Pos,
                        RecipeCode = recipe.Code.ToString()
                    });
                }
            }
        );
        dialog.TryOpen();
    }

    // TODO: Make tools use durability
    public void OnUseOver(IPlayer byPlayer, Vec3i voxelPos, BlockFacing facing, int toolMode
    )
    {
        if (WorkItemStack == null) return;

        // Go off the tool type, checking the code meant pickaxes counted as axes
        EnumTool? tool = byPlayer.InventoryManager.ActiveHotbarSlot.Itemstack?.Collectible?.Tool;
        if (tool is not (EnumTool.Axe or EnumTool.Saw or EnumTool.Chisel or EnumTool.Knife)) return;

        if (voxelPos.X < 0 || voxelPos.X >= 16 ||
            voxelPos.Y < 0 || voxelPos.Y >= 16 ||
            voxelPos.Z < 0 || voxelPos.Z >= 16) return;

        if (Voxels[voxelPos.X, voxelPos.Y, voxelPos.Z] == (byte)EnumWoodMaterial.Empty) return;

        // Grab these before the cut, finishing the recipe clears the work item
        ItemStack chipStack = WorkItemStack;
        int woodBefore = CountWood();

        switch (tool)
        {
            case EnumTool.Axe:
                OnChop(voxelPos, facing, byPlayer, toolMode);
                break;
            case EnumTool.Saw:
                OnSaw(voxelPos, facing, byPlayer, toolMode);
                break;
            // Once durability handling is in place the knife should take more durability damage than the chisel
            case EnumTool.Chisel:
            case EnumTool.Knife:
                OnChiselOrKnife(voxelPos, facing, byPlayer);
                break;
        }

        SpawnWoodChips(chipStack, voxelPos, facing, woodBefore - CountWood());

        CheckIfFinished(byPlayer);
        RegenSelectionBoxes();
        MarkDirty();
    }

    private int CountWood()
    {
        int count = 0;
        foreach (byte voxel in Voxels)
        {
            if (voxel != (byte)EnumWoodMaterial.Empty) count++;
        }
        return count;
    }

    // Chips fly off the side you hit, more wood cut means more chips. Runs on the server so everyone sees them
    private void SpawnWoodChips(ItemStack stack, Vec3i voxelPos, BlockFacing facing, int removed)
    {
        if (removed <= 0) return;

        Vec3d hitPos = new Vec3d(
            Pos.X + (voxelPos.X + 0.5) / 16.0,
            Pos.Y + (voxelPos.Y + 10.5) / 16.0,
            Pos.Z + (voxelPos.Z + 0.5) / 16.0
        );

        Vec3f velocity = new Vec3f(facing.Normalf.X * 2f, 1.5f + facing.Normalf.Y, facing.Normalf.Z * 2f);
        int quantity = GameMath.Clamp(removed / 3, 3, 30);

        Api.World.SpawnCubeParticles(hitPos, stack, 0.2f, quantity, 0.5f, null, velocity);
    }

    private void RemoveVoxels(Vec3i start, Vec3i end, Vec3i step)
    {
        byte empty = (byte)EnumWoodMaterial.Empty;
        for (int x = start.X; step.X > 0 ? x < end.X : x > end.X; x += step.X)
        {
            for (int y = start.Y; step.Y > 0 ? y < end.Y : y > end.Y; y += step.Y)
            {
                for (int z = start.Z; step.Z > 0 ? z < end.Z : z > end.Z; z += step.Z)
                {
                    if (x is >= 0 and < 16 && y is >= 0 and < 16 && z is >= 0 and < 16)
                    {
                        Voxels[x, y, z] = empty;
                    }
                }
            }
        }
    }

    private void OnSaw(Vec3i voxelPos, BlockFacing facing, IPlayer byPlayer, int toolMode)
    {
        EnumSawToolModes tool = (EnumSawToolModes)toolMode;

        if (tool == EnumSawToolModes.SawDown) HandleSawDown(voxelPos, facing, byPlayer);
        if (facing == BlockFacing.UP) return;
        if (tool == EnumSawToolModes.SawSideways) HandleSawSideways(voxelPos, byPlayer);
    }

    private void HandleSawSideways(Vec3i voxelPos, IPlayer byPlayer)
    {
        double dx = byPlayer.Entity.Pos.X - (Pos.X + 0.5);
        double dz = byPlayer.Entity.Pos.Z - (Pos.Z + 0.5);

        if (Math.Abs(dx) > Math.Abs(dz))
        {
            // Player is primarily East/West, so cut a slice along the Z-axis.
            RemoveVoxels(new Vec3i(voxelPos.X, voxelPos.Y, 0), new Vec3i(voxelPos.X + 1, voxelPos.Y + 1, 16), new Vec3i(1, 1, 1));
        }
        else
        {
            // Player is primarily North/South, so cut a slice along the X-axis.
            RemoveVoxels(new Vec3i(0, voxelPos.Y, voxelPos.Z), new Vec3i(16, voxelPos.Y + 1, voxelPos.Z + 1), new Vec3i(1, 1, 1));
        }
    }

    private void HandleSawDown(Vec3i voxelPos, BlockFacing facing, IPlayer byPlayer)
    {
        double dx = byPlayer.Entity.Pos.X - (Pos.X + 0.5);
        double dz = byPlayer.Entity.Pos.Z - (Pos.Z + 0.5);

        if (Math.Abs(dx) > Math.Abs(dz))
        {
            // Player E/W, cut along X axis
            RemoveVoxels(new Vec3i(0, voxelPos.Y, voxelPos.Z), new Vec3i(16, voxelPos.Y + 1, voxelPos.Z + 1), new Vec3i(1, 1, 1));
        }
        else
        {
            // Player N/S, cut along Z axis
            RemoveVoxels(new Vec3i(voxelPos.X, voxelPos.Y, 0), new Vec3i(voxelPos.X + 1, voxelPos.Y + 1, 16), new Vec3i(1, 1, 1));
        }
    }

    private void OnChiselOrKnife(Vec3i voxelPos, BlockFacing facing, IPlayer byPlayer)
    {
        HandleOneVoxel(voxelPos);
    }

    private bool TryPutLog(IPlayer byPlayer)
    {
        ItemStack heldStack = byPlayer.InventoryManager.ActiveHotbarSlot.Itemstack;
        if (heldStack == null) return false;

        // Do we have ANY matches?
        if (!TreeSplittingModSystem.GetRecipes(Api).Any(r => r.Matches(Api.World, heldStack))) return false;

        WorkItemStack = heldStack.Clone();
        WorkItemStack.StackSize = 1;
        byPlayer.InventoryManager.ActiveHotbarSlot.TakeOut(1);

        SelectedRecipe = null;
        SelectedRecipeCode = null;

        GenerateLogVoxels();

        RegenSelectionBoxes();
        MarkDirty();

        OpenRecipeDialog(byPlayer);
        return true;
    }


    private bool TryTakeLog(IPlayer byPlayer)
    {
        if (WorkItemStack == null) return false;
        ItemStack stackToReturn = WorkItemStack.Clone();
        stackToReturn.StackSize = 1;
        if (!byPlayer.InventoryManager.TryGiveItemstack(stackToReturn))
            Api.World.SpawnItemEntity(stackToReturn, Pos.ToVec3d().Add(0.5, 1.0, 0.5));
        ResetBlock();

        // Close GUI if open
        if (Api.Side == EnumAppSide.Client) dialog?.TryClose();

        return true;
    }

    private void GenerateLogVoxels()
    {
        for (int x = 0; x < 16; x++)
        {
            for (int z = 0; z < 16; z++)
            {
                byte mat = (byte)EnumWoodMaterial.Empty;
                if (InsideLog[x, z]) mat = IsBark[x, z] ? (byte)EnumWoodMaterial.Bark : (byte)EnumWoodMaterial.Heartwood;
                for (int y = 0; y < 16; y++) Voxels[x, y, z] = mat;
            }
        }
    }

    private void OnChop(Vec3i pos, BlockFacing facing, IPlayer player, int toolMode)
    {
        var tool = (EnumAxeToolModes)toolMode;

        switch (tool)
        {
            case EnumAxeToolModes.ChopVertical:
                HandleChopDown(pos, facing, player);
                break;
            case EnumAxeToolModes.ChopHorizontal:
                HandleChopSideways(pos);
                break;
        }
    }

    private void HandleChopSideways(Vec3i pos)
    {
        RemoveVoxels(new Vec3i(0, pos.Y, 0), new Vec3i(16, pos.Y + 1, 16), new Vec3i(1, 1, 1));
    }

    private void HandleChopDown(Vec3i pos, BlockFacing facing, IPlayer player)
    {
        double dx = player.Entity.Pos.X - (Pos.X + 0.5);
        double dz = player.Entity.Pos.Z - (Pos.Z + 0.5);

        
        if (Math.Abs(dx) > Math.Abs(dz))
        {
            // Player E/W, cut along X axis
            RemoveVoxels(new Vec3i(0, 15, pos.Z), new Vec3i(16, -1, pos.Z + 1), new Vec3i(1, -1, 1));
        }
        else
        {
            // Player N/S, cut along Z axis
            RemoveVoxels(new Vec3i(pos.X, 15, 0), new Vec3i(pos.X + 1, -1, 16), new Vec3i(1, -1, 1));
        }
    }

    private void HandleOneVoxel(Vec3i pos)
    {
        Voxels[pos.X, pos.Y, pos.Z] = (byte)EnumWoodMaterial.Empty;
    }

    private void CheckIfFinished(IPlayer player)
    {
        // TODO: Either make this check if there are any voxels left and make the work item null, or do it somewhere else
        bool[,,]? target = TargetVoxels;
        if (SelectedRecipe == null || target == null) return;

        bool ruined = false;
        bool finished = true;

        for (int x = 0; x < 16; x++)
        for (int z = 0; z < 16; z++)
        {
            bool insideLog = InsideLog[x, z];
            for (int y = 0; y < 16; y++)
            {
                bool recipeNeedsWood = target[x, y, z];
                bool hasWood = Voxels[x, y, z] != (byte)EnumWoodMaterial.Empty;

                // Only ruin it if the missing wood was inside the log, the recipe can want wood in the corners
                if (recipeNeedsWood && !hasWood && insideLog) ruined = true;

                // Still wood left that has to go
                if (!recipeNeedsWood && hasWood) finished = false;
            }
        }

        if (ruined)
        {
            // Drop Firewood? Or just clear?
            Api.World.SpawnItemEntity(new ItemStack(Api.World.GetItem(new AssetLocation("game:firewood")), 2),
                Pos.ToVec3d().Add(0.5, 1, 0.5));
            Api.World.PlaySoundAt(new AssetLocation("game:sounds/block/chop2"), Pos.X, Pos.Y, Pos.Z, player);
            ResetBlock();
            return;
        }

        if (finished)
        {
            // Success!
            if (WorkItemStack != null)
            {
                ItemStack result = SelectedRecipe.GenerateOutput(WorkItemStack, Api.World);
                //if (!player.InventoryManager.TryGiveItemstack(result))
                Api.World.SpawnItemEntity(result, Pos.ToVec3d().Add(0.5, 1, 0.5));

                Api.World.PlaySoundAt(new AssetLocation("game:sounds/block/planks"), Pos.X, Pos.Y, Pos.Z, player);
            }

            ResetBlock();
        }
    }

    private void ResetBlock()
    {
        WorkItemStack = null;
        SelectedRecipe = null;
        SelectedRecipeCode = null;
        Voxels = new byte[16, 16, 16];
        RegenSelectionBoxes();
        MarkDirty();
    }

    private void RegenSelectionBoxes()
    {
        // Update Renderer
        if (Api?.Side == EnumAppSide.Client && renderer != null)
        {
            renderer.RegenMesh(WorkItemStack, Voxels, TargetVoxels);
        }

        // The log goes from the stump up into the block above it (layers 0-5 in the stump, 6-15 above).
        // Both blocks get all the selection boxes so the index -> voxel lookup is the same for both,
        // collision is split between them so they don't overlap
        List<Cuboidf> selectionBoxes = new List<Cuboidf>();
        List<Vec3i?> selectionVoxels = new List<Vec3i?>();
        List<Cuboidf> collisionBoxes = new List<Cuboidf>();
        List<Cuboidf> topCollisionBoxes = new List<Cuboidf>();

        // Always add the stump as base collision/selection
        Cuboidf stumpBox = new Cuboidf(0, 0, 0, 1, 10f / 16f, 1);
        selectionBoxes.Add(stumpBox);
        selectionVoxels.Add(null);
        collisionBoxes.Add(stumpBox);

        float yStart = 10.0f;

        // For selection boxes: only generate for VISIBLE voxels (at least one exposed face)
        // For collision boxes: generate a simplified bounding box for better performance

        // Track bounds for collision box optimization
        int minX = 16, maxX = -1, minY = 16, maxY = -1, minZ = 16, maxZ = -1;
        bool hasAnyVoxels = false;

        for (int x = 0; x < 16; x++)
        {
            for (int y = 0; y < 16; y++)
            {
                for (int z = 0; z < 16; z++)
                {
                    if (Voxels[x, y, z] != (byte)EnumWoodMaterial.Empty)
                    {
                        hasAnyVoxels = true;

                        // Track bounds for collision
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                        if (z < minZ) minZ = z;
                        if (z > maxZ) maxZ = z;

                        // Only add a selection box if this voxel has at least one exposed face
                        // (neighbor is empty or out of bounds)
                        bool hasExposedFace =
                            (y == 15 || Voxels[x, y + 1, z] == 0) || // Top
                            (y == 0 || Voxels[x, y - 1, z] == 0) || // Bottom
                            (x == 0 || Voxels[x - 1, y, z] == 0) || // West
                            (x == 15 || Voxels[x + 1, y, z] == 0) || // East
                            (z == 0 || Voxels[x, y, z - 1] == 0) || // North
                            (z == 15 || Voxels[x, y, z + 1] == 0); // South

                        if (hasExposedFace)
                        {
                            float py = y + yStart;
                            selectionBoxes.Add(new Cuboidf(
                                x / 16f,
                                py / 16f,
                                z / 16f,
                                (x + 1) / 16f,
                                (py + 1) / 16f,
                                (z + 1) / 16f
                            ));
                            selectionVoxels.Add(new Vec3i(x, y, z));
                        }
                    }
                }
            }
        }

        // For collision, use a single bounding box covering all voxels
        // This is much more efficient than individual voxel collision boxes
        if (hasAnyVoxels)
        {
            float pyMin = (minY + yStart) / 16f;
            float pyMax = (maxY + 1 + yStart) / 16f;

            // Split it where the stump ends and the block above starts
            if (pyMin < 1)
            {
                collisionBoxes.Add(new Cuboidf(minX / 16f, pyMin, minZ / 16f, (maxX + 1) / 16f, Math.Min(pyMax, 1), (maxZ + 1) / 16f));
            }

            if (pyMax > 1)
            {
                topCollisionBoxes.Add(new Cuboidf(minX / 16f, Math.Max(pyMin, 1) - 1, minZ / 16f, (maxX + 1) / 16f, pyMax - 1, (maxZ + 1) / 16f));
            }
        }

        SelectionBoxes = selectionBoxes.ToArray();
        TopSelectionBoxes = selectionBoxes.Select(b => b.OffsetCopy(0, -1, 0)).ToArray();
        SelectionVoxels = selectionVoxels.ToArray();
        CollisionBoxes = collisionBoxes.ToArray();
        TopCollisionBoxes = topCollisionBoxes.ToArray();
    }

    /// <summary>
    /// Which voxel a selection box index is, null for the stump itself.
    /// </summary>
    public Vec3i? GetSelectedVoxel(int selectionBoxIndex)
    {
        if (selectionBoxIndex < 0 || selectionBoxIndex >= SelectionVoxels.Length) return null;
        return SelectionVoxels[selectionBoxIndex];
    }

    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();
        renderer?.Dispose();
    }

    public override void OnBlockBroken(IPlayer? byPlayer = null)
    {
        base.OnBlockBroken(byPlayer);

        // Drop the log instead of just deleting it
        if (Api.Side == EnumAppSide.Server && WorkItemStack != null)
        {
            Api.World.SpawnItemEntity(WorkItemStack.Clone(), Pos.ToVec3d().Add(0.5, 0.5, 0.5));
            WorkItemStack = null;
        }
    }

    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();
        renderer?.Dispose();
        dialog?.TryClose();
    }
}