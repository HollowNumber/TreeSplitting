using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace TreeSplitting.Recipes;

public class HewingRecipe : GenericRecipe
{
    // Bits of a log code that aren't the wood type
    private static readonly HashSet<string> IgnoredCodeParts = new(StringComparer.OrdinalIgnoreCase)
    {
        "log", "placed", "planted", "grown", "resin", "resinharvested", "logsection",
        "ud", "ns", "we", "ne", "nw", "se", "sw", "ld", "u", "d", "placedlog", "debarkedlog"
    };

    private static readonly Regex LogWoodRegex = new(@"log[-_](.+?)(?:[-_]|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public override bool Matches(IWorldAccessor worldAccessor, ItemStack inputStack)
    {
        if (!base.Matches(worldAccessor, inputStack)) return false;
        if (AllowedWoods.Length == 0 && SkipWoods.Length == 0) return true;

        string? wood = ExtractWoodName(inputStack.Collectible.Code.Path);
        if (AllowedWoods.Length > 0 && !AllowedWoods.Contains(wood)) return false;
        return !SkipWoods.Contains(wood);
    }

    protected override string TransformOutputCode(string outputTemplate, string inputCode, IWorldAccessor world)
    {
        // Nothing to fill in, so don't bother working out the wood
        if (!outputTemplate.Contains("{wood}")) return outputTemplate;

        string woodName = ExtractWoodName(inputCode);

        if (string.IsNullOrEmpty(woodName))
        {
            world.Logger.Warning($"Failed to extract wood name from input code '{inputCode}' for recipe {Code}");
            return null;
        }

        return outputTemplate.Replace("{wood}", woodName);

    }


    private string ExtractWoodName(string inputCode)
    {
        if (string.IsNullOrEmpty(inputCode)) return null;

        var parts = inputCode.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var p in parts)
        {
            if (IgnoredCodeParts.Contains(p)) continue;
            if (p.All(char.IsDigit)) continue; // skip numeric segments
            return p;
        }

        // Fallbacks: regex like before, or last segment
        var m = LogWoodRegex.Match(inputCode);
        if (m is { Success: true, Groups.Count: > 1 }) return m.Groups[1].Value;

        return parts.Length > 0 ? parts[parts.Length - 1] : null;
    }
}