using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TreeSplitting.Blocks;
using TreeSplitting.Recipes;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace TreeSplitting.Patches;

/// <summary>
/// Adds hewing to the handbook, like knapping and clay forming have.
/// Output pages get a "made by hewing" section with the logs that make it, the chopping block page lists everything that can be made on it.
/// </summary>
[HarmonyPatchCategory("treesplitting")]
public static class HandbookPatches
{
    private const int MediumPadding = 14;
    private const int TinyPadding = 2;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CollectibleBehaviorHandbookTextAndExtraInfo), nameof(CollectibleBehaviorHandbookTextAndExtraInfo.GetHandbookInfo))]
    public static void AddHewingInfo(ItemSlot inSlot, ICoreClientAPI capi, ItemStack[] allStacks, ActionConsumable<string> openDetailPageFor, ref RichTextComponentBase[] __result)
    {
        ItemStack? page = inSlot.Itemstack;
        if (page?.Collectible?.Code == null) return;

        List<HewingRecipe> recipes = TreeSplittingModSystem.GetRecipes(capi);
        if (recipes.Count == 0) return;

        List<RichTextComponentBase>? extra = page.Collectible is BlockChoppingBlock
            ? BuildChoppingBlockSection(capi, recipes, allStacks, openDetailPageFor)
            : BuildCreatedBySection(capi, recipes, page, allStacks, openDetailPageFor);

        if (extra == null) return;
        __result = __result.Concat(extra).ToArray();
    }

    // "Made by hewing these logs" on the output's page
    private static List<RichTextComponentBase>? BuildCreatedBySection(ICoreClientAPI capi, List<HewingRecipe> recipes, ItemStack page, ItemStack[] allStacks, ActionConsumable<string> openDetailPageFor)
    {
        // Quick check on the output code first so we don't go through every stack for every handbook page
        var candidates = recipes.Where(r => WildcardUtil.Match(OutputPattern(r), page.Collectible.Code)).ToList();
        if (candidates.Count == 0) return null;

        List<ItemStack> logs = new();
        foreach (ItemStack stack in allStacks)
        {
            foreach (HewingRecipe recipe in candidates)
            {
                if (!recipe.Matches(capi.World, stack)) continue;

                ItemStack? output = recipe.GenerateOutput(stack, capi.World);
                if (output?.Collectible.Code.Equals(page.Collectible.Code) != true) continue;

                logs.Add(stack);
                break;
            }
        }

        if (logs.Count == 0) return null;

        var components = new List<RichTextComponentBase>();
        AddHeading(components, capi, "treesplitting:handbook-hewing-createdby");
        AddSlideshow(components, capi, logs, openDetailPageFor);
        components.Add(new ClearFloatTextComponent(capi, TinyPadding));
        components.Add(new RichTextComponent(capi, Lang.Get("treesplitting:handbook-hewing-howto") + "\n", CairoFont.WhiteSmallText()));
        AddHeavyHitText(components, capi);
        return components;
    }

    // Everything that can be made on the chopping block, on its own page
    private static List<RichTextComponentBase>? BuildChoppingBlockSection(ICoreClientAPI capi, List<HewingRecipe> recipes, ItemStack[] allStacks, ActionConsumable<string> openDetailPageFor)
    {
        List<ItemStack> outputs = new();
        foreach (HewingRecipe recipe in recipes)
        {
            // Any log that works will do to show what it makes
            ItemStack? log = allStacks.FirstOrDefault(s => recipe.Matches(capi.World, s));
            ItemStack? output = log == null ? null : recipe.GenerateOutput(log, capi.World);
            if (output != null) outputs.Add(output);
        }

        if (outputs.Count == 0) return null;

        var components = new List<RichTextComponentBase>();
        AddHeading(components, capi, "treesplitting:handbook-hewing-makes");
        components.Add(new RichTextComponent(capi, Lang.Get("treesplitting:handbook-hewing-makes-desc") + "\n", CairoFont.WhiteSmallText()));
        AddSlideshow(components, capi, outputs, openDetailPageFor);
        AddHeavyHitText(components, capi);
        return components;
    }

    // What the axe's heavy hit does
    private static void AddHeavyHitText(List<RichTextComponentBase> components, ICoreClientAPI capi)
    {
        components.Add(new ClearFloatTextComponent(capi, TinyPadding));
        components.Add(new RichTextComponent(capi, Lang.Get("treesplitting:handbook-hewing-heavyhit") + "\n", CairoFont.WhiteSmallText()));
    }

    // {wood} turned into a wildcard so we can match output pages against it
    private static AssetLocation OutputPattern(HewingRecipe recipe)
    {
        return new AssetLocation(recipe.Output.Code.Domain, recipe.Output.Code.Path.Replace("{wood}", "*"));
    }

    private static void AddHeading(List<RichTextComponentBase> components, ICoreClientAPI capi, string langKey)
    {
        components.Add(new ClearFloatTextComponent(capi, MediumPadding));
        components.Add(new RichTextComponent(capi, Lang.Get(langKey) + "\n", CairoFont.WhiteSmallText().WithWeight(Cairo.FontWeight.Bold)));
    }

    private static void AddSlideshow(List<RichTextComponentBase> components, ICoreClientAPI capi, List<ItemStack> stacks, ActionConsumable<string> openDetailPageFor)
    {
        components.Add(new SlideshowItemstackTextComponent(capi, stacks.ToArray(), 40, EnumFloat.Inline,
            cs => openDetailPageFor(GuiHandbookItemStackPage.PageCodeForStack(cs))));
        components.Add(new ClearFloatTextComponent(capi, TinyPadding));
    }
}
