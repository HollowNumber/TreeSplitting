using TreeSplitting.BlockEntities;
using TreeSplitting.Blocks;
using TreeSplitting.Utils;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

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

                modes[0] = new SkillItem() { Code = new AssetLocation("line-down"), Name = "Line" }.WithIcon(capi,
                    (cr, x, y, width, height, rgba) => Drawing.DrawUpset(cr, x, y, width, height, rgba, GameMath.PI));
                modes[1] =
                    new SkillItem() { Code = new AssetLocation("line-sideways"), Name = "Line Sideways" }.WithIcon(capi,
                        (cr, x, y, w, h, c) => Drawing.DrawUpDown(cr, x, y, w, h, c, GameMath.PI / 2));

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


    public override void OnHeldAttackStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel,
        EntitySelection entitySel,
        ref EnumHandHandling handling)
    {
        if (blockSel == null)
        {
            base.OnHeldAttackStart(slot, byEntity, blockSel, entitySel, ref handling);
            return;
        }

        IPlayer byPlayer = (byEntity as EntityPlayer)?.Player;

        if (byPlayer == null) return;

        BlockEntity be = GetChoppingBlockBE(byEntity);


        if (be is not BEChoppingBlock)
        {
            api.Logger.Debug($"Not a chopping block");
            base.OnHeldAttackStart(slot, byEntity, blockSel, entitySel, ref handling);
            return;
        }

        handling = EnumHandHandling.PreventDefault;
    }


    public override void OnHeldActionAnimStart(ItemSlot slot, EntityAgent byEntity, EnumHandInteract type)
    {
        IPlayer byPlayer = (byEntity as EntityPlayer)?.Player;
        BlockSelection blockSel = (byEntity as EntityPlayer)?.BlockSelection;

        if (type != EnumHandInteract.HeldItemAttack || blockSel == null) return;

        BlockEntity be = GetChoppingBlockBE(byEntity);


        if (be is not BEChoppingBlock) return;


        startHitAction(slot, byEntity, false);
    }

    internal BEChoppingBlock GetChoppingBlockBE(EntityAgent byEntity)
    {
        IPlayer byPlayer = (byEntity as EntityPlayer)?.Player;
        BlockSelection blockSel = (byEntity as EntityPlayer)?.BlockSelection;

        if (blockSel == null) return null;

        BlockEntity be = byEntity.World.BlockAccessor.GetBlockEntity(blockSel.Position);
        // Check if we're hitting the top block
        if (be is not BEChoppingBlock)
        {
            // Try the block below
            be = byEntity.World.BlockAccessor.GetBlockEntity(blockSel.Position.DownCopy());
        }

        return be as BEChoppingBlock;
    }

    private void startHitAction(ItemSlot slot, EntityAgent byEntity, bool merge)
    {
        string anim = GetHeldTpHitAnimation(slot, byEntity);

        float framesound = CollectibleBehaviorAnimationAuthoritative.getSoundAtFrame(byEntity, anim);
        float framehitaction = CollectibleBehaviorAnimationAuthoritative.getHitDamageAtFrame(byEntity, anim);

        slot.Itemstack.TempAttributes.SetBool("isChoppingAction", true);
        var state = byEntity.AnimManager.GetAnimationState(anim);

        byEntity.AnimManager.RegisterFrameCallback(new AnimFrameCallback()
        {
            Animation = anim, Frame = framesound,
            Callback = () => strikeChoppingBlockSound(byEntity, merge, slot.Itemstack)
        });
        byEntity.AnimManager.RegisterFrameCallback(new AnimFrameCallback()
        {
            Animation = anim, Frame = framehitaction,
            Callback = () => strikeChoppingBlock(byEntity, slot, slot.Itemstack)
        });
    }

    public override bool OnHeldAttackCancel(float secondsPassed, ItemSlot slot, EntityAgent byEntity,
        BlockSelection blockSelection,
        EntitySelection entitySel, EnumItemUseCancelReason cancelReason)
    {
        if (!slot.Itemstack.Attributes.GetBool("isChoppingAction"))
            return base.OnHeldAttackCancel(secondsPassed, slot, byEntity, blockSelection, entitySel, cancelReason);

        if (cancelReason is EnumItemUseCancelReason.Death or EnumItemUseCancelReason.Destroyed)
        {
            slot.Itemstack.TempAttributes.SetBool("isChoppingAction", false);
            return true;
        }

        return false;
    }

    public override bool OnHeldAttackStep(float secondsPassed, ItemSlot slot, EntityAgent byEntity,
        BlockSelection blockSelection,
        EntitySelection entitySel)
    {
        if (!slot.Itemstack.Attributes.GetBool("isChoppingAction"))
            return base.OnHeldAttackStep(secondsPassed, slot, byEntity, blockSelection, entitySel);

        if (blockSelection == null) return false;

        BlockEntity be = GetChoppingBlockBE(byEntity);

        if (be is not BEChoppingBlock) return false;

        string anim = GetHeldTpHitAnimation(slot, byEntity);
        return byEntity.AnimManager.IsAnimationActive(anim);
    }

    // Same with this
    private void strikeChoppingBlock(EntityAgent byEntity, ItemSlot slot, ItemStack choppingItem)
    {
        IPlayer byPlayer = (byEntity as EntityPlayer).Player;
        if (byPlayer == null) return;

        var blockSel = byPlayer.CurrentBlockSelection;
        if (blockSel == null) return;

        if (choppingItem != byPlayer.InventoryManager.ActiveHotbarSlot.Itemstack) return;

        Block hitBlock = byEntity.World.BlockAccessor.GetBlock(blockSel.Position);
        bool isTopBlock = hitBlock is BlockChoppingBlockTop;

        BlockEntity be = GetChoppingBlockBE(byEntity);

        if (be is not BEChoppingBlock) return;

        BEChoppingBlock bea = be as BEChoppingBlock;

        if (bea == null) return;

        if (api.World.Side == EnumAppSide.Client)
        {
            if (isTopBlock)
            {
                bea.OnTopBlockUseOver(byPlayer, blockSel.SelectionBoxIndex);
            }
            else
            {
                bea.OnUseOver(byPlayer, blockSel.SelectionBoxIndex);
            }
        }

        slot.Itemstack?.TempAttributes.SetBool("isChoppingAction", false);
    }

    // This can be thrown into a util
    private void strikeChoppingBlockSound(EntityAgent byEntity, bool merge, ItemStack strikingItem)
    {
        IPlayer byPlayer = (byEntity as EntityPlayer).Player;
        if (byPlayer == null) return;
        var blockSel = byPlayer.CurrentBlockSelection;
        if (blockSel == null) return;
        if (strikingItem != byPlayer.InventoryManager.ActiveHotbarSlot.Itemstack) return;

        byPlayer.Entity.World.PlaySoundAt(
            merge ? new AssetLocation("sounds/block/chop2") : new AssetLocation("sounds/block/chop1"),
            byPlayer.Entity,
            byPlayer,
            0.9f + (float)byEntity.World.Rand.NextDouble() * 0.2f,
            16,
            0.35f
        );
    }
}