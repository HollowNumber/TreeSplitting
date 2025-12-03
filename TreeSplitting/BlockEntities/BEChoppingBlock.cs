using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

public enum EnumHewingPacket
{
    OpenDialog = 1000,
    SelectRecipe = 1001,
    OnUseOver = 1002,
    CancelSelect = 1003
}

public class BEChoppingBlock : BlockEntity
{
    public byte[,,] Voxels = new byte[16, 16, 16];
    public byte[,,] TargetVoxels = null;
    public int SelectedRecipeId = -1;

    // Inventory System
    public ItemStack WorkItemStack;

    // Visuals & Selection
    public Cuboidf[] SelectionBoxes = [];
    public Cuboidf[] CollisionBoxes = [];
    public List<Vec3i> SelectionBoxToVoxelCoords = new List<Vec3i>(); // Index mapping

    WoodWorkItemRenderer renderer;
    GuiDialogBlockEntityRecipeSelector dialog;

    public VSAPIHewingRecipe SelectedRecipe
    {
        get { return Api.GetHewingRecipes().FirstOrDefault(r => r.RecipeId == SelectedRecipeId); }
    }


    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);


        WorkItemStack?.ResolveBlockOrItem(api.World);
        
        // If client, setup renderer
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

    #region Data Serialization / Deserialization

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        
        // Save Inventory
        if (WorkItemStack != null)
        {
            tree.SetItemstack("workItem", WorkItemStack);
        }
        
        // Save State
        tree.SetInt("selectedRecipeId", SelectedRecipeId);
        tree.SetBytes("voxels", SerializeVoxels());
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        
        // Load Inventory
        WorkItemStack = tree.GetItemstack("workItem");

        if (WorkItemStack != null)
        {
            WorkItemStack.ResolveBlockOrItem(worldAccessForResolve);
            
            // Load Voxels
            byte[] packedVoxels = tree.GetBytes("voxels");
            if (packedVoxels != null) DeserializeVoxels(packedVoxels);
            else Voxels = new byte[16, 16, 16];

            // Load Recipe State
            int newRecipeId = tree.GetInt("selectedRecipeId", -1);
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
            // Reset
            Voxels = new byte[16, 16, 16];
            SelectedRecipeId = -1;
            TargetVoxels = null;
        }

        RegenSelectionBoxes();
        MarkDirty(true);
    }

    private byte[] SerializeVoxels()
    {
        byte[] data = new byte[1024];
        int pos = 0;
        for (int x = 0; x < 16; x++)
        for (int y = 0; y < 16; y++)
        for (int z = 0; z < 16; z += 4)
        {
            byte v0 = Voxels[x, y, z];
            byte v1 = Voxels[x, y, z + 1];
            byte v2 = Voxels[x, y, z + 2];
            byte v3 = Voxels[x, y, z + 3];
            data[pos++] = (byte)((v0 & 0x3) | ((v1 & 0x3) << 2) | ((v2 & 0x3) << 4) | ((v3 & 0x3) << 6));
        }
        return data;
    }

    private void DeserializeVoxels(byte[] data)
    {
        Voxels = new byte[16, 16, 16];
        if (data.Length != 1024) return;
        int pos = 0;
        for (int x = 0; x < 16; x++)
        for (int y = 0; y < 16; y++)
        for (int z = 0; z < 16; z += 4)
        {
            byte packed = data[pos++];
            Voxels[x, y, z]     = (byte)(packed & 0x3);
            Voxels[x, y, z + 1] = (byte)((packed >> 2) & 0x3);
            Voxels[x, y, z + 2] = (byte)((packed >> 4) & 0x3);
            Voxels[x, y, z + 3] = (byte)((packed >> 6) & 0x3);
        }
    }

    #endregion


    #region Network

    public override void OnReceivedClientPacket(IPlayer fromPlayer, int packetid, byte[] data)
    {
        // 1. SELECT RECIPE
        if (packetid == (int)EnumHewingPacket.SelectRecipe)
        {
            int recipeId = SerializerUtil.Deserialize<int>(data);
            SelectedRecipeId = recipeId;
            MarkDirty();
            return;
        }
        
        // 2. CANCEL SELECT
        if (packetid == (int)EnumHewingPacket.CancelSelect)
        {
            if (SelectedRecipeId == -1 && WorkItemStack != null)
            {
                TryTakeLog(fromPlayer);
            }
            return;
        }
        
        // 3. TOOL USE (VOXELS)
        if (packetid == (int)EnumHewingPacket.OnUseOver)
        {
            using (MemoryStream ms = new MemoryStream(data))
            {
                BinaryReader reader = new BinaryReader(ms);
                int x = reader.ReadInt32();
                int y = reader.ReadInt32();
                int z = reader.ReadInt32();
                byte faceIndex = reader.ReadByte();
                int toolMode = reader.ReadInt32();

                Vec3i voxelPos = new Vec3i(x, y, z);
                BlockFacing face = BlockFacing.ALLFACES[faceIndex];

                OnUseOver(fromPlayer, voxelPos, face, toolMode);
            }
            return;
        }
        
        base.OnReceivedClientPacket(fromPlayer, packetid, data);
    }

    public override void OnReceivedServerPacket(int packetid, byte[] data)
    {
        // 4. OPEN DIALOG
        if (packetid == (int)EnumHewingPacket.OpenDialog)
        {
            OpenRecipeDialog();
            return;
        }
        base.OnReceivedServerPacket(packetid, data);
    }

    #endregion

   
    

    public bool OnInteract(IPlayer byPlayer, BlockSelection blockSel)
    {
        bool isShift = byPlayer.Entity.Controls.ShiftKey;
        
        if (WorkItemStack == null && isShift) return TryPutLog(byPlayer);
        if (WorkItemStack != null && isShift) return TryTakeLog(byPlayer);

        // If waiting for recipe, open dialog locally
        if (WorkItemStack != null && SelectedRecipeId == -1)
        {
            if (Api.Side == EnumAppSide.Client) OpenRecipeDialog();
            return true;
        }

        return false;
    }

    public void OnPlayerLeftClick(IPlayer player, BlockSelection sel)
    {
        if (Api.Side != EnumAppSide.Client || sel == null) return;

        
        ItemStack heldStack = player.InventoryManager.ActiveHotbarSlot.Itemstack;
        if (heldStack == null) return;
        

        string itemCode = heldStack.Item.Code.Path;
        if (!itemCode.Contains("axe") && !itemCode.Contains("saw") && !itemCode.Contains("chisel")) return;

        // Offhand Check for Chisel
        if (itemCode.Contains("chisel"))
        {
            ItemStack offhand = player.InventoryManager.OffhandHotbarSlot.Itemstack;
            if (offhand == null || !offhand.Item.Code.Path.Contains("hammer")) return; 
        }

        // 1. Find Voxel from Selection Box
        int index = sel.SelectionBoxIndex;
        Api.Logger.Warning($"[Debug] Left Click detected. Index: {index}, Total Boxes: {SelectionBoxToVoxelCoords.Count}");
        if (index >= 0 && index < SelectionBoxToVoxelCoords.Count)
        {
            Vec3i voxelPos = SelectionBoxToVoxelCoords[index];

            if (voxelPos == null)
            {
                Api.Logger.Warning("[Debug] Hit Index 0 (Stump). Ignoring.");
                return;
            }
            
            Api.Logger.Warning($"[Debug] Hit Voxel at {voxelPos}");

                string anim = heldStack.Item.GetHeldTpHitAnimation(player.InventoryManager.ActiveHotbarSlot, player.Entity) ?? "axehit";
                player.Entity.StartAnimation(anim);
                Api.World.PlaySoundAt(new AssetLocation("game:sounds/block/chop2"), Pos.X, Pos.Y, Pos.Z, player);

                int toolMode = heldStack.Attributes.GetInt("toolMode");
                SendUseOverPacket(voxelPos, sel.Face, toolMode);
        }
        else
        {
            Api.Logger.Error($"[Debug] Index {index} is out of bounds!");

        }
    }

    private void SendUseOverPacket(Vec3i pos, BlockFacing face, int toolMode)
    {
        using (MemoryStream ms = new MemoryStream())
        {
            BinaryWriter writer = new BinaryWriter(ms);
            writer.Write(pos.X);
            writer.Write(pos.Y);
            writer.Write(pos.Z);
            writer.Write((byte)face.Index);
            writer.Write(toolMode);

            ((ICoreClientAPI)Api).Network.SendBlockEntityPacket(
                Pos, 
                (int)EnumHewingPacket.OnUseOver, 
                ms.ToArray()
            );
        }
    }


    private bool TryPutLog(IPlayer byPlayer)
    {
        ItemSlot slot = byPlayer.InventoryManager.ActiveHotbarSlot;
        if (slot.Empty) return false;
        
        // Check recipe valid
        bool isWorkable = Api.GetHewingRecipes().Any(r => r.Matches(Api.World, slot.Itemstack));
        if (!isWorkable) return false;

        // Move Item
        WorkItemStack = slot.TakeOut(1);
        slot.MarkDirty();

        // Setup Voxels
        GenerateLogVoxels();
        RegenSelectionBoxes();
        MarkDirty();

        // Tell Client to Open Dialog
        if (Api.Side == EnumAppSide.Server)
        {
            ((ICoreServerAPI)Api).Network.SendBlockEntityPacket(byPlayer as IServerPlayer, Pos, (int)EnumHewingPacket.OpenDialog);
        }
        
        return true;
    }

    private bool TryTakeLog(IPlayer byPlayer)
    {
        if (WorkItemStack == null) return false;
        
        if (!byPlayer.InventoryManager.TryGiveItemstack(WorkItemStack))
            Api.World.SpawnItemEntity(WorkItemStack, Pos.ToVec3d().Add(0.5, 1.0, 0.5));
        
        ResetBlock();

        if (Api.Side == EnumAppSide.Client) dialog?.TryClose();
        return true;
    }

    private void ResetBlock()
    {
        WorkItemStack = null;
        SelectedRecipeId = -1;
        Voxels = new byte[16, 16, 16];
        TargetVoxels = null;
        RegenSelectionBoxes();
        MarkDirty();
    }


    private void OpenRecipeDialog()
    {
        var capi = Api as ICoreClientAPI;
        if (capi == null || WorkItemStack == null) return;

        // Note: Ensure you have the 'HewingRecipeExtensions' method we discussed earlier
        List<VSAPIHewingRecipe> matching = WorkItemStack.GetMatchingHewingRecipes(Api.World);
        var stacks = matching.Select(r => r.Output.ResolvedItemstack).Where(s => s != null).ToArray();

        if (matching.Count == 0) return;
        if (dialog?.IsOpened() == true) dialog.TryClose();

        dialog = new GuiDialogBlockEntityRecipeSelector(
            "Select Recipe",
            stacks,
            (selectedIndex) => {
                if (selectedIndex >= 0 && selectedIndex < matching.Count) {
                    var recipe = matching[selectedIndex];
                    byte[] data = SerializerUtil.Serialize(recipe.RecipeId);
                    capi.Network.SendBlockEntityPacket(Pos, (int)EnumHewingPacket.SelectRecipe, data);
                }
            },
            () => {
                capi.Network.SendBlockEntityPacket(Pos, (int)EnumHewingPacket.CancelSelect, null);
            },
            Pos,
            capi
        );
        dialog.TryOpen();
    }

    public Cuboidf[] GetTopBlockBoxes()
    {
        List<Cuboidf> topBoxes = new List<Cuboidf>();
        
        // Loop through all boxes (voxels + stump)
        for (int i = 0; i < SelectionBoxes.Length; i++)
        {
            Cuboidf box = SelectionBoxes[i];
            
            // If this box goes higher than 1.0 (into the block above)
            if (box.Y2 > 1.0f)
            {
                // Shift it down by 1.0 so it renders correctly in the top block's local space
                // Example: A box from 0.9 to 1.5 becomes -0.1 to 0.5
                // We clamp Y1 to 0 so we don't select the air below the top block
                float localY1 = Math.Max(0f, box.Y1 - 1.0f);
                float localY2 = box.Y2 - 1.0f;
                
                topBoxes.Add(new Cuboidf(box.X1, localY1, box.Z1, box.X2, localY2, box.Z2));
            }
        }
        return topBoxes.ToArray();
    }

    // HELPER: Handles a Left Click that occurred on the Top Block
    public void OnTopBlockLeftClick(IPlayer player, int topBoxIndex)
    {
        // We need to find which REAL Voxel corresponds to this "Top Box Index"
        int currentTopIndex = 0;
        
        for (int i = 0; i < SelectionBoxes.Length; i++)
        {
            if (SelectionBoxes[i].Y2 > 1.0f)
            {
                // We found a box that exists in the top layer
                if (currentTopIndex == topBoxIndex)
                {
                    // FOUND IT! 'i' is the real index in the main SelectionBoxes array
                    BlockSelection fakeSel = new BlockSelection() { 
                        SelectionBoxIndex = i, 
                        Face = BlockFacing.UP,
                        Position = Pos // Important: Use the Bottom Block's pos
                    }; 
                    
                    // Forward to the main logic
                    OnPlayerLeftClick(player, fakeSel);
                    return;
                }
                currentTopIndex++;
            }
        }
    }

    public void OnUseOver(IPlayer byPlayer, Vec3i voxelPos, BlockFacing facing, int toolMode)
    {
        if (WorkItemStack == null) return;

        if (voxelPos.X < 0 || voxelPos.X >= 16 || voxelPos.Y < 0 || voxelPos.Y >= 16 || voxelPos.Z < 0 || voxelPos.Z >= 16) return;
        if (Voxels[voxelPos.X, voxelPos.Y, voxelPos.Z] == (byte)EnumWoodMaterial.Empty) return;

        ItemStack heldStack = byPlayer.InventoryManager.ActiveHotbarSlot.Itemstack;
        string heldItem = heldStack?.Item.Code.ToString() ?? "";

        if (heldItem.Contains("axe")) OnChop(voxelPos, facing, byPlayer, toolMode);
        else if (heldItem.Contains("saw")) OnSaw(voxelPos, facing, byPlayer, toolMode);
        else if (heldItem.Contains("chisel") || heldItem.Contains("knife")) OnChiselOrKnife(voxelPos, facing, byPlayer);

        CheckIfFinished(byPlayer);
        RegenSelectionBoxes();
        MarkDirty(true);
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

    private void OnSaw(Vec3i voxelPos, BlockFacing facing, IPlayer byPlayer, int toolMode)
    {
        EnumSawToolModes tool = (EnumSawToolModes)toolMode;
        Api.Logger.Debug($"OnSaw: {voxelPos}, {facing}, {toolMode}");

        if (tool == EnumSawToolModes.SawDown) HandleSawDown(voxelPos, facing, byPlayer);
        else if (facing != BlockFacing.UP && tool == EnumSawToolModes.SawSideways) HandleSawSideways(voxelPos, byPlayer);
    }

    private void OnChiselOrKnife(Vec3i voxelPos, BlockFacing facing, IPlayer byPlayer)
    {
        HandleOneVoxel(voxelPos);
    }


    private void HandleOneVoxel(Vec3i pos)
    {
        Voxels[pos.X, pos.Y, pos.Z] = (byte)EnumWoodMaterial.Empty;
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

    private void HandleSawSideways(Vec3i voxelPos, IPlayer byPlayer)
    {
        double dx = byPlayer.Entity.Pos.X - (Pos.X + 0.5);
        double dz = byPlayer.Entity.Pos.Z - (Pos.Z + 0.5);

        if (Math.Abs(dx) > Math.Abs(dz))
        {
            // Player E/W
            RemoveVoxels(new Vec3i(voxelPos.X, voxelPos.Y, 0), new Vec3i(voxelPos.X + 1, voxelPos.Y + 1, 16), new Vec3i(1, 1, 1));
        }
        else
        {
            // Player N/S
            RemoveVoxels(new Vec3i(0, voxelPos.Y, voxelPos.Z), new Vec3i(16, voxelPos.Y + 1, voxelPos.Z + 1), new Vec3i(1, 1, 1));
        }
    }

    private void HandleSawDown(Vec3i voxelPos, BlockFacing facing, IPlayer byPlayer)
    {
        double dx = byPlayer.Entity.Pos.X - (Pos.X + 0.5);
        double dz = byPlayer.Entity.Pos.Z - (Pos.Z + 0.5);

        if (Math.Abs(dx) > Math.Abs(dz))
        {
            // Player E/W
            RemoveVoxels(new Vec3i(0, voxelPos.Y, voxelPos.Z), new Vec3i(16, voxelPos.Y + 1, voxelPos.Z + 1), new Vec3i(1, 1, 1));
        }
        else
        {
            // Player N/S
            RemoveVoxels(new Vec3i(voxelPos.X, voxelPos.Y, 0), new Vec3i(voxelPos.X + 1, voxelPos.Y + 1, 16), new Vec3i(1, 1, 1));
        }
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


    private void CheckIfFinished(IPlayer player)
    {
        if (SelectedRecipe == null) return;

        bool ruined = false;
        bool finished = true;

        for (int x = 0; x < 16; x++)
        for (int y = 0; y < 16; y++)
        for (int z = 0; z < 16; z++)
        {
            bool recipeNeedsWood = SelectedRecipe.Voxels[x, y, z];
            bool hasWood = Voxels[x, y, z] != (byte)EnumWoodMaterial.Empty;

            double dist = Math.Sqrt(Math.Pow(x - 7.5, 2) + Math.Pow(z - 7.5, 2));
            bool insideLog = dist <= 7.5;

            if (recipeNeedsWood && !hasWood)
            {
                if (insideLog) ruined = true;
            }

            if (!recipeNeedsWood && hasWood)
            {
                finished = false;
            }
        }

        if (ruined)
        {
            Api.World.SpawnItemEntity(new ItemStack(Api.World.GetItem(new AssetLocation("game:firewood")), 2), Pos.ToVec3d().Add(0.5, 1, 0.5));
            Api.World.PlaySoundAt(new AssetLocation("game:sounds/block/chop2"), Pos.X, Pos.Y, Pos.Z, player);
            ResetBlock();
            return;
        }

        if (finished)
        {
            ItemStack result = SelectedRecipe.Output.ResolvedItemstack;
            Api.World.SpawnItemEntity(result, Pos.ToVec3d().Add(0.5, 1, 0.5));
            Api.World.PlaySoundAt(new AssetLocation("game:sounds/block/planks"), Pos.X, Pos.Y, Pos.Z, player);
            ResetBlock();
        }
    }
    

    private void GenerateTargetVoxels()
    {
        if (Api?.Side != EnumAppSide.Client) return;

        var recipe = SelectedRecipe;
        if (recipe == null || recipe.Voxels == null)
        {
            TargetVoxels = null;
            return;
        }

        try
        {
            int srcX = recipe.Voxels.GetLength(0);
            int srcY = recipe.Voxels.GetLength(1);
            int srcZ = recipe.Voxels.GetLength(2);

            TargetVoxels = new byte[16, 16, 16];
            int maxX = Math.Min(16, srcX);
            int maxY = Math.Min(16, srcY);
            int maxZ = Math.Min(16, srcZ);

            for (int x = 0; x < maxX; x++)
            for (int y = 0; y < maxY; y++)
            for (int z = 0; z < maxZ; z++)
            {
                if (recipe.Voxels[x, y, z]) TargetVoxels[x, y, z] = 1;
            }
        }
        catch (Exception ex)
        {
            Api?.Logger?.Error("GenerateTargetVoxels failed: {0}", ex.Message);
            TargetVoxels = null;
        }
        MarkDirty(true);
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
        
        // Clear Map
        SelectionBoxToVoxelCoords.Clear();

        // Stump (Base) - Index 0
        Cuboidf stumpBox = new Cuboidf(0, 0, 0, 1, 10f / 16f, 1);
        selectionBoxes.Add(stumpBox);
        collisionBoxes.Add(stumpBox);
        SelectionBoxToVoxelCoords.Add(null); // Placeholder for non-voxel click

        float yStart = 10.0f;
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

                        // Bounding Box Logic
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                        if (z < minZ) minZ = z;
                        if (z > maxZ) maxZ = z;

                        bool hasExposedFace =
                            (y == 15 || Voxels[x, y + 1, z] == 0) ||
                            (y == 0 || Voxels[x, y - 1, z] == 0) ||
                            (x == 0 || Voxels[x - 1, y, z] == 0) ||
                            (x == 15 || Voxels[x + 1, y, z] == 0) ||
                            (z == 0 || Voxels[x, y, z - 1] == 0) ||
                            (z == 15 || Voxels[x, y, z + 1] == 0);

                        if (hasExposedFace)
                        {
                            float py = y + yStart;
                            selectionBoxes.Add(new Cuboidf(
                                x / 16f, py / 16f, z / 16f,
                                (x + 1) / 16f, (py + 1) / 16f, (z + 1) / 16f
                            ));
                            
                            // Map box index to coordinate
                            SelectionBoxToVoxelCoords.Add(new Vec3i(x, y, z));
                        }
                    }
                }
            }
        }

        // Bounding Box for Collision
        if (hasAnyVoxels)
        {
            float pyMin = minY + yStart;
            float pyMax = maxY + 1 + yStart;
            collisionBoxes.Add(new Cuboidf(
                minX / 16f, pyMin / 16f, minZ / 16f,
                (maxX + 1) / 16f, pyMax / 16f, (maxZ + 1) / 16f
            ));
        }

        SelectionBoxes = selectionBoxes.ToArray();
        CollisionBoxes = collisionBoxes.ToArray();
    }
}