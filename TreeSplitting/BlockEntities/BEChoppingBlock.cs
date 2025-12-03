using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TreeSplitting.Network;
using TreeSplitting.Recipes;
using TreeSplitting.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using TreeSplitting.Enums;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace TreeSplitting.BlockEntities;

public enum EnumWoodMaterial : byte
{
    Empty = 0,
    Heartwood = 1,
    Sapwood = 2,
    Bark = 3,
}

// Potential rename to BEChoppingStation?
public class BEChoppingBlock : BlockEntity
{
    public byte[,,] Voxels = new byte[16, 16, 16];
    public byte[,,] TargetVoxels = null;

    public ItemStack WorkItemStack => workItemStack;

    public VSAPIHewingRecipe SelectedRecipe
    {
        get { return Api.GetHewingRecipes().FirstOrDefault(r => r.RecipeId == SelectedRecipeId); }
    }

    internal InventoryGeneric inventory;

    public ItemSlot WorkSlot => inventory[0];


    private ItemStack workItemStack;
    public int SelectedRecipeId = -1;

    // Visuals
    public Cuboidf[] SelectionBoxes = [];
    public Cuboidf[] CollisionBoxes = [];
    WoodWorkItemRenderer renderer;
    GuiDialogBlockEntityRecipeSelector dialog;


    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        inventory = new InventoryGeneric(1, null, null);
        workItemStack?.ResolveBlockOrItem(api.World);

        if (api is ICoreClientAPI capi)
        {
            if (SelectedRecipeId != -1)
            {
                GenerateTargetVoxels();
            }

            renderer = new WoodWorkItemRenderer(this, Pos, capi);

            capi.Event.RegisterRenderer(renderer, EnumRenderStage.Opaque);

            RegenSelectionBoxes();
        }
    }


    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        inventory.ToTreeAttributes(tree);
        tree.SetInt("selectedRecipeId", SelectedRecipeId);

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
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        inventory.FromTreeAttributes(tree);

        if (!WorkSlot.Empty)
        {
            WorkSlot.Itemstack.ResolveBlockOrItem(worldAccessForResolve);

            // Restore Voxels
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

            // CHANGED: Load ID
            int newRecipeId = tree.GetInt("recipeId", -1);

            if (SelectedRecipeId != newRecipeId)
            {
                SelectedRecipeId = newRecipeId;

                if (Api?.Side == EnumAppSide.Client && SelectedRecipe != null)
                {
                    GenerateTargetVoxels();
                }
            }
        }
        else
        {
            Voxels = new byte[16, 16, 16];
            SelectedRecipeId = -1;
            TargetVoxels = null;
        }

        RegenSelectionBoxes();
        MarkDirty(true);
    }


    // Helper to convert Recipe Booleans to Target Bytes
    private void GenerateTargetVoxels()
    {
        if (Api?.Side != EnumAppSide.Client) return;

        var recipe = SelectedRecipe;

        Api?.Logger?.Debug($"Generating Target Voxels for Recipe {SelectedRecipeId}");

        if (recipe == null || recipe.Voxels == null)
        {
            Api?.Logger?.Error($"SelectedRecipe is null or Voxels is null for Recipe {recipe.RecipeId}");
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
        if (workItemStack == null && byPlayer.Entity.Controls.ShiftKey) return TryPutLog(byPlayer);
        if (workItemStack != null && byPlayer.Entity.Controls.ShiftKey) return TryTakeLog(byPlayer);

        if (WorkItemStack != null && SelectedRecipe == null)
        {
            OpenRecipeDialog();
            return true;
        }

        return false;
    }

    private void OpenRecipeDialog()
    {
        var capi = Api as ICoreClientAPI;
        Api.Logger.Debug($"Trying to open RecipeDialog");

        if (capi == null || WorkSlot.Empty) return;

        var matching = WorkItemStack.GetMatchingHewingRecipes(Api.World);
        var stacks = matching.Select(r => r.Output.ResolvedItemstack).Where(s => s != null).ToArray();

        capi.Logger.Debug($"Found {matching.Count} matching recipes");

        if (matching.Count == 0) return;

        if (dialog?.IsOpened() == true) dialog.TryClose();

        dialog = new(null, stacks,
            (selectedIndex) =>
            {
                    var recipe = matching[selectedIndex];

                    byte[] data = SerializerUtil.Serialize(recipe.RecipeId);

                    capi.Network.SendBlockEntityPacket(Pos, (int)EnumHewingPacket.SelectRecipe, data);
                
            },
            () =>
            {
                capi.Network.SendBlockEntityPacket(Pos, (int)EnumHewingPacket.CancelSelect);
            },
            Pos,
            Api as ICoreClientAPI
        );
        dialog.TryOpen();
    }

    // TODO: Make tools use durability
    public void OnUseOver(IPlayer byPlayer, Vec3i voxelPos, BlockFacing facing, int toolMode
    )
    {
        if (workItemStack == null) return;

        ItemStack heldStack = byPlayer.InventoryManager.ActiveHotbarSlot.Itemstack;

        string heldItem = heldStack?.Item.Code.ToString();


        if (voxelPos.X < 0 || voxelPos.X >= 16 ||
            voxelPos.Y < 0 || voxelPos.Y >= 16 ||
            voxelPos.Z < 0 || voxelPos.Z >= 16) return;

        if (Voxels[voxelPos.X, voxelPos.Y, voxelPos.Z] == (byte)EnumWoodMaterial.Empty) return;

        Api.Logger.Debug($"OnUseOver: {voxelPos}, {facing}, {toolMode}, {heldItem}");

        if (heldItem.Contains("axe")) OnChop(voxelPos, facing, byPlayer, toolMode);
        if (heldItem.Contains("saw")) OnSaw(voxelPos, facing, byPlayer, toolMode);
        // Once durability handling is in place the knife should take more durability damage than the chisel
        if (heldItem.Contains("chisel") || heldItem.Contains("knife")) OnChiselOrKnife(voxelPos, facing, byPlayer);

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
            RemoveVoxels(new Vec3i(voxelPos.X, voxelPos.Y, 0), new Vec3i(voxelPos.X + 1, voxelPos.Y + 1, 16),
                new Vec3i(1, 1, 1));
        }
        else
        {
            // Player is primarily North/South, so cut a slice along the X-axis.
            RemoveVoxels(new Vec3i(0, voxelPos.Y, voxelPos.Z), new Vec3i(16, voxelPos.Y + 1, voxelPos.Z + 1),
                new Vec3i(1, 1, 1));
        }
    }

    private void HandleSawDown(Vec3i voxelPos, BlockFacing facing, IPlayer byPlayer)
    {
        double dx = byPlayer.Entity.Pos.X - (Pos.X + 0.5);
        double dz = byPlayer.Entity.Pos.Z - (Pos.Z + 0.5);

        if (Math.Abs(dx) > Math.Abs(dz))
        {
            // Player E/W, cut along X axis
            RemoveVoxels(new Vec3i(0, voxelPos.Y, voxelPos.Z), new Vec3i(16, voxelPos.Y + 1, voxelPos.Z + 1),
                new Vec3i(1, 1, 1));
        }
        else
        {
            // Player N/S, cut along Z axis
            RemoveVoxels(new Vec3i(voxelPos.X, voxelPos.Y, 0), new Vec3i(voxelPos.X + 1, voxelPos.Y + 1, 16),
                new Vec3i(1, 1, 1));
        }
    }

    private void OnChiselOrKnife(Vec3i voxelPos, BlockFacing facing, IPlayer byPlayer)
    {
        Api.Logger.Debug($"OnChisel: {voxelPos}, {facing}");
        HandleOneVoxel(voxelPos);
    }

    private bool TryPutLog(IPlayer byPlayer)
    {
        ItemSlot slot = byPlayer.InventoryManager.ActiveHotbarSlot;
        if (slot.Empty) return false;
        
        ItemStack heldStack = slot.Itemstack;
        
        bool isWorkable = Api.GetHewingRecipes().Any(r => r.Matches(Api.World, heldStack));

        if (!isWorkable) return false;

        WorkSlot.Itemstack = slot.TakeOut(1);
        WorkSlot.MarkDirty();


        GenerateLogVoxels();
        RegenSelectionBoxes();
        MarkDirty();

        if (Api.Side == EnumAppSide.Server)
        {
            ((ICoreServerAPI)Api).Network.SendBlockEntityPacket(byPlayer as IServerPlayer, Pos, (int)EnumHewingPacket.OpenDialog);
        }
        
        return true;
    }


    private bool TryTakeLog(IPlayer byPlayer)
    {
        if (WorkSlot.Empty) return false;
        
        ItemStack stackToReturn = WorkSlot.TakeOut(1);
        if (!byPlayer.InventoryManager.TryGiveItemstack(stackToReturn))
            Api.World.SpawnItemEntity(stackToReturn, Pos.ToVec3d().Add(0.5, 1.0, 0.5));
        ResetBlock();

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
            case EnumAxeToolModes.ChopVertical:
                HandleChopDown(pos, facing, player);
                break;
            case EnumAxeToolModes.ChopHorizontal:
                HandleChopSideways(pos);
                break;
        }
    }

    public override void OnReceivedClientPacket(IPlayer fromPlayer, int packetid, byte[] data)
    {

        if (packetid == (int)EnumHewingPacket.SelectRecipe)
        {
            int recipeId = SerializerUtil.Deserialize<int>(data);
            SelectedRecipeId = recipeId;
            MarkDirty();
            return;
        }
        
        if (packetid == (int)EnumHewingPacket.CancelSelect)
        {
            if (SelectedRecipeId == -1 && !WorkSlot.Empty)
            {
                TryTakeLog(fromPlayer);
            }

            return;
        }
        
        if (packetid == (int)EnumHewingPacket.OnUseOver)
        {
            using (MemoryStream ms = new MemoryStream(data))
            {
                BinaryReader reader = new BinaryReader(ms);
            
                // Read in exact order of writing
                int x = reader.ReadInt32();
                int y = reader.ReadInt32();
                int z = reader.ReadInt32();
                byte faceIndex = reader.ReadByte();
                int toolMode = reader.ReadInt32();

                Vec3i voxelPos = new Vec3i(x, y, z);
                BlockFacing face = BlockFacing.ALLFACES[faceIndex];

                OnUseOver(fromPlayer, voxelPos, face, toolMode);
                MarkDirty();
            }
            return;
        }
        
        base.OnReceivedClientPacket(fromPlayer, packetid, data);
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
            if (workItemStack != null)
            {
                ItemStack result = SelectedRecipe.Output.ResolvedItemstack;
                //if (!player.InventoryManager.TryGiveItemstack(result))
                Api.World.SpawnItemEntity(result, Pos.ToVec3d().Add(0.5, 1, 0.5));

                Api.World.PlaySoundAt(new AssetLocation("game:sounds/block/planks"), Pos.X, Pos.Y, Pos.Z, player);
            }

            ResetBlock();
        }
    }

    private void ResetBlock()
    {
        workItemStack = null;
        SelectedRecipeId = -1;
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

    public override void OnReceivedServerPacket(int packetid, byte[] data)
    {
        if (packetid == (int)EnumHewingPacket.OpenDialog)
        {
            OpenRecipeDialog();
            return;
        }
        
        base.OnReceivedServerPacket(packetid, data);
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

public enum EnumHewingPacket
{
    OpenDialog = 1000,
    SelectRecipe = 1001,
    OnUseOver = 1002,
    CancelSelect = 1003
}