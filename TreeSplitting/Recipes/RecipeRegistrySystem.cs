using System;
using System.Collections.Generic;
using Vintagestory.API.Common;

namespace TreeSplitting.Recipes;

public static class TreeSplittingAdditions
{
    public static List<VSAPIHewingRecipe> GetHewingRecipes(this ICoreAPI api)
    {
        return api.ModLoader.GetModSystem<RecipeRegistrySystem>().HewingRecipes;
    }


    public static void RegisterHewingRecipe(this ICoreAPI api, VSAPIHewingRecipe recipe)
    {
        api.ModLoader.GetModSystem<RecipeRegistrySystem>().RegisterHewingRecipe(recipe);
    }
}

public class DisableRecipeRegisteringSystem : ModSystem
{
    public override double ExecuteOrder() => 99999;
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    public override void AssetsFinalize(ICoreAPI api)
    {
        RecipeRegistrySystem.canRegister = false;
    }
}


public class RecipeRegistrySystem : ModSystem
{
    public static bool canRegister = true;


    public List<VSAPIHewingRecipe> HewingRecipes = [];

    public override double ExecuteOrder() => 0.6;

    public override void StartPre(ICoreAPI api)
    {
        base.StartPre(api);

        canRegister = true;
    }

    public override void Start(ICoreAPI api)
    {
        base.Start(api);

        HewingRecipes = api.RegisterRecipeRegistry<RecipeRegistryGeneric<VSAPIHewingRecipe>>("hewingrecipes")
            .Recipes;
    }


    public void RegisterHewingRecipe(VSAPIHewingRecipe recipe)
    {
        if (!canRegister)
            throw new InvalidOperationException("Cannot register recipes after the game has started.");
        recipe.RecipeId = HewingRecipes.Count + 1;

        HewingRecipes.Add(recipe);
    }
}