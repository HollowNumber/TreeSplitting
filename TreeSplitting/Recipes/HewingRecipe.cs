using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace TreeSplitting.Recipes;

public class HewingRecipe : GenericRecipe
{
    protected override string TransformOutputCode(string outputTemplate, string inputCode, IWorldAccessor world)
    {
        if (outputTemplate.Contains("{woodname}")) return outputTemplate;
        
        string woodName = ExtractWoodName(inputCode);

        if (string.IsNullOrEmpty(woodName))
        {
            world.Logger.Warning($"Failed to extract wood name from input code '{inputCode}' for recipe {Code}");
            return null;
        }
        
        world.Logger.Debug($"HewingRecipe: {inputCode} -> wood={woodName}");
        return outputTemplate.Replace("{wood}", woodName);
        
    }


    private string ExtractWoodName(string inputCode)
    {
        if (string.IsNullOrEmpty(inputCode)) return null;

        var parts = inputCode.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        var ignore = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "log", "placed", "planted", "ud", "ld", "u", "d", "placedlog", "debarkedlog"
        };

        foreach (var p in parts)
        {
            if (ignore.Contains(p)) continue;
            if (p.All(char.IsDigit)) continue; // skip numeric segments
            return p;
        }

        // Fallbacks: regex like before, or last segment
        var m = Regex.Match(inputCode, @"log[-_](.+?)(?:[-_]|$)", RegexOptions.IgnoreCase);
        if (m is { Success: true, Groups.Count: > 1 }) return m.Groups[1].Value;

        return parts.Length > 0 ? parts[parts.Length - 1] : null;
    }
}