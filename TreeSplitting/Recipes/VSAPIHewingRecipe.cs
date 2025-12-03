using Vintagestory.API.Common;

namespace TreeSplitting.Recipes;

public class VSAPIHewingRecipe : LayeredVoxelRecipe<VSAPIHewingRecipe>, IByteSerializable
{
    public override VSAPIHewingRecipe Clone()
    {
        VSAPIHewingRecipe recipe = new VSAPIHewingRecipe();

        recipe.Pattern = new string[Pattern.Length][];

        for (int i = 0; i < recipe.Pattern.Length; i++)
        {
            recipe.Pattern[i] = (string[])Pattern[i].Clone();
        }

        recipe.Ingredient = Ingredient.Clone();
        recipe.Output = Output.Clone();
        recipe.Name = Name;

        return recipe;
    }

    public override int QuantityLayers => 16;
    public override string RecipeCategoryCode => "hewing";
}