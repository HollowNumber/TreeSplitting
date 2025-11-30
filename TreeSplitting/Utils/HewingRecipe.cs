using System;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace TreeSplitting.Utils
{
    public class HewingRecipe
    {
        // -- JSON Loaded Fields --
        
        // The unique ID (filename)
        public AssetLocation Code;

        // Input item (e.g., "game:log-*-ud")
        public JsonItemStack Ingredient;

        // Output item (e.g., "game:woodenbeam-{wood}-ud")
        public JsonItemStack Output;

        // The raw text from the JSON file
        // Array of Layers -> Array of Rows (Strings)
        public string[][] Pattern;


        // -- Runtime Logic Fields --

        // The 3D grid used by the BlockEntity
        // true = Wood must exist here
        // false = Empty space (must be chopped)
        public bool[,,] Voxels;


        /// <summary>
        /// Called by your ModSystem after loading the JSON.
        /// Converts the text pattern into the boolean voxel grid.
        /// </summary>
        public void Resolve(IWorldAccessor world)
        {
            // 1. Resolve Items (Turn string codes into actual Item IDs)
            Ingredient.Resolve(world, "Hewing Recipe Ingredient");
            Output.Resolve(world, "Hewing Recipe Output");

            // 2. Initialize Voxel Grid (16x16x16)
            Voxels = new bool[16, 16, 16];

            // 3. Parse the Pattern
            // If only 1 layer is provided in JSON, we extrude it (copy to all Y levels)
            bool extrude = (Pattern.Length == 1);
            int layersToProcess = extrude ? 1 : Math.Min(Pattern.Length, 16);

            for (int yLayer = 0; yLayer < layersToProcess; yLayer++)
            {
                string[] rows = Pattern[yLayer];

                // Loop Z (Rows)
                for (int z = 0; z < 16; z++)
                {
                    if (z >= rows.Length) break; // Safety check

                    string row = rows[z];
                    
                    // Loop X (Characters)
                    for (int x = 0; x < 16; x++)
                    {
                        if (x >= row.Length) break; // Safety check

                        char c = row[x];
                        bool isWood = (c == '#'); // '#' means keep wood, '_' means chop

                        if (extrude)
                        {
                            // Apply to ALL Y levels
                            for (int y = 0; y < 16; y++)
                            {
                                Voxels[x, y, z] = isWood;
                            }
                        }
                        else
                        {
                            // Apply to specific Y level
                            Voxels[x, yLayer, z] = isWood;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Checks if the item the player is holding is valid for this recipe.
        /// Handles wildcards (e.g. log-oak-ud matches log-*-ud).
        /// </summary>
        public bool Matches(IWorldAccessor worldAccessor, ItemStack inputStack)
        {
            if (inputStack == null) return false;

            // FIX: Only use the built-in Matches if we successfully resolved a specific item earlier.
            if (Ingredient.ResolvedItemstack != null)
            {
                if (Ingredient.Matches(worldAccessor, inputStack)) return true;
            }

            // If it was a wildcard recipe (and thus not resolved), we MUST use wildcard matching.
            if (Ingredient.Code.Path.Contains("*"))
            {
                return WildcardUtil.Match(Ingredient.Code, inputStack.Collectible.Code);
            }

            return false;
        }

        /// <summary>
        /// Generates the correct output stack.
        /// Handles replacing {wood} with the actual wood type.
        /// </summary>
        public ItemStack GenerateOutput(ItemStack inputStack, IWorldAccessor world)
        {
            // 1. Start with the base output
            ItemStack finalOutput = Output.ResolvedItemstack.Clone();

            // 2. Handle Wildcard Replacement
            // If input was "log-oak-ud" and recipe was "log-*-ud"
            // We need to find "oak" and put it into "beam-{wood}-ud"
            if (Ingredient.Code.Path.Contains("*") && Output.Code.Path.Contains("{"))
            {
                string code = inputStack.Collectible.Code.Path;
                string pattern = Ingredient.Code.Path;
                
                // Extract the variable part (e.g. "oak")
                string extractedType = WildcardUtil.GetWildcardValue(new AssetLocation(pattern), new AssetLocation(code));

                // Inject it into output code
                string newOutputCode = Output.Code.Path.Replace("{wood}", extractedType).Replace("{type}", extractedType); // Handle generic names

                // Create new item stack from the specific block/item
                var blockOrItem = world.GetBlock(new AssetLocation(newOutputCode)) ?? (CollectibleObject)world.GetItem(new AssetLocation(newOutputCode));
                
                if (blockOrItem != null)
                {
                    finalOutput = new ItemStack(blockOrItem, Output.StackSize);
                }
            }

            return finalOutput;
        }
    }
}