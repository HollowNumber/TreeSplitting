using System;
using System.Collections.Generic;
using System.Linq;
using Cairo;
using HarmonyLib;
using TreeSplitting.Blocks;
using TreeSplitting.Utils;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util; // Required for ObjectCacheUtil
using Vintagestory.GameContent;

namespace TreeSplitting.Patches;

[HarmonyPatchCategory("treesplitting")]
public class AxePatches
{
    private static SkillItem[] CustomModes;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CollectibleObject), "OnLoaded")]
    public static void OnLoadedPostfix(CollectibleObject __instance, ICoreAPI api)
    {
        if (!(__instance is ItemAxe)) return;
        if (api.Side != EnumAppSide.Client) return;

        ICoreClientAPI capi = api as ICoreClientAPI;

        CustomModes = ObjectCacheUtil.GetOrCreate(api, "treesplittingAxeModes", () =>
        {
            SkillItem[] modes = new SkillItem[2];

            modes[0] = new SkillItem() { Code = new AssetLocation("chopping-down"), Name = "Chop Vertical" }.WithIcon(capi, (cr, x, y, width, height, rgba) =>  Drawing.DrawUpDown(cr, x, y, width, height, rgba, GameMath.PI));

            modes[1] = new SkillItem() { Code = new AssetLocation("chopping-sideways"), Name = "Chop Horizontal" }.WithIcon( capi, (cr, x, y, w, h, c) => Drawing.DrawUpDown(cr, x, y, w, h, c, GameMath.PI / 2));
            
            
            return modes;
        });
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CollectibleObject), "GetToolModes")]
    public static void onAxeToolModesPostfix(CollectibleObject __instance, ItemSlot slot, IClientPlayer forPlayer, BlockSelection blockSel, ref SkillItem[] __result)
    {
        if (!(__instance is ItemAxe)) return;
        if (blockSel == null) return;

        Block block = forPlayer.Entity.World.BlockAccessor.GetBlock(blockSel.Position);
        // Allow both Base and Top blocks
        if (block is not (BlockChoppingBlock or BlockChoppingBlockTop)) return;

        if (CustomModes == null) return;

        // Append our cached modes to the result
        if (__result == null)
        {
            __result = CustomModes;
        }
        else
        {
            __result = __result.Concat(CustomModes).ToArray();
        }
    }
    
    [HarmonyPrefix]
    [HarmonyPatch(typeof(CollectibleObject), "SetToolMode")]
    public static bool onSetToolMode(CollectibleObject __instance, ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection, int toolMode)
    {
        if (!(__instance is ItemAxe)) return true;
        
        slot.Itemstack.Attributes.SetInt("toolMode", toolMode);
        return false; // Skip original
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(CollectibleObject), "GetToolMode")]
    public static bool onGetToolMode(CollectibleObject __instance, ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection, ref int __result)
    {
        if (!(__instance is ItemAxe)) return true;

        __result = slot.Itemstack.Attributes.GetInt("toolMode", 0);
        return false; // Skip original
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CollectibleObject), "OnUnloaded")]
    public static void OnUnloadedPostFix(CollectibleObject __instance, ICoreAPI api)
    {
        if (!(__instance is ItemAxe)) return;

        for (int i = 0; CustomModes != null && i < CustomModes.Length; i++) CustomModes[i]?.Dispose();

    }

}