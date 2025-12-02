using System;
using System.Collections.Generic;
using System.Linq;
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
    public byte[,,] TargetVoxels = null;

    public ItemStack? WorkItemStack;
    public AssetLocation? SelectedRecipeCode;
    public HewingRecipe? SelectedRecipe; // Runtime only

    // Visuals
    public Cuboidf[] SelectionBoxes = [];
    public Cuboidf[] CollisionBoxes = [];
    WoodWorkItemRenderer renderer;
    GuiDialogRecipeSelector dialog;


    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);

        if (SelectedRecipeCode != null)
        {
            SelectedRecipe = TreeSplittingModSystem.Recipes.FirstOrDefault(x => x.Code.Equals(SelectedRecipeCode));

            // Re-Generate Target Voxels for Visuals
            if (SelectedRecipe != null)
            {
                GenerateTargetVoxels();
            }
        }

        if (api.Side == EnumAppSide.Client)
        {
            renderer = new WoodWorkItemRenderer(this, Pos, api as ICoreClientAPI);

            (api as ICoreClientAPI)?.Event.RegisterRenderer(renderer, EnumRenderStage.Opaque);

            RegenSelectionBoxes();
        }
    }


    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetItemstack("workItem", WorkItemStack);

        if (Voxels != null)
        {
            byte[] flatVoxels = new byte[16 * 16 * 16];
            int i = 0;
            for (int x = 0; x < 16; x++)
            for (int y = 0; y < 16; y++)
            for (int z = 0; z < 16; z++)
                flatVoxels[i++] = Voxels[x, y, z];

            tree.SetBytes("voxels", flatVoxels);
        }

        if (SelectedRecipeCode != null) tree.SetString("recipeCode", SelectedRecipeCode.ToString());
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        WorkItemStack = tree.GetItemstack("workItem");

        if (WorkItemStack != null)
        {
            WorkItemStack.ResolveBlockOrItem(worldAccessForResolve);

            // UNFLATTEN 1D Array -> 3D Array
            byte[] flatVoxels = tree.GetBytes("voxels");
            if (flatVoxels is { Length: 4096 })
            {
                Voxels = new byte[16, 16, 16];
                int i = 0;
                for (int x = 0; x < 16; x++)
                for (int y = 0; y < 16; y++)
                for (int z = 0; z < 16; z++)
                    Voxels[x, y, z] = flatVoxels[i++];
            }

            string code = tree.GetString("recipeCode");
            if (code != null)
            {
                SelectedRecipeCode = new AssetLocation(code);
                SelectedRecipe =
                    TreeSplittingModSystem.Recipes.FirstOrDefault(x => x.Code.Equals(SelectedRecipeCode));

                Api.Logger.Debug($"Resolved Recipe {SelectedRecipeCode} for Chopping Block at {Pos}");

                if (SelectedRecipe != null) GenerateTargetVoxels();
            }
        }
        else
        {
            Voxels = new byte[16, 16, 16];
            SelectedRecipe = null;
            TargetVoxels = null;
        }

        RegenSelectionBoxes();
        MarkDirty(true);
    }

    public void SetSelectedRecipe(AssetLocation code)
    {
        SelectedRecipeCode = code;
        SelectedRecipe = TreeSplittingModSystem.Recipes.FirstOrDefault(x => x.Code.Equals(SelectedRecipeCode));
        if (SelectedRecipe != null)
        {
            GenerateTargetVoxels();
            RegenSelectionBoxes();
            MarkDirty();
        }
    }

    // Helper to convert Recipe Booleans to Target Bytes
    private void GenerateTargetVoxels()
    {
        Api?.Logger?.Debug($"Generating Target Voxels for Recipe {SelectedRecipeCode}");

        if (SelectedRecipe == null || SelectedRecipe.Voxels == null)
        {
            Api?.Logger?.Error($"SelectedRecipe is null or Voxels is null for Recipe {SelectedRecipeCode}");
            TargetVoxels = null;
            return;
        }

        try
        {
            int srcX = SelectedRecipe.Voxels.GetLength(0);
            int srcY = SelectedRecipe.Voxels.GetLength(1);
            int srcZ = SelectedRecipe.Voxels.GetLength(2);

            // Allocate full 16x16x16 target (defaults to zeros)
            TargetVoxels = new byte[16, 16, 16];

            Api.Logger.Debug($"SelectedRecipe Voxels: {srcX}x{srcY}x{srcZ}");

            // Copy only within the source and target bounds to avoid exceptions
            int maxX = Math.Min(16, srcX);
            int maxY = Math.Min(16, srcY);
            int maxZ = Math.Min(16, srcZ);

            for (int x = 0; x < maxX; x++)
            {
                for (int y = 0; y < maxY; y++)
                {
                    for (int z = 0; z < maxZ; z++)
                    {
                        if (SelectedRecipe.Voxels[x, y, z])
                        {
                            TargetVoxels[x, y, z] = 1;
                        }
                    }
                }
            }

            Api.Logger.Debug(
                $"TargetVoxels: {TargetVoxels.GetLength(0)}x{TargetVoxels.GetLength(1)}x{TargetVoxels.GetLength(2)}");
        }

        catch (Exception ex)
        {
            Api?.Logger?.Error("GenerateTargetVoxels failed: {0}\n{1}", ex.Message, ex.StackTrace);
            TargetVoxels = null;
        }

        MarkDirty(true);
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
        Api.Logger.Debug($"Trying to open RecipeDialog");

        if (capi == null || WorkItemStack == null) return;

        var matching = new List<HewingRecipe>();
        var stacks = new List<ItemStack>();

        foreach (var recipe in TreeSplittingModSystem.Recipes)
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

        capi.Logger.Debug($"Found {matching.Count} matching recipes");

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

        ItemStack heldStack = byPlayer.InventoryManager.ActiveHotbarSlot.Itemstack;

        string heldItem = heldStack?.Item.Code.ToString();


        if (voxelPos.X < 0 || voxelPos.X >= 16 ||
            voxelPos.Y < 0 || voxelPos.Y >= 16 ||
            voxelPos.Z < 0 || voxelPos.Z >= 16) return;

        if (Voxels[voxelPos.X, voxelPos.Y, voxelPos.Z] == (byte)EnumWoodMaterial.Empty) return;

        Api.Logger.Debug($"OnUseOver: {voxelPos}, {facing}, {toolMode}, {heldItem}");

        if (heldItem.Contains("axe")) OnChop(voxelPos, facing, byPlayer, toolMode);
        if (heldItem.Contains("saw")) OnSaw(voxelPos, facing, byPlayer, toolMode);
        if (heldItem.Contains("chisel")) OnChisel(voxelPos, facing, byPlayer);

        CheckIfFinished(byPlayer);
        RegenSelectionBoxes();
        MarkDirty(true);
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
        Api.Logger.Debug($"OnSaw: {voxelPos}, {facing}, {toolMode}");

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

    private void OnChisel(Vec3i voxelPos, BlockFacing facing, IPlayer byPlayer)
    {
        Api.Logger.Debug($"OnChisel: {voxelPos}, {facing}");
        HandleChisel(voxelPos);
    }

    private bool TryPutLog(IPlayer byPlayer)
    {
        ItemStack heldStack = byPlayer.InventoryManager.ActiveHotbarSlot.Itemstack;
        if (heldStack == null) return false;

        // Do we have ANY matches?
        if (!TreeSplittingModSystem.Recipes.Any(r => r.Matches(Api.World, heldStack))) return false;

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
                double dist = Math.Sqrt(Math.Pow(x - 7.5, 2) + Math.Pow(z - 7.5, 2));
                byte mat = (byte)EnumWoodMaterial.Empty;
                if (dist <= 7.5)
                    mat = (dist > 6.0) ? (byte)EnumWoodMaterial.Bark : (byte)EnumWoodMaterial.Heartwood;
                for (int y = 0; y < 16; y++) Voxels[x, y, z] = mat;
            }
        }
    }

    private void OnChop(Vec3i pos, BlockFacing facing, IPlayer player, int toolMode)
    {
        var tool = (EnumAxeToolModes)toolMode;
        Api.Logger.Debug($"OnChop: {pos}, {facing}, {toolMode}");

        switch (tool)
        {
            case EnumAxeToolModes.ChopDown:
                HandleChopDown(pos, facing, player);
                break;
            case EnumAxeToolModes.ChopSideways:
                if (facing == BlockFacing.UP) return;
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

    private void HandleChisel(Vec3i pos)
    {
        Voxels[pos.X, pos.Y, pos.Z] = (byte)EnumWoodMaterial.Empty;
    }

    private void CheckIfFinished(IPlayer player)
    {
        // TODO: Either make this check if there are any voxels left and make the work item null, or do it somewhere else
        if (SelectedRecipe == null) return;

        bool ruined = false;
        bool finished = true;

        for (int x = 0; x < 16; x++)
        for (int y = 0; y < 16; y++)
        for (int z = 0; z < 16; z++)
        {
            bool recipeNeedsWood = SelectedRecipe.Voxels[x, y, z];
            bool hasWood = Voxels[x, y, z] != (byte)EnumWoodMaterial.Empty;

            //  Check if this voxel is actually within the log's radius
            double dist = Math.Sqrt(Math.Pow(x - 7.5, 2) + Math.Pow(z - 7.5, 2));
            bool insideLog = dist <= 7.5;

            if (recipeNeedsWood && !hasWood)
            {
                // Only ruin if we are INSIDE the log's radius.
                // If the recipe wants wood in the corner (outside radius), we ignore it.
                if (insideLog) ruined = true;
            }

            if (!recipeNeedsWood && hasWood)
            {
                // We still have wood that needs to be removed.
                finished = false;
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
        TargetVoxels = null;
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

        List<Cuboidf> selectionBoxes = new List<Cuboidf>();
        List<Cuboidf> collisionBoxes = new List<Cuboidf>();

        // Always add the stump as base collision/selection
        Cuboidf stumpBox = new Cuboidf(0, 0, 0, 1, 10f / 16f, 1);
        selectionBoxes.Add(stumpBox);
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
                        }
                    }
                }
            }
        }

        // For collision, use a single bounding box covering all voxels
        // This is much more efficient than individual voxel collision boxes
        if (hasAnyVoxels)
        {
            float pyMin = minY + yStart;
            float pyMax = maxY + 1 + yStart;
            collisionBoxes.Add(new Cuboidf(
                minX / 16f,
                pyMin / 16f,
                minZ / 16f,
                (maxX + 1) / 16f,
                pyMax / 16f,
                (maxZ + 1) / 16f
            ));
        }

        SelectionBoxes = selectionBoxes.ToArray();
        CollisionBoxes = collisionBoxes.ToArray();
    }

    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();
        renderer?.Dispose();
    }

    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();
        renderer?.Dispose();
    }
}