using System;
using System.Collections.Generic;
using System.Linq;
using TreeSplitting.Blocks;
using TreeSplitting.Gui;
using TreeSplitting.Item;
using TreeSplitting.Logic;
using TreeSplitting.Network;
using TreeSplitting.Recipes;
using TreeSplitting.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using TreeSplitting.Config;
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
    public bool[,,]? TargetVoxels
    {
        get
        {
            bool[,,]? voxels = SelectedRecipe?.Voxels;
            if (voxels == null || Rotation == 0) return voxels;

            // The recipe turns with the log, worked out once per recipe and turn
            if (rotatedTargetFor != SelectedRecipe || rotatedTargetTurn != Rotation)
            {
                rotatedTarget = voxels;
                for (int i = 0; i < Rotation; i++) rotatedTarget = RotateCw(rotatedTarget);
                rotatedTargetFor = SelectedRecipe;
                rotatedTargetTurn = Rotation;
            }
            return rotatedTarget;
        }
    }

    private bool[,,]? rotatedTarget;
    private HewingRecipe? rotatedTargetFor;
    private int rotatedTargetTurn;

    public ItemStack? WorkItemStack;
    // Log lying on its side with the grain running along X, for recipes that need it
    public bool Horizontal;
    // How many quarter turns the log has been turned on the block, like turning the work item on the anvil
    public int Rotation;

    // Which way the grain runs: up for a standing log, along X for a lying one, along Z once that's turned a quarter
    public int GrainAxis => !Horizontal ? 1 : Rotation % 2 == 0 ? 0 : 2;
    // Once it's been cut into it can't be tipped over anymore
    public bool HasBeenCut;
    public AssetLocation? SelectedRecipeCode;
    public HewingRecipe? SelectedRecipe; // Runtime only

    // Picked from the recipe list like any other, no target so nothing finishes or ruins. Can be swapped for a real recipe later
    public static readonly AssetLocation FreeformCode = new("treesplitting:freeform");
    public bool Freeform => FreeformCode.Equals(SelectedRecipeCode);

    // Index 0 is the stump, the rest are voxels. Top* are the same boxes moved down a block for the part above the stump
    public Cuboidf[] SelectionBoxes = [];
    public Cuboidf[] TopSelectionBoxes = [];
    public Cuboidf[] CollisionBoxes = [];
    public Cuboidf[] TopCollisionBoxes = [];
    // Which voxel each selection box is, same order as SelectionBoxes
    private Vec3i?[] SelectionVoxels = [];
    WoodWorkItemRenderer? renderer;
    GuiDialogRecipeSelector? dialog;

    // Which spots in the log's cross section are wood, and which of those are bark. Same for every log so only work it out once
    // Standing up that's (x, z), lying down it's (y, z)
    private static readonly bool[,] InsideLog = new bool[16, 16];
    private static readonly bool[,] IsBark = new bool[16, 16];
    // Lighter ring between the bark and the heartwood
    private static readonly bool[,] IsSapwood = new bool[16, 16];
    // How much wood a fresh log has, for working out how much firewood a cut up one is worth
    private static readonly int FullLogWood;

    private bool IsInLog(int x, int y, int z) => GrainAxis switch { 0 => InsideLog[y, z], 2 => InsideLog[x, y], _ => InsideLog[x, z] };
    private bool IsBarkAt(int x, int y, int z) => GrainAxis switch { 0 => IsBark[y, z], 2 => IsBark[x, y], _ => IsBark[x, z] };
    private bool IsSapwoodAt(int x, int y, int z) => GrainAxis switch { 0 => IsSapwood[y, z], 2 => IsSapwood[x, y], _ => IsSapwood[x, z] };

    static BEChoppingBlock()
    {
        for (int x = 0; x < 16; x++)
        for (int z = 0; z < 16; z++)
        {
            double dist = Math.Sqrt((x - 7.5) * (x - 7.5) + (z - 7.5) * (z - 7.5));
            InsideLog[x, z] = dist <= 7.5;
            IsBark[x, z] = dist > 6.0 && dist <= 7.5;
            IsSapwood[x, z] = dist > 4.5 && dist <= 6.0;
            if (InsideLog[x, z]) FullLogWood += 16;
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

        // Server needs these too, otherwise players fall through the log
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
        tree.SetBool("horizontal", Horizontal);
        tree.SetInt("rotation", Rotation);
        tree.SetBool("hasBeenCut", HasBeenCut);

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
        Horizontal = tree.GetBool("horizontal");
        Rotation = tree.GetInt("rotation");
        HasBeenCut = tree.GetBool("hasBeenCut");

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

    // A cut log can only go for recipes that don't need wood that's already gone
    private bool HasWoodFor(HewingRecipe recipe)
    {
        if (!HasBeenCut || recipe.Voxels == null) return true;

        bool[,,] target = recipe.Voxels;
        for (int i = 0; i < Rotation; i++) target = RotateCw(target);

        for (int x = 0; x < 16; x++)
        for (int y = 0; y < 16; y++)
        for (int z = 0; z < 16; z++)
        {
            if (target[x, y, z] && IsInLog(x, y, z) && Voxels[x, y, z] == (byte)EnumWoodMaterial.Empty) return false;
        }
        return true;
    }

    public void SetSelectedRecipe(AssetLocation code)
    {
        if (FreeformCode.Equals(code))
        {
            SelectedRecipeCode = FreeformCode;
            SelectedRecipe = null;
            RegenSelectionBoxes();
            MarkDirty();
            return;
        }

        SelectedRecipeCode = code;
        SelectedRecipe = FindRecipe(Api, SelectedRecipeCode);
        // Same check as the picker, in case it gets asked for one it didn't show
        if (SelectedRecipe != null && !HasWoodFor(SelectedRecipe))
        {
            SelectedRecipe = null;
            SelectedRecipeCode = null;
            return;
        }
        if (SelectedRecipe != null)
        {
            // The picker only shows recipes for how the log sits, this is just in case
            if (SelectedRecipe.Horizontal != Horizontal)
            {
                if (HasBeenCut)
                {
                    SelectedRecipe = null;
                    SelectedRecipeCode = null;
                    return;
                }

                Horizontal = SelectedRecipe.Horizontal;
                Rotation = 0;
                GenerateLogVoxels();
            }

            RegenSelectionBoxes();
            MarkDirty();
        }
    }

    public bool OnInteract(IPlayer byPlayer, BlockSelection blockSel)
    {
        if (WorkItemStack != null && byPlayer.Entity.Controls.CtrlKey) return TryRotateLog(byPlayer);
        if (CanTurn && IsHoldingTurningTool(byPlayer) && !byPlayer.Entity.Controls.ShiftKey) return TryTurnLog(byPlayer);
        if (WorkItemStack == null && byPlayer.Entity.Controls.ShiftKey) return TryPutLog(byPlayer);
        if (WorkItemStack != null && byPlayer.Entity.Controls.ShiftKey) return TryTakeLog(byPlayer);

        if (WorkItemStack != null && SelectedRecipe == null)
        {
            OpenRecipeDialog(byPlayer);
            return true;
        }

        return false;
    }

    // A cut log can only be tipped in freeform, a recipe wouldn't line up anymore
    public bool CanRotate => WorkItemStack != null && (!HasBeenCut || Freeform);

    // Before anything's picked a right click opens the picker, tool in hand or not
    public bool CanTurn => WorkItemStack != null && (SelectedRecipe != null || Freeform);

    private static bool IsHoldingTurningTool(IPlayer byPlayer)
    {
        EnumTool? tool = byPlayer.InventoryManager.ActiveHotbarSlot.Itemstack?.Collectible?.Tool;
        return tool is EnumTool.Axe or EnumTool.Saw or EnumTool.Knife or EnumTool.Chisel;
    }

    // Right click with a tool turns the log a quarter turn so the player doesn't have to walk around it, like the anvil
    private bool TryTurnLog(IPlayer byPlayer)
    {
        Voxels = RotateCw(Voxels);
        Rotation = (Rotation + 1) % 4;

        Api.World.PlaySoundAt(new AssetLocation("game:sounds/block/planks"), Pos.X + 0.5, Pos.Y + 1, Pos.Z + 0.5, byPlayer, 1.2f, 16, 0.5f);
        RegenSelectionBoxes();
        MarkDirty();
        return true;
    }

    // Quarter turn clockwise around the vertical axis, same as the anvil does it
    private static T[,,] RotateCw<T>(T[,,] voxels)
    {
        T[,,] rotated = new T[16, 16, 16];
        for (int x = 0; x < 16; x++)
        for (int y = 0; y < 16; y++)
        for (int z = 0; z < 16; z++)
        {
            rotated[z, y, x] = voxels[16 - x - 1, y, z];
        }
        return rotated;
    }

    // Ctrl + right click tips the log onto its side or stands it back up, which recipes show up depends on how it sits
    private bool TryRotateLog(IPlayer byPlayer)
    {
        if (!CanRotate) return false;

        // A cut log keeps its shape and just gets tipped over, a fresh one is made again
        if (HasBeenCut) TipVoxels(!Horizontal);
        Horizontal = !Horizontal;
        Rotation = 0;
        SelectedRecipe = null;
        // Freeform doesn't care how the log sits so it stays on
        SelectedRecipeCode = Freeform ? FreeformCode : null;
        if (!HasBeenCut) GenerateLogVoxels();

        Api.World.PlaySoundAt(new AssetLocation("game:sounds/block/planks"), Pos.X, Pos.Y, Pos.Z, byPlayer);
        RegenSelectionBoxes();
        MarkDirty();

        if (Api.Side == EnumAppSide.Client)
        {
            dialog?.TryClose();
            if (!HasBeenCut) OpenRecipeDialog(byPlayer);
        }

        return true;
    }

    // Grain up goes to along X when laying it down, and from along X or Z back up when standing it up
    private void TipVoxels(bool toLying)
    {
        byte[,,] tipped = new byte[16, 16, 16];
        int grain = GrainAxis;
        for (int x = 0; x < 16; x++)
        for (int y = 0; y < 16; y++)
        for (int z = 0; z < 16; z++)
        {
            if (toLying) tipped[y, 15 - x, z] = Voxels[x, y, z];
            else if (grain == 0) tipped[15 - y, x, z] = Voxels[x, y, z];
            else tipped[x, z, 15 - y] = Voxels[x, y, z];
        }
        Voxels = tipped;
    }

    private void OpenRecipeDialog(IPlayer player)
    {
        var capi = Api as ICoreClientAPI;
        if (capi == null || WorkItemStack == null) return;

        var matching = new List<HewingRecipe>();
        var stacks = new List<ItemStack>();
        var names = new List<string>();

        foreach (var recipe in TreeSplittingModSystem.GetRecipes(Api))
        {
            if (recipe.Horizontal == Horizontal && recipe.Matches(Api.World, WorkItemStack) && HasWoodFor(recipe))
            {
                ItemStack outStack = recipe.GenerateOutput(WorkItemStack, Api.World);

                if (outStack != null)
                {
                    matching.Add(recipe);
                    stacks.Add(outStack);
                }
            }
        }

        // Freeform always fits, last in the list
        if (!Freeform)
        {
            // The log as it sits right now, carving it as is
            ItemStack icon = ItemHewnLog.Create(Api.World, WorkItemStack, Voxels, Horizontal, Rotation, null);
            names.AddRange(stacks.Select(s => s.GetName()));
            stacks.Add(icon);
            names.Add(Lang.Get("treesplitting:recipe-freeform"));
        }
        else names.AddRange(stacks.Select(s => s.GetName()));

        if (stacks.Count == 0)
        {
            capi.TriggerIngameError(this, "nowoodleft", Lang.Get("treesplitting:ingameerror-hewnlog-nowood"));
            return;
        }

        if (dialog?.IsOpened() == true) dialog.TryClose();

        dialog = new GuiDialogRecipeSelector(capi, stacks,
            (selectedIndex) =>
            {
                if (selectedIndex >= 0 && selectedIndex < stacks.Count)
                {
                    AssetLocation code = selectedIndex < matching.Count ? matching[selectedIndex].Code : FreeformCode;
                    capi.Network.GetChannel("treesplitting").SendPacket(new RecipeSelectPacket()
                    {
                        Pos = Pos,
                        RecipeCode = code.ToString()
                    });
                }
            }, names: names
        );
        dialog.TryOpen();
    }

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
        EnumChopResult chop = EnumChopResult.None;

        switch (tool)
        {
            case EnumTool.Axe:
                chop = OnChop(voxelPos, byPlayer, toolMode);
                break;
            case EnumTool.Saw:
                OnSaw(voxelPos, facing, byPlayer, toolMode);
                break;
            // Once durability handling is in place the knife should take more durability damage than the chisel
            case EnumTool.Chisel:
                HandleOneVoxel(voxelPos);
                break;
            case EnumTool.Knife:
                OnKnife(voxelPos, facing);
                break;
        }

        bool splitOff = RemoveLoosePieces();
        int removed = woodBefore - CountWood();
        if (removed > 0) HasBeenCut = true;

        // Splits throw a good burst, bites only a puff, heavy hits the most
        float chipMul = chop switch
        {
            EnumChopResult.Split => 1.5f,
            EnumChopResult.Bite => 0.5f,
            EnumChopResult.Heavy => 2f,
            _ => 1f
        };
        SpawnWoodChips(chipStack, voxelPos, facing, (int)(removed * chipMul));

        if (removed > 0)
        {
            float durabilityMul = chop == EnumChopResult.Heavy ? TreeSplittingModSystem.GetConfig(Api).HeavyHitDurabilityMultiplier : 1f;
            DamageTool(byPlayer, tool.Value, durabilityMul);
            if (Api.Side == EnumAppSide.Server)
            {
                if (tool == EnumTool.Axe) ChopSounds.Play(Api.World, Pos, chop, byPlayer);
                else ChopSounds.Play(Api.World, Pos, ChopSounds.ForTool(tool.Value, Api.World), byPlayer);
            }
        }

        CheckIfFinished(byPlayer);
        // Only after the axe actually split something off, so a small carving doesn't turn into firewood on the next tap
        if (Freeform && splitOff && tool == EnumTool.Axe) PopOffRemainder();
        RegenSelectionBoxes();
        MarkDirty();
    }

    // In freeform, once a split leaves half a log or less on the block that comes off too, same as the piece that fell:
    // a half drops as a partly hewn log, a quarter as firewood. So a log splits into two halves and each half into two quarters
    private void PopOffRemainder()
    {
        int wood = CountWood();
        if (wood == 0 || wood * 2 > FullLogWood) return;

        if (Api.Side == EnumAppSide.Server)
        {
            List<Vec3i> rest = new();
            for (int x = 0; x < 16; x++)
            for (int y = 0; y < 16; y++)
            for (int z = 0; z < 16; z++)
            {
                if (Voxels[x, y, z] != (byte)EnumWoodMaterial.Empty) rest.Add(new Vec3i(x, y, z));
            }
            DropPiece(rest);
        }
        ResetBlock();
    }

    // Only wears the tool when something actually gets cut
    private void DamageTool(IPlayer byPlayer, EnumTool tool, float multiplier = 1f)
    {
        if (Api.Side != EnumAppSide.Server) return;

        TreeSplittingConfig config = TreeSplittingModSystem.GetConfig(Api);
        int cost = tool switch
        {
            EnumTool.Axe => config.AxeDurabilityCost,
            EnumTool.Saw => config.SawDurabilityCost,
            EnumTool.Knife => config.KnifeDurabilityCost,
            EnumTool.Chisel => config.ChiselDurabilityCost,
            _ => 0
        };
        cost = (int)Math.Round(cost * config.ToolDurabilityMultiplier * multiplier);
        if (cost <= 0) return;

        ItemSlot slot = byPlayer.InventoryManager.ActiveHotbarSlot;
        slot.Itemstack?.Collectible.DamageItem(Api.World, byPlayer.Entity, slot, cost);
    }

    // Like the anvil, bits that got cut off from the rest fall away. With a recipe anything that still has recipe wood in it stays
    // (firewood is four separate quarters), in freeform the biggest piece is the log and everything else goes
    private bool RemoveLoosePieces()
    {
        int[,,] group = new int[16, 16, 16];
        List<List<Vec3i>> pieces = new();
        Queue<Vec3i> queue = new();

        for (int x = 0; x < 16; x++)
        for (int y = 0; y < 16; y++)
        for (int z = 0; z < 16; z++)
        {
            if (Voxels[x, y, z] == (byte)EnumWoodMaterial.Empty || group[x, y, z] != 0) continue;

            List<Vec3i> piece = new();
            pieces.Add(piece);
            group[x, y, z] = pieces.Count;
            queue.Enqueue(new Vec3i(x, y, z));

            while (queue.Count > 0)
            {
                Vec3i v = queue.Dequeue();
                piece.Add(v);
                foreach (BlockFacing face in BlockFacing.ALLFACES)
                {
                    int nx = v.X + face.Normali.X, ny = v.Y + face.Normali.Y, nz = v.Z + face.Normali.Z;
                    if (nx is < 0 or > 15 || ny is < 0 or > 15 || nz is < 0 or > 15) continue;
                    if (Voxels[nx, ny, nz] == (byte)EnumWoodMaterial.Empty || group[nx, ny, nz] != 0) continue;
                    group[nx, ny, nz] = pieces.Count;
                    queue.Enqueue(new Vec3i(nx, ny, nz));
                }
            }
        }

        if (pieces.Count < 2) return false;

        List<Vec3i> main = pieces.MaxBy(p => p.Count)!;
        bool[,,]? target = TargetVoxels;
        bool Keep(List<Vec3i> piece) => target == null ? piece == main : piece.Any(v => target[v.X, v.Y, v.Z]);
        // All waste somehow, keep the biggest so there's still a log
        bool anyKept = pieces.Any(Keep);

        bool dropped = false;
        foreach (List<Vec3i> piece in pieces)
        {
            if (anyKept ? Keep(piece) : piece == main) continue;
            if (Api.Side == EnumAppSide.Server) DropPiece(piece);
            foreach (Vec3i v in piece) Voxels[v.X, v.Y, v.Z] = (byte)EnumWoodMaterial.Empty;
            dropped = true;
        }
        return dropped;
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

    // Chips fly off the side that got hit, more wood cut means more chips. Runs on the server so everyone sees them
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

    // The knife peels bark off a 3x3 patch on the face it hit, on bare wood it just takes the one voxel like the chisel
    private void OnKnife(Vec3i voxelPos, BlockFacing facing)
    {
        if (Voxels[voxelPos.X, voxelPos.Y, voxelPos.Z] != (byte)EnumWoodMaterial.Bark)
        {
            HandleOneVoxel(voxelPos);
            return;
        }

        // No spread into the log, only across the face
        int rx = facing.Axis == EnumAxis.X ? 0 : 1, ry = facing.Axis == EnumAxis.Y ? 0 : 1, rz = facing.Axis == EnumAxis.Z ? 0 : 1;
        for (int x = Math.Max(0, voxelPos.X - rx); x <= Math.Min(15, voxelPos.X + rx); x++)
        for (int y = Math.Max(0, voxelPos.Y - ry); y <= Math.Min(15, voxelPos.Y + ry); y++)
        for (int z = Math.Max(0, voxelPos.Z - rz); z <= Math.Min(15, voxelPos.Z + rz); z++)
        {
            if (Voxels[x, y, z] == (byte)EnumWoodMaterial.Bark) Voxels[x, y, z] = (byte)EnumWoodMaterial.Empty;
        }
    }

    private bool TryPutLog(IPlayer byPlayer)
    {
        ItemStack heldStack = byPlayer.InventoryManager.ActiveHotbarSlot.Itemstack;
        if (heldStack == null) return false;

        if (heldStack.Collectible is ItemHewnLog) return TryPutHewnLog(byPlayer, heldStack);
        if (heldStack.Collectible.Code.Domain == "treesplitting" && heldStack.Collectible.FirstCodePart() == "halflog") return TryPutHalfLog(byPlayer, heldStack);

        // Do we have ANY matches?
        if (!TreeSplittingModSystem.GetRecipes(Api).Any(r => r.Matches(Api.World, heldStack))) return false;

        WorkItemStack = heldStack.Clone();
        WorkItemStack.StackSize = 1;
        byPlayer.InventoryManager.ActiveHotbarSlot.TakeOut(1);

        SelectedRecipe = null;
        SelectedRecipeCode = null;
        Horizontal = false;
        Rotation = 0;
        HasBeenCut = false;

        GenerateLogVoxels();

        RegenSelectionBoxes();
        MarkDirty();

        OpenRecipeDialog(byPlayer);
        return true;
    }


    // Put a partly hewn log back, exactly how it was left
    private bool TryPutHewnLog(IPlayer byPlayer, ItemStack hewnStack)
    {
        byte[,,]? voxels = ItemHewnLog.GetVoxels(hewnStack);
        ItemStack? log = ItemHewnLog.GetLog(hewnStack, Api.World);
        if (voxels == null || log?.Collectible == null) return false;

        WorkItemStack = log;
        WorkItemStack.StackSize = 1;
        Voxels = voxels;
        Horizontal = ItemHewnLog.IsHorizontal(hewnStack);
        Rotation = ItemHewnLog.GetRotation(hewnStack);
        HasBeenCut = true;

        SelectedRecipeCode = ItemHewnLog.GetRecipeCode(hewnStack);
        SelectedRecipe = SelectedRecipeCode == null ? null : FindRecipe(Api, SelectedRecipeCode);
        // Recipe got removed or doesn't fit anymore, so a new one gets picked
        if (SelectedRecipe != null && SelectedRecipe.Horizontal != Horizontal)
        {
            SelectedRecipe = null;
            SelectedRecipeCode = null;
        }

        byPlayer.InventoryManager.ActiveHotbarSlot.TakeOut(1);
        byPlayer.InventoryManager.ActiveHotbarSlot.MarkDirty();

        RegenSelectionBoxes();
        MarkDirty();

        if (SelectedRecipe == null && !Freeform) OpenRecipeDialog(byPlayer);
        return true;
    }

    /// <summary>
    /// What comes back when the log comes off: the plain log if it hasn't been cut yet, otherwise a partly hewn log that remembers everything.
    /// </summary>
    private ItemStack? TakeWorkPiece()
    {
        if (WorkItemStack == null) return null;

        // An untouched half log comes back as a half log
        if (IsHalfLog && CountWood() * 2 == FullLogWood)
        {
            ItemStack half = WorkItemStack.Clone();
            half.StackSize = 1;
            return half;
        }

        if (HasBeenCut) return ItemHewnLog.Create(Api.World, WorkItemStack, Voxels, Horizontal, Rotation, SelectedRecipeCode);

        ItemStack log = WorkItemStack.Clone();
        log.StackSize = 1;
        return log;
    }

    // A half log stands on the block as half of a standing log, then it's the recipe list like any log
    private bool TryPutHalfLog(IPlayer byPlayer, ItemStack halfStack)
    {
        WorkItemStack = halfStack.Clone();
        WorkItemStack.StackSize = 1;
        Horizontal = false;
        Rotation = 0;
        GenerateLogVoxels();
        for (int x = 0; x < 16; x++)
        for (int y = 0; y < 16; y++)
        for (int z = 0; z < 8; z++)
        {
            Voxels[x, y, z] = (byte)EnumWoodMaterial.Empty;
        }
        HasBeenCut = true;
        SelectedRecipe = null;
        SelectedRecipeCode = null;

        byPlayer.InventoryManager.ActiveHotbarSlot.TakeOut(1);
        byPlayer.InventoryManager.ActiveHotbarSlot.MarkDirty();

        RegenSelectionBoxes();
        MarkDirty();

        OpenRecipeDialog(byPlayer);
        return true;
    }

    private bool IsHalfLog => WorkItemStack?.Collectible.Code.Domain == "treesplitting" && WorkItemStack.Collectible.FirstCodePart() == "halflog";

    private bool TryTakeLog(IPlayer byPlayer)
    {
        ItemStack? stackToReturn = TakeWorkPiece();
        if (stackToReturn == null) return false;
        if (!byPlayer.InventoryManager.TryGiveItemstack(stackToReturn))
            Api.World.SpawnItemEntity(stackToReturn, Pos.ToVec3d().Add(0.5, 1.0, 0.5));
        ResetBlock();

        // Close GUI if open
        if (Api.Side == EnumAppSide.Client) dialog?.TryClose();

        return true;
    }

    // Ruined logs give half what the grid would for the wood that's left, so mistakes still sting. Aged logs give aged firewood
    private ItemStack LeftoverFirewood() => Firewood(Math.Max(1, (int)(2.0 * CountWood() / FullLogWood)));

    private Block? HalfLog()
    {
        string? wood = WorkItemStack?.Collectible.Variant["wood"];
        return wood == null ? null : Api.World.GetBlock(new AssetLocation("treesplitting:halflog-" + wood + "-ns"));
    }

    private ItemStack Firewood(int quantity)
    {
        string code = WorkItemStack?.Collectible.Code.Path.Contains("aged") == true ? "game:agedfirewood" : "game:firewood";
        return new ItemStack(Api.World.GetItem(new AssetLocation(code)), quantity);
    }

    // A piece that fell off: more than a quarter log comes off as a partly hewn log in freeform so it can be worked on,
    // smaller ones a firewood, slivers are just chips
    private void DropPiece(List<Vec3i> piece)
    {
        if (WorkItemStack == null) return;

        ItemStack drop;
        // A half that comes off in freeform is a proper half log block, they stack and can go back on later.
        // Only real halves (a third and up, cuts take wood so a log can't give three), thinner slabs keep their shape as partly hewn logs
        if (Freeform && piece.Count * 3 >= FullLogWood && piece.Count * 16 <= FullLogWood * 9 && HalfLog() is { } halfLog)
        {
            drop = new ItemStack(halfLog);
        }
        else if (piece.Count * 4 > FullLogWood)
        {
            byte[,,] voxels = new byte[16, 16, 16];
            foreach (Vec3i v in piece) voxels[v.X, v.Y, v.Z] = Voxels[v.X, v.Y, v.Z];
            drop = ItemHewnLog.Create(Api.World, WorkItemStack, voxels, Horizontal, Rotation, FreeformCode);
        }
        else
        {
            // An eighth of a log or more is worth a firewood
            if (piece.Count * 8 < FullLogWood) return;
            drop = Firewood(1);
        }

        Api.World.SpawnItemEntity(drop, Pos.ToVec3d().Add(0.5, 1.2, 0.5));
    }

    private void GenerateLogVoxels()
    {
        for (int x = 0; x < 16; x++)
        for (int y = 0; y < 16; y++)
        for (int z = 0; z < 16; z++)
        {
            byte mat = (byte)EnumWoodMaterial.Empty;
            if (IsInLog(x, y, z))
            {
                mat = IsBarkAt(x, y, z) ? (byte)EnumWoodMaterial.Bark
                    : IsSapwoodAt(x, y, z) ? (byte)EnumWoodMaterial.Sapwood
                    : (byte)EnumWoodMaterial.Heartwood;
            }
            Voxels[x, y, z] = mat;
        }
    }

    private EnumChopResult OnChop(Vec3i pos, IPlayer player, int toolMode)
    {
        double dx = player.Entity.Pos.X - (Pos.X + 0.5);
        double dz = player.Entity.Pos.Z - (Pos.Z + 0.5);

        return AxeCut.Cut(Voxels, pos, dx, dz, (EnumAxeToolModes)toolMode, TreeSplittingModSystem.GetConfig(Api));
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

        TreeSplittingConfig config = TreeSplittingModSystem.GetConfig(Api);

        bool ruined = false;
        bool finished = true;

        for (int x = 0; x < 16; x++)
        for (int y = 0; y < 16; y++)
        for (int z = 0; z < 16; z++)
        {
            bool recipeNeedsWood = target[x, y, z];
            bool hasWood = Voxels[x, y, z] != (byte)EnumWoodMaterial.Empty;

            // Only ruin it if the missing wood was inside the log, the recipe can want wood in the corners
            if (recipeNeedsWood && !hasWood && IsInLog(x, y, z)) ruined = true;

            // Still wood left that has to go
            if (!recipeNeedsWood && hasWood) finished = false;
        }

        // Forgiving mode just ignores mistakes
        if (config.ForgivingMode) ruined = false;

        if (ruined)
        {
            Api.World.SpawnItemEntity(LeftoverFirewood(), Pos.ToVec3d().Add(0.5, 1, 0.5));
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
                if (result != null) result.StackSize = Math.Max(1, (int)Math.Round(result.StackSize * config.OutputMultiplier));
                if (result != null && !player.InventoryManager.TryGiveItemstack(result))
                {
                    Api.World.SpawnItemEntity(result, Pos.ToVec3d().Add(0.5, 1, 0.5));
                }

                ChopSounds.Play(Api.World, Pos, ChopSounds.Finish, null);
                Api.World.SpawnCubeParticles(Pos.ToVec3d().Add(0.5, 1.2, 0.5), WorkItemStack, 0.4f, 40, 0.6f, null, new Vec3f(0, 2, 0));
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
        Horizontal = false;
        Rotation = 0;
        HasBeenCut = false;
        RegenSelectionBoxes();
        MarkDirty();
    }

    private void RegenSelectionBoxes()
    {
        // Update Renderer
        if (Api?.Side == EnumAppSide.Client && renderer != null)
        {
            renderer.RegenMesh(WorkItemStack, Voxels, TargetVoxels, GrainAxis);
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

        // Drop the log (or what's left of it) instead of just deleting it
        if (Api.Side == EnumAppSide.Server && TakeWorkPiece() is { } piece)
        {
            Api.World.SpawnItemEntity(piece, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
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