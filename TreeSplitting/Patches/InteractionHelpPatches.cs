using System;
using System.Reflection;
using HarmonyLib;
using TreeSplitting.BlockEntities;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace TreeSplitting.Patches;

/// <summary>
///     The hint box in the corner rebuilds every time the looked at selection box changes. Every voxel of the log is its own
///     box, so sweeping across the log rebuilt it about 30 times a second at ~10 ms each. Hints
///     don't depend on which voxel it is, only on the log, so it only rebuilds when that changes.
/// </summary>
[HarmonyPatchCategory("treesplitting")]
[HarmonyPatch]
public static class InteractionHelpPatches
{
    private static readonly AccessTools.FieldRef<GuiDialog, ICoreClientAPI> Capi =
        AccessTools.FieldRefAccess<GuiDialog, ICoreClientAPI>("capi");

    private static BlockPos? lastPos;
    private static int lastState;

    private static MethodBase TargetMethod()
    {
        return AccessTools.Method("Vintagestory.Client.NoObf.HudElementInteractionHelp:BlockInView");
    }

    public static void Prefix(GuiDialog __instance, ref int ___currentBlockSelectionIndex)
    {
        var capi = Capi(__instance);
        var sel = capi.World.Player?.CurrentBlockSelection;
        if (sel == null || BEChoppingBlock.AtPosition(capi.World.BlockAccessor, sel.Position) is not { } be) return;

        var state = HashCode.Combine(be.WorkItemStack?.Collectible?.Code, be.SelectedRecipeCode, be.CanRotate,
            be.Horizontal);
        if (be.Pos.Equals(lastPos))
            // Same log and state, pretend it's still the same box so it skips the rebuild. Changed, force one even
            // if the crosshair hasn't moved, so the hints don't go stale after putting a log on or picking a recipe
            ___currentBlockSelectionIndex = state == lastState ? sel.SelectionBoxIndex : -1;

        lastPos = be.Pos;
        lastState = state;
    }
}
