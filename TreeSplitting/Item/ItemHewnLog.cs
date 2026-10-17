using System;
using System.Linq;
using System.Text;
using TreeSplitting.BlockEntities;
using TreeSplitting.Blocks;
using TreeSplitting.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Util;

namespace TreeSplitting.Item;

/// <summary>
///     A log that's been partly hewn and taken off the chopping block, like the anvil's work items.
///     Keeps the voxels, the recipe, which way it was lying and the original log, so putting it back carries on where it
///     was left.
/// </summary>
public class ItemHewnLog : Vintagestory.API.Common.Item
{
    public const string ItemCode = "treesplitting:hewnlog";

    public static ItemStack Create(IWorldAccessor world, ItemStack log, byte[,,] voxels, bool horizontal, int rotation,
        AssetLocation? recipeCode)
    {
        var stack = new ItemStack(world.GetItem(new AssetLocation(ItemCode)));
        var attr = stack.Attributes;

        var flat = new byte[16 * 16 * 16];
        Buffer.BlockCopy(voxels, 0, flat, 0, flat.Length);
        attr.SetBytes("voxels", flat);
        attr.SetBool("horizontal", horizontal);
        attr.SetInt("rotation", rotation);
        attr.SetItemstack("log", log.Clone());
        if (recipeCode != null) attr.SetString("recipeCode", recipeCode.ToString());
        // Kept on the stack itself like the anvil's meshRefId, so copies of it find the mesh straight away too
        attr.SetString("meshKey", MeshKey(log, flat, GrainAxis(stack)));

        return stack;
    }

    public static byte[,,]? GetVoxels(ItemStack stack)
    {
        var flat = stack.Attributes.GetBytes("voxels");
        if (flat is not { Length: 4096 }) return null;

        var voxels = new byte[16, 16, 16];
        Buffer.BlockCopy(flat, 0, voxels, 0, flat.Length);
        return voxels;
    }

    public static ItemStack? GetLog(ItemStack stack, IWorldAccessor world)
    {
        var log = stack.Attributes.GetItemstack("log");
        log?.ResolveBlockOrItem(world);
        return log;
    }

    public static bool IsHorizontal(ItemStack stack)
    {
        return stack.Attributes.GetBool("horizontal");
    }

    public static int GetRotation(ItemStack stack)
    {
        return stack.Attributes.GetInt("rotation");
    }

    // Same as the chopping block, a lying log turned a quarter has its grain along Z
    public static int GrainAxis(ItemStack stack)
    {
        return !IsHorizontal(stack) ? 1 : GetRotation(stack) % 2 == 0 ? 0 : 2;
    }

    public static AssetLocation? GetRecipeCode(ItemStack stack)
    {
        var code = stack.Attributes.GetString("recipeCode");
        return code == null ? null : new AssetLocation(code);
    }

    public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target,
        ref ItemRenderInfo renderinfo)
    {
        // Runs every frame for every one in sight, so go straight to the mesh once it's made
        if (itemstack.Attributes.GetString("meshKey") is { } knownKey &&
            capi.ObjectCache.TryGetValue(knownKey, out var known))
        {
            renderinfo.ModelRef = (MultiTextureMeshRef)known;
            base.OnBeforeRender(capi, itemstack, target, ref renderinfo);
            return;
        }

        var voxels = GetVoxels(itemstack);
        var log = GetLog(itemstack, capi.World);

        // Handbook and creative stacks don't have any voxels, just show the flat texture then
        if (voxels == null || log?.Collectible == null)
        {
            base.OnBeforeRender(capi, itemstack, target, ref renderinfo);
            return;
        }

        // Older stacks from before the key was saved on them get one now
        var key = itemstack.Attributes.GetString("meshKey");
        if (key == null)
        {
            key = MeshKey(log, itemstack.Attributes.GetBytes("voxels"), GrainAxis(itemstack));
            itemstack.Attributes.SetString("meshKey", key);
        }

        var grainAxis = GrainAxis(itemstack);

        renderinfo.ModelRef = ObjectCacheUtil.GetOrCreate(capi, key, () =>
        {
            // The log sits 10 voxels up on the chopping block, as an item it starts at the bottom
            var mesh = LogMesher.GenMesh(capi, log, voxels, grainAxis, 0);
            return capi.Render.UploadMultiTextureMesh(mesh);
        });

        base.OnBeforeRender(capi, itemstack, target, ref renderinfo);
    }

    // Right click a block face to put it down in the world as it is, for sculptures and the like
    public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel,
        EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
    {
        var player = (byEntity as EntityPlayer)?.Player;
        if (!firstEvent || blockSel == null || player == null || GetVoxels(slot.Itemstack) == null)
        {
            base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
            return;
        }

        var world = byEntity.World;
        var pos = blockSel.Position.AddCopy(blockSel.Face);
        var placed = world.GetBlock(new AssetLocation(BlockHewnLogPlaced.BlockCode));
        if (placed == null || world.BlockAccessor.GetBlock(pos).Replaceable < 6000 ||
            !world.Claims.TryAccess(player, pos, EnumBlockAccessFlags.BuildOrBreak)) return;

        handling = EnumHandHandling.PreventDefault;
        if (world.Side != EnumAppSide.Server) return;

        world.BlockAccessor.SetBlock(placed.Id, pos);
        if (world.BlockAccessor.GetBlockEntity(pos) == null)
            world.BlockAccessor.SpawnBlockEntity(placed.EntityClass, pos);
        if (world.BlockAccessor.GetBlockEntity(pos) is BEHewnLogPlaced be) be.SetPiece(slot.TakeOut(1));
        slot.MarkDirty();

        world.PlaySoundAt(new AssetLocation("game:sounds/block/planks"), pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5);
    }

    public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
    {
        return new[]
            {
                new WorldInteraction
                    { ActionLangCode = "treesplitting:heldhelp-hewnlog-place", MouseButton = EnumMouseButton.Right }
            }
            .Append(base.GetHeldInteractionHelp(inSlot));
    }

    // Same shape and log means same mesh, so every stack with the same one shares it
    private static string MeshKey(ItemStack log, byte[] voxels, int grainAxis)
    {
        return "treesplitting-hewnlog-" + log.Collectible.Code + "-" + grainAxis + "-" + Hash(voxels);
    }

    private static string Hash(byte[] bytes)
    {
        // FNV-1a, plenty for telling shapes apart
        var hash = 2166136261;
        foreach (var b in bytes) hash = (hash ^ b) * 16777619;
        return hash.ToString("x8");
    }

    public override string GetHeldItemName(ItemStack itemStack)
    {
        var log = GetLog(itemStack, api.World);
        var logName = log?.GetName() ?? Lang.Get("treesplitting:hewnlog-unknown");
        return Lang.Get("treesplitting:item-hewnlog", logName);
    }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

        var log = GetLog(inSlot.Itemstack, world);
        var recipeCode = GetRecipeCode(inSlot.Itemstack);
        if (log == null || recipeCode == null) return;

        var recipe = TreeSplittingModSystem.GetRecipes(world.Api).FirstOrDefault(r => r.Code.Equals(recipeCode));
        var output = recipe?.GenerateOutput(log, world);
        if (output != null) dsc.AppendLine(Lang.Get("treesplitting:hewnlog-making", output.GetName()));

        dsc.AppendLine(Lang.Get("treesplitting:hewnlog-howto"));
    }
}