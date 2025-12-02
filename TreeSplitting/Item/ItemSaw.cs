using TreeSplitting.Blocks;
using TreeSplitting.Utils;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace TreeSplitting.Item;

/// <summary>
///  Since Vintagestory doesn't have a saw item yet, we'll make one ourselves.
/// </summary>
public class ItemSaw : Vintagestory.API.Common.Item
{
    private SkillItem[] toolModes;


    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        
        if (api is ICoreClientAPI capi)
        {
            toolModes = ObjectCacheUtil.GetOrCreate(api, "treesplittingSawModes", () =>
            {
                SkillItem[] modes = new SkillItem[2];
                
                modes[0] = new SkillItem(){Code = new AssetLocation("line-down"), Name = "Line"}.WithIcon(capi, (cr, x, y, width, height, rgba) =>  Drawing.DrawUpset(cr, x, y, width, height, rgba, GameMath.PI));
                modes[1] = new SkillItem(){Code = new AssetLocation("line-sideways"), Name = "Line Sideways"}.WithIcon( capi, (cr, x, y, w, h, c) => Drawing.DrawUpDown(cr, x, y, w, h, c, GameMath.PI / 2));

                return modes;
            });
        }
    }


    public override void OnUnloaded(ICoreAPI api)
    {
        base.OnUnloaded(api);
        
        for (int i = 0; toolModes != null && i < toolModes.Length; i++)
        {
            toolModes[i]?.Dispose();
        }
    }

    public override SkillItem[] GetToolModes(ItemSlot slot, IClientPlayer forPlayer, BlockSelection blockSel)
    {
        if (blockSel is null) return null;
        Block block = forPlayer.Entity.World.BlockAccessor.GetBlock(blockSel.Position);
        return block is BlockChoppingBlock or BlockChoppingBlockTop ? toolModes : null;
    }

    public override int GetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection)
    {
        return slot.Itemstack.Attributes.GetInt("toolMode");
    }

    public override void SetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection, int toolMode)
    {
        slot.Itemstack.Attributes.SetInt("toolMode", toolMode);
    }
}