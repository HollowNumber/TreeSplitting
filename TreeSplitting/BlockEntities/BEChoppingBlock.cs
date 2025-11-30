using System;
using System.Collections.Generic;
using System.Linq;
using TreeSplitting.Gui;
using TreeSplitting.Network;
using TreeSplitting.Rendering;
using TreeSplitting.Utils;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
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

    public enum EnumToolMode : byte
    {
        Chop = 0,
        Precise = 1,
    }

    public class BEChoppingBlock : BlockEntity
    {
        // Data
        public byte[,,] Voxels = new byte[16, 16, 16];

        // NEW: Target Voxels for Green Highlight (Matches Renderer Expectation)
        public byte[,,] TargetVoxels = null;

        public ItemStack? WorkItemStack;
        public AssetLocation? SelectedRecipeCode;
        public HewingRecipe? SelectedRecipe; // Runtime only

        // Visuals
        public Cuboidf[] SelectionBoxes = new Cuboidf[0];
        public Cuboidf[] CollisionBoxes = new Cuboidf[0];
        WoodWorkItemRenderer renderer;
        GuiDialogRecipeSelector dialog;

        private static readonly AnimationMetaData AnimationMetaData = new AnimationMetaData()
        {
            Code = "axechop",
            Animation = "axechop",
            AnimationSpeed = 1.5f,
            Weight = 10,
            BlendMode = EnumAnimationBlendMode.Average,
            EaseInSpeed = 999f, // Start immediately
            EaseOutSpeed = 999f, // End immediately
            TriggeredBy = new AnimationTrigger()
            {
                OnControls = new[] { EnumEntityActivity.Idle },
                MatchExact = false
            }
        }.Init();


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
                this.TargetVoxels = new byte[16, 16, 16];

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
                                this.TargetVoxels[x, y, z] = 1;
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

            // Don't declare 'var dialog', use the class field 'dialog' so we can close it later
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

        public void OnUseOver(IPlayer byPlayer, Vec3i voxelPos, BlockFacing facing, EnumToolMode toolMode)
        {
            if (WorkItemStack == null) return;
            OnChop(voxelPos, facing, byPlayer, toolMode);
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

        private void OnChop(Vec3i pos, BlockFacing facing, IPlayer player, EnumToolMode toolMode)
        {
            if (pos.X < 0 || pos.X >= 16 ||
                pos.Y < 0 || pos.Y >= 16 ||
                pos.Z < 0 || pos.Z >= 16) return;

            if (Voxels[pos.X, pos.Y, pos.Z] == (byte)EnumWoodMaterial.Empty) return;

            Voxels[pos.X, pos.Y, pos.Z] = (byte)EnumWoodMaterial.Empty;

            if (toolMode == EnumToolMode.Precise) // Chop or Cleave
            {
                // Simple downward force logic
                if (facing == BlockFacing.UP)
                {
                    for (int i = 1; i <= 3; i++)
                        if (pos.Y - i >= 0)
                            Voxels[pos.X, pos.Y - i, pos.Z] = (byte)EnumWoodMaterial.Empty;
                }

                // TODO: Add the more complex logic for precise
            }

            if (facing == BlockFacing.UP)
            {
                double playerX = player.Entity.Pos.X;
                double playerZ = player.Entity.Pos.Z;
                double blockCenterX = Pos.X + 0.5;
                double blockCenterZ = Pos.Z + 0.5;

                double dx = playerX - blockCenterX;
                double dz = playerZ - blockCenterZ;

                if (Math.Abs(dx) > Math.Abs(dz))
                {
                    // Player is East/West -> cleave along Z (vary z), keep X fixed
                    for (int x = 0; x < 16; x++)
                    {
                        for (int y = pos.Y; y >= 0; y--)
                        {
                            Voxels[x, y, pos.Z] = (byte)EnumWoodMaterial.Empty;
                        }
                    }
                }
                else
                {
                    // Player is North/South -> cleave along X (vary x), keep Z fixed
                    for (int z = 0; z < 16; z++)
                    {
                        for (int y = pos.Y; y >= 0; y--)
                        {
                            Voxels[pos.X, y, z] = (byte)EnumWoodMaterial.Empty;
                        }
                    }
                }
            }

            // Add sound/particles here if desired
            //Api.World.PlaySoundAt(new AssetLocation("game:sounds/block/chop2"), Pos.X, Pos.Y, Pos.Z, player);

            player.Entity.AnimManager.StartAnimation(AnimationMetaData);

            Api.World.RegisterCallback((dt) => { player.Entity.AnimManager.StopAnimation(AnimationMetaData.Code); },
                500);


            CheckIfFinished(player);
            RegenSelectionBoxes();
            MarkDirty(true);
        }

        private void CheckIfFinished(IPlayer player)
        {
            if (SelectedRecipe == null) return;

            bool ruined = false;
            bool finished = true;

            for (int x = 0; x < 16; x++)
            {
                for (int y = 0; y < 16; y++)
                {
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
                            if (insideLog)
                            {
                                ruined = true;
                            }
                        }

                        if (!recipeNeedsWood && hasWood)
                        {
                            // We still have wood that needs to be removed.
                            finished = false;
                        }
                    }
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

            if (WorkItemStack == null)
            {
                SelectionBoxes = new Cuboidf[0];
                CollisionBoxes = new Cuboidf[0];
                return;
            }

            List<Cuboidf> boxes = new List<Cuboidf>();
            boxes.Add(new Cuboidf(0, 0, 0, 1, 10f / 16f, 1)); // Stump

            float yStart = 10.0f;

            for (int x = 0; x < 16; x++)
            {
                for (int y = 0; y < 16; y++)
                {
                    for (int z = 0; z < 16; z++)
                    {
                        if (Voxels[x, y, z] != (byte)EnumWoodMaterial.Empty)
                        {
                            float py = y + yStart;

                            // Create selection box for voxel
                            boxes.Add(new Cuboidf(
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

            SelectionBoxes = boxes.ToArray();
            CollisionBoxes = boxes.ToArray();
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