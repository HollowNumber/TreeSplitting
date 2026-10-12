using System.Linq;
using System.Text;
using TreeSplitting.BlockEntities;
using TreeSplitting.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;

namespace TreeSplitting.Item;

/// <summary>
/// A log that's been partly hewn and taken off the chopping block, like the anvil's work items.
/// Keeps the voxels, the recipe, which way it was lying and the original log, so putting it back carries on where it was left.
/// </summary>
public class ItemHewnLog : Vintagestory.API.Common.Item
{
    public const string ItemCode = "treesplitting:hewnlog";

    public static ItemStack Create(IWorldAccessor world, ItemStack log, byte[,,] voxels, bool horizontal, int rotation, AssetLocation? recipeCode)
    {
        ItemStack stack = new ItemStack(world.GetItem(new AssetLocation(ItemCode)));
        ITreeAttribute attr = stack.Attributes;

        byte[] flat = new byte[16 * 16 * 16];
        System.Buffer.BlockCopy(voxels, 0, flat, 0, flat.Length);
        attr.SetBytes("voxels", flat);
        attr.SetBool("horizontal", horizontal);
        attr.SetInt("rotation", rotation);
        attr.SetItemstack("log", log.Clone());
        if (recipeCode != null) attr.SetString("recipeCode", recipeCode.ToString());

        return stack;
    }

    public static byte[,,]? GetVoxels(ItemStack stack)
    {
        byte[] flat = stack.Attributes.GetBytes("voxels");
        if (flat is not { Length: 4096 }) return null;

        byte[,,] voxels = new byte[16, 16, 16];
        System.Buffer.BlockCopy(flat, 0, voxels, 0, flat.Length);
        return voxels;
    }

    public static ItemStack? GetLog(ItemStack stack, IWorldAccessor world)
    {
        ItemStack? log = stack.Attributes.GetItemstack("log");
        log?.ResolveBlockOrItem(world);
        return log;
    }

    public static bool IsHorizontal(ItemStack stack) => stack.Attributes.GetBool("horizontal");
    public static int GetRotation(ItemStack stack) => stack.Attributes.GetInt("rotation");
    // Same as the chopping block, a lying log turned a quarter has its grain along Z
    private static int GrainAxis(ItemStack stack) => !IsHorizontal(stack) ? 1 : GetRotation(stack) % 2 == 0 ? 0 : 2;

    public static AssetLocation? GetRecipeCode(ItemStack stack)
    {
        string code = stack.Attributes.GetString("recipeCode");
        return code == null ? null : new AssetLocation(code);
    }

    public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
    {
        byte[,,]? voxels = GetVoxels(itemstack);
        ItemStack? log = GetLog(itemstack, capi.World);

        // Handbook and creative stacks don't have any voxels, just show the flat texture then
        if (voxels == null || log?.Collectible == null)
        {
            base.OnBeforeRender(capi, itemstack, target, ref renderinfo);
            return;
        }

        // Same shape and log means same mesh, so key the cache on that instead of storing an id on the stack like the anvil does
        string key = "treesplitting-hewnlog-" + log.Collectible.Code + "-" + GrainAxis(itemstack) + "-" + Hash(itemstack.Attributes.GetBytes("voxels"));
        int grainAxis = GrainAxis(itemstack);

        renderinfo.ModelRef = ObjectCacheUtil.GetOrCreate(capi, key, () =>
        {
            // The log sits 10 voxels up on the chopping block, as an item it starts at the bottom
            MeshData mesh = LogMesher.GenMesh(capi, log, voxels, grainAxis, 0);
            return capi.Render.UploadMultiTextureMesh(mesh);
        });

        base.OnBeforeRender(capi, itemstack, target, ref renderinfo);
    }

    private static string Hash(byte[] bytes)
    {
        // FNV-1a, plenty for telling shapes apart
        uint hash = 2166136261;
        foreach (byte b in bytes) hash = (hash ^ b) * 16777619;
        return hash.ToString("x8");
    }

    public override string GetHeldItemName(ItemStack itemStack)
    {
        ItemStack? log = GetLog(itemStack, api.World);
        string logName = log?.GetName() ?? Lang.Get("treesplitting:hewnlog-unknown");
        return Lang.Get("treesplitting:item-hewnlog", logName);
    }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

        ItemStack? log = GetLog(inSlot.Itemstack, world);
        AssetLocation? recipeCode = GetRecipeCode(inSlot.Itemstack);
        if (log == null || recipeCode == null) return;

        var recipe = TreeSplittingModSystem.GetRecipes(world.Api).FirstOrDefault(r => r.Code.Equals(recipeCode));
        ItemStack? output = recipe?.GenerateOutput(log, world);
        if (output != null) dsc.AppendLine(Lang.Get("treesplitting:hewnlog-making", output.GetName()));

        dsc.AppendLine(Lang.Get("treesplitting:hewnlog-howto"));
    }
}
