using TreeSplitting.BlockEntities;
using System;
using System.Collections.Generic;
using System.Linq;
using Cairo;
using HarmonyLib;
using TreeSplitting.Blocks;
using TreeSplitting.Utils;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
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
            SkillItem[] modes = new SkillItem[3];

            modes[0] = new SkillItem() { Code = new AssetLocation("chopping-down"), Name = "Chop Vertical" }.WithIcon(capi, (cr, x, y, width, height, rgba) =>  Drawing.DrawUpDown(cr, x, y, width, height, rgba, GameMath.PI));

            modes[1] = new SkillItem() { Code = new AssetLocation("chopping-sideways"), Name = "Chop Horizontal" }.WithIcon( capi, (cr, x, y, w, h, c) => Drawing.DrawUpDown(cr, x, y, w, h, c, GameMath.PI / 2));

            // Takes three planes in one go, wears the axe a lot more
            modes[2] = new SkillItem() { Code = new AssetLocation("chopping-heavy"), Name = Lang.Get("treesplitting:toolmode-heavyhit") }.WithIcon(capi, (cr, x, y, w, h, c) => Drawing.DrawSplit(cr, x, y, w, h, c));


            return modes;
        });
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CollectibleObject), "GetToolModes")]
    public static void onAxeToolModesPostfix(CollectibleObject __instance, ItemSlot slot, IClientPlayer forPlayer, BlockSelection blockSel, ref SkillItem[] __result)
    {
        if (!(__instance is ItemAxe)) return;
        if (blockSel == null) return;

        // The stump or the space above it
        if (BEChoppingBlock.AtPosition(forPlayer.Entity.World.BlockAccessor, blockSel.Position) == null) return;

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

        // Every axe type gets here, only dispose them once
        if (CustomModes == null) return;
        foreach (var mode in CustomModes) mode?.Dispose();
        CustomModes = null;

    }

}