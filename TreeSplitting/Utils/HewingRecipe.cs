using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace TreeSplitting.Utils
{
    public class HewingRecipe
    {

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
            if (world == null) return null;
            if (inputStack == null)
            {
                world.Logger.Warning("HewingRecipe.GenerateOutput called with null inputStack");
                return null;
            }
        
            try
            {
                string outputTemplate = Output?.Code?.ToString();
                if (string.IsNullOrEmpty(outputTemplate))
                {
                    world.Logger.Warning(
                        "HewingRecipe.GenerateOutput: recipe {0} Output.Code template is missing (Output.Code: {1})",
                        this.Code?.ToString() ?? "<null>",
                        outputTemplate ?? "<null>"
                    );
                    return null;
                }
        
                string inputCode = null;
                if (inputStack.Collectible != null && inputStack.Collectible.Code != null)
                {
                    inputCode = inputStack.Collectible.Code.Path;
                }
                else if (inputStack.Block != null && inputStack.Block.Code != null)
                {
                    inputCode = inputStack.Block.Code.Path;
                }
        
                if (string.IsNullOrEmpty(inputCode))
                {
                    world.Logger.Warning(
                        "HewingRecipe.GenerateOutput: cannot determine input stack code for recipe {0}",
                        this.Code ?? "<null>");
                    return null;
                }
        
                // Use the helper to robustly pick the wood name (e.g. "birch" from "log-placed-birch-ud")
                string woodName = ExtractWoodName(inputCode);
                if (string.IsNullOrEmpty(woodName))
                {
                    world.Logger.Warning(
                        "HewingRecipe.GenerateOutput: failed to extract wood identifier from '{0}' for recipe {1}",
                        inputCode, this.Code ?? "<null>");
                    return null;
                }
        
                // Replace the token `"{wood}"` and resolve
                string finalCode = outputTemplate.Replace("{wood}", woodName);
        
                world.Logger.Debug(
                    "HewingRecipe.GenerateOutput: recipe={0}, outputTemplate={1}, inputCode={2}, woodName={3}, finalCode={4}",
                    this.Code?.ToString() ?? "<null>",
                    outputTemplate,
                    inputCode,
                    woodName,
                    finalCode
                );
        
                AssetLocation finalLoc;
                try
                {
                    finalLoc = new AssetLocation(finalCode);
                }
                catch (Exception)
                {
                    world.Logger.Warning(
                        "HewingRecipe.GenerateOutput: invalid output asset location '{0}' for recipe {1}", finalCode,
                        this.Code ?? "<null>");
                    return null;
                }
                
                if (Output.Quantity == null) Output.Quantity = 1;
                
        
                var block = world.GetBlock(finalLoc);
                if (block != null && block.Code != null) return new ItemStack(block, Output.Quantity);
        
                var item = world.GetItem(finalLoc);
                if (item != null && item.Code != null) return new ItemStack(item, Output.Quantity);
        
                world.Logger.Warning("HewingRecipe.GenerateOutput: could not resolve output asset '{0}' for recipe {1}",
                    finalCode, this.Code ?? "<null>");
                return null;
            }
            catch (Exception ex)
            {
                world.Logger.Error("HewingRecipe.GenerateOutput threw: {0}\n{1}", ex.Message, ex.StackTrace);
                return null;
            }
        }
        
        private string ExtractWoodName(string inputCode)
        {
            if (string.IsNullOrEmpty(inputCode)) return null;

            var parts = inputCode.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            var ignore = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "log", "placed", "planted", "ud", "ld", "u", "d", "placedlog"
            };

            foreach (var p in parts)
            {
                if (ignore.Contains(p)) continue;
                if (p.All(char.IsDigit)) continue; // skip numeric segments
                return p;
            }

            // Fallbacks: regex like before, or last segment
            var m = Regex.Match(inputCode, @"log[-_](.+?)(?:[-_]|$)", RegexOptions.IgnoreCase);
            if (m.Success && m.Groups.Count > 1) return m.Groups[1].Value;

            return parts.Length > 0 ? parts[parts.Length - 1] : null;
        }
    }
}