using System.Collections.Generic;
using TreeSplitting.BlockEntities;
using TreeSplitting.Blocks;
using TreeSplitting.Config;
using TreeSplitting.Utils;
using Vintagestory.API.Common;

namespace TreeSplitting;

public class TreeSplittingModSystem : ModSystem
{
    public static TreeSplittingConfig Config;
    public static List<HewingRecipe> Recipes = new();

    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        
        // Load Assets

        LoadRecipes(api);

        string modid = Mod.Info.ModID;
        
        api.RegisterBlockEntityClass(modid + ".choppingblockentity", typeof(BEChoppingBlock));
        api.RegisterBlockClass(modid + ".choppingblock", typeof(BlockChoppingBlock));
    }

    private static void LoadRecipes(ICoreAPI api)
    {
        var recipes = api.Assets.GetMany<HewingRecipe>(api.Logger, "recipes/hewing");

        foreach (var entry in recipes)
        {
            HewingRecipe recipe = entry.Value;
            recipe.Code = entry.Key;
            
            recipe.Resolve(api.World);
            Recipes.Add(recipe);
        }
        
        api.Logger.Notification("Loaded {0} tree splitting recipes", Recipes.Count);
    }
}