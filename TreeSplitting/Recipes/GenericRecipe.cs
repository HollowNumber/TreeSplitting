using System;
using System.IO;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace TreeSplitting.Recipes;

/// <summary>
/// Byte-serializable so the server can sync recipes to clients through a recipe registry;
/// the client never loads the "recipes" asset folder itself.
/// </summary>
public abstract class GenericRecipe : IByteSerializable
{
    // Common properties
    public AssetLocation Code { get; set; }
    public JsonItemStack Ingredient { get; set; }
    public JsonItemStack Output { get; set; }
    public string[][] Pattern { get; set; }
    public bool[,,] Voxels { get; protected set; }

    // Configurable settings
    protected virtual int GridSize => 16;
    protected virtual char SolidChar => '#';
    protected virtual char EmptyChar => '_';

    public void ToBytes(BinaryWriter writer)
    {
        writer.Write(Code.ToShortString());
        Ingredient.ToBytes(writer);
        Output.ToBytes(writer);

        writer.Write(Pattern?.Length ?? 0);
        foreach (string[] layer in Pattern ?? [])
        {
            writer.Write(layer.Length);
            foreach (string row in layer) writer.Write(row);
        }
    }

    public void FromBytes(BinaryReader reader, IWorldAccessor resolver)
    {
        Code = new AssetLocation(reader.ReadString());

        Ingredient = new JsonItemStack();
        Ingredient.FromBytes(reader, resolver);
        Output = new JsonItemStack();
        Output.FromBytes(reader, resolver);

        Pattern = new string[reader.ReadInt32()][];
        for (int i = 0; i < Pattern.Length; i++)
        {
            Pattern[i] = new string[reader.ReadInt32()];
            for (int j = 0; j < Pattern[i].Length; j++) Pattern[i][j] = reader.ReadString();
        }

        ParsePattern();
    }

    /// <summary>
    /// Resolves the recipe's item stacks. Must be called once blocks and items are registered (AssetsFinalize or later).
    /// Wildcard and templated codes can't be resolved to a single stack, so they are matched/built at runtime instead.
    /// </summary>
    public virtual void Resolve(IWorldAccessor world)
    {
        if (!IsPatternCode(Ingredient.Code)) Ingredient.Resolve(world, GetIngredientName());
        if (!IsPatternCode(Output.Code)) Output.Resolve(world, GetOutputName());
    }

    private static bool IsPatternCode(AssetLocation code)
    {
        return code.Path.Contains('*') || code.Path.Contains('{');
    }

    /// <summary>
    /// Checks if the input stack matches this recipe.
    /// </summary>
    public virtual bool Matches(IWorldAccessor worldAccessor, ItemStack inputStack)
    {
        if (inputStack?.Collectible?.Code == null) return false;

        // Handle wildcards
        if (Ingredient.Code.Path.Contains('*'))
        {
            return WildcardUtil.Match(Ingredient.Code, inputStack.Collectible.Code);
        }

        // Exact code. Compared directly so matching doesn't depend on Resolve() having run on this side.
        return Ingredient.Code.Equals(inputStack.Collectible.Code);
    }

    /// <summary>
    /// Generates the output itemstack for the given input.
    /// </summary>
    public virtual ItemStack GenerateOutput(ItemStack inputStack, IWorldAccessor world)
    {
        if (world == null || inputStack == null)
        {
            world?.Logger.Warning($"{GetRecipeTypeName()}.GenerateOutput called with null parameter");
            return null;
        }

        try
        {
            string outputTemplate = Output?.Code?.ToString();
            if (string.IsNullOrEmpty(outputTemplate))
            {
                world.Logger.Warning($"{GetRecipeTypeName()}: Output.Code template is missing for recipe {Code}");
                return null;
            }

            string inputCode = GetInputCode(inputStack);
            if (string.IsNullOrEmpty(inputCode))
            {
                world.Logger.Warning($"{GetRecipeTypeName()}: Cannot determine input code for recipe {Code}");
                return null;
            }

            // Transform the output template (can be overridden for custom behavior)
            string finalCode = TransformOutputCode(outputTemplate, inputCode, world);
            if (string.IsNullOrEmpty(finalCode))
            {
                world.Logger.Warning($"{GetRecipeTypeName()}: Failed to transform output for recipe {Code}");
                return null;
            }

            // Resolve the final item/block
            return ResolveOutputStack(finalCode, world);
        }
        catch (Exception ex)
        {
            world.Logger.Error($"{GetRecipeTypeName()}.GenerateOutput error: {ex.Message}\n{ex.StackTrace}");
            return null;
        }
    }

    /// <summary>
    /// Override this to customize how the output code is generated.
    /// Default: returns the template as-is (no substitution).
    /// </summary>
    protected virtual string TransformOutputCode(string outputTemplate, string inputCode, IWorldAccessor world)
    {
        return outputTemplate;
    }

    // Helper methods
    protected virtual string GetRecipeTypeName() => GetType().Name;
    protected virtual string GetIngredientName() => $"{GetRecipeTypeName()} Ingredient";
    protected virtual string GetOutputName() => $"{GetRecipeTypeName()} Output";

    protected string GetInputCode(ItemStack inputStack)
    {
        if (inputStack.Collectible?.Code != null)
            return inputStack.Collectible.Code.Path;

        if (inputStack.Block?.Code != null)
            return inputStack.Block.Code.Path;

        return null;
    }

    protected ItemStack ResolveOutputStack(string finalCode, IWorldAccessor world)
    {
        AssetLocation finalLoc;
        try
        {
            finalLoc = new AssetLocation(finalCode);
        }
        catch (Exception)
        {
            world.Logger.Warning($"Invalid output asset location '{finalCode}' for recipe {Code}");
            return null;
        }

        int quantity = Math.Max(1, Output.StackSize);

        var block = world.GetBlock(finalLoc);
        if (block?.Code != null) return new ItemStack(block, quantity);

        var item = world.GetItem(finalLoc);
        if (item?.Code != null) return new ItemStack(item, quantity);

        world.Logger.Warning($"Could not resolve output asset '{finalCode}' for recipe {Code}");
        return null;
    }

    public void ParsePattern()
    {
        if (Pattern == null) return;

        Voxels = new bool[GridSize, GridSize, GridSize];
        bool extrude = (Pattern.Length == 1);
        int layersToProcess = extrude ? 1 : Math.Min(Pattern.Length, GridSize);

        for (int yLayer = 0; yLayer < layersToProcess; yLayer++)
        {
            string[] rows = Pattern[yLayer];

            for (int z = 0; z < GridSize; z++)
            {
                if (z >= rows.Length) break;
                string row = rows[z];

                for (int x = 0; x < GridSize; x++)
                {
                    if (x >= row.Length) break;
                    char c = row[x];
                    bool isSolid = (c == SolidChar);

                    if (extrude)
                    {
                        for (int y = 0; y < GridSize; y++)
                        {
                            Voxels[x, y, z] = isSolid;
                        }
                    }
                    else
                    {
                        Voxels[x, yLayer, z] = isSolid;
                    }
                }
            }
        }
    }
}