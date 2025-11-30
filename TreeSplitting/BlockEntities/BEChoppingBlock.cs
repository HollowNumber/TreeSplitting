using System;
using System.Collections.Generic;
using System.Linq;
using TreeSplitting.Rendering;
using TreeSplitting.Utils;
using Vintagestory.API.Client; // Assuming this is where HewingRecipe/System is
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace TreeSplitting.BlockEntities
{
    public enum EnumWoodMaterial : byte
    {
        Empty = 0,
        Heartwood = 1,
        Sapwood = 2,
        Bark = 3,
    }

    public class BEChoppingBlock : BlockEntity
    {
        // Data
        public byte[,,] Voxels = new byte[16, 16, 16];
        public ItemStack? WorkItemStack;
        public AssetLocation? SelectedRecipeCode;
        public HewingRecipe? SelectedRecipe; // Runtime only

        // Visuals
        public Cuboidf?[] SelectionBoxes = new Cuboidf[0];
        WoodWorkItemRenderer renderer; // You will implement this later

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            if (SelectedRecipeCode != null)
            {
                SelectedRecipe = TreeSplittingModSystem.Recipes.FirstOrDefault(x => x.Code == SelectedRecipeCode);
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
    
            // FLATTEN 3D Array -> 1D Array
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

            if (SelectedRecipeCode != null) tree.SetString("recipeCode", SelectedRecipeCode.Path);
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
                if (flatVoxels != null && flatVoxels.Length == 4096)
                {
                    Voxels = new byte[16, 16, 16];
                    int i = 0;
                    for (int x = 0; x < 16; x++)
                    for (int y = 0; y < 16; y++)
                    for (int z = 0; z < 16; z++)
                        Voxels[x, y, z] = flatVoxels[i++];
                }
        
                string code = tree.GetString("recipeCode");
                if (code != null) SelectedRecipeCode = new AssetLocation(code);
        
                RegenSelectionBoxes();
            }
        }

        // Called by BlockChoppingBlock.OnBlockInteractStart
        public bool OnInteract(IPlayer byPlayer, BlockSelection blockSel)
        {
            // CASE A: Placing a log (Shift + Right Click with Log)
            if (WorkItemStack == null && byPlayer.Entity.Controls.ShiftKey)
            {
                return TryPutLog(byPlayer);
            }

            // CASE B: Taking the log back (Shift + Right Click Empty Hand)
            if (WorkItemStack != null && byPlayer.Entity.Controls.ShiftKey)
            {
                return TryTakeLog(byPlayer);
            }

            return false;
        }

        // Called by BlockChoppingBlock.OnBlockInteractStart (or a specific packet)
        public void OnUseOver(IPlayer byPlayer, Vec3i voxelPos, BlockFacing facing)
        {
            if (WorkItemStack == null) return;

            // Check for Axe
            Item? heldItem = byPlayer.InventoryManager.ActiveHotbarSlot.Itemstack?.Item;
            if (heldItem is not { Tool: EnumTool.Axe }) return;

            OnChop(voxelPos, facing, byPlayer);
        }

        private bool TryPutLog(IPlayer byPlayer)
        {
            ItemStack heldStack = byPlayer.InventoryManager.ActiveHotbarSlot.Itemstack;
            if (heldStack == null) return false;

            // Find matching recipe
            // Passing Api.World here is crucial for the recipe wildcard check
            var recipe = TreeSplittingModSystem.Recipes.FirstOrDefault(r => r.Matches(Api.World, heldStack));
            if (recipe == null) return false;

            // 1. Move Item
            WorkItemStack = heldStack.Clone();
            WorkItemStack.StackSize = 1;
            byPlayer.InventoryManager.ActiveHotbarSlot.TakeOut(1);

            // 2. Setup Data
            SelectedRecipe = recipe;
            SelectedRecipeCode = recipe.Code;
            GenerateLogVoxels();

            // 3. Update
            RegenSelectionBoxes();
            MarkDirty();
            return true;
        }

        private bool TryTakeLog(IPlayer byPlayer)
        {
            if (WorkItemStack == null) return false;

            ItemStack stackToReturn = WorkItemStack.Clone();
            stackToReturn.StackSize = 1;

            if (!byPlayer.InventoryManager.TryGiveItemstack(stackToReturn))
            {
                Api.World.SpawnItemEntity(stackToReturn, Pos.ToVec3d().Add(0.5, 1.0, 0.5));
            }

            WorkItemStack = null;
            SelectedRecipe = null;
            SelectedRecipeCode = null;
            Voxels = new byte[16, 16, 16];

            RegenSelectionBoxes();
            MarkDirty();
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
                    {
                        mat = (dist > 6.0) ? (byte)EnumWoodMaterial.Bark : (byte)EnumWoodMaterial.Heartwood;
                    }

                    for (int y = 0; y < 16; y++)
                    {
                        Voxels[x, y, z] = mat;
                    }
                }
            }
        }

        private void OnChop(Vec3i pos, BlockFacing facing, IPlayer player)
        {
            // Safety Check
            if (pos.X < 0 || pos.X >= 16 || 
                pos.Y < 0 || pos.Y >= 16 || 
                pos.Z < 0 || pos.Z >= 16) return;

            // Basic Chop Logic
            if (Voxels[pos.X, pos.Y, pos.Z] == (byte)EnumWoodMaterial.Empty) return;

            Voxels[pos.X, pos.Y, pos.Z] = (byte)EnumWoodMaterial.Empty;

            if (facing == BlockFacing.UP)
            {
                for (int i = 1; i <= 3; i++)
                {
                    // Safety check for the downward propagation too!
                    if (pos.Y - i >= 0) 
                        Voxels[pos.X, pos.Y - i, pos.Z] = (byte)EnumWoodMaterial.Empty;
                }
            }

            CheckIfFinished(player);
            RegenSelectionBoxes();
            MarkDirty();
        }

        private void CheckIfFinished(IPlayer player)
        {
            if (SelectedRecipe == null) return;

            for (int x = 0; x < 16; x++)
            {
                for (int y = 0; y < 16; y++)
                {
                    for (int z = 0; z < 16; z++)
                    {
                        bool recipeNeedsWood = SelectedRecipe.Voxels[x, y, z];
                        bool hasWood = Voxels[x, y, z] != (byte)EnumWoodMaterial.Empty;

                        if (recipeNeedsWood != hasWood) return;
                    }
                }
            }

            // Finished!
            if (WorkItemStack != null)
            {
                ItemStack result = SelectedRecipe.GenerateOutput(WorkItemStack, Api.World);

                if (!player.InventoryManager.TryGiveItemstack(result))
                {
                    Api.World.SpawnItemEntity(result, Pos.ToVec3d().Add(0.5, 1, 0.5));
                }
            }

            WorkItemStack = null;
            SelectedRecipe = null;
            SelectedRecipeCode = null;
            Voxels = new byte[16, 16, 16];

            RegenSelectionBoxes();
            MarkDirty();
        }

        private void RegenSelectionBoxes()
        {
            if (Api?.Side == EnumAppSide.Client && renderer != null)
            {
                renderer.RegenMesh(WorkItemStack, Voxels);
            }

            if (WorkItemStack == null)
            {
                SelectionBoxes = [];
                return;
            }

            List<Cuboidf> boxes = new List<Cuboidf>();
            boxes.Add(null); // Index 0 = Stump

            // 2. Fix the Offset (The wireframes)
            float yStart = 10.0f;  // Matches stump height in choppingblock.json

            for (int x = 0; x < 16; x++)
            {
                for (int y = 0; y < 16; y++)
                {
                    for (int z = 0; z < 16; z++)
                    {
                        if (Voxels[x, y, z] != (byte)EnumWoodMaterial.Empty)
                        {
                            float py = y + yStart;

                            boxes.Add(new Cuboidf(
                                x / 16f,
                                py / 16f,
                                z / 16f,
                                x / 16f + 1 / 16f,
                                py / 16f + 1 / 16f,
                                z / 16f + 1 / 16f
                            ));
                        }
                    }
                }
            }

            SelectionBoxes = boxes.ToArray();
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
}