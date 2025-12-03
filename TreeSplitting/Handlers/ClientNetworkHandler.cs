using TreeSplitting.BlockEntities;
using TreeSplitting.Blocks;
using TreeSplitting.Network;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace TreeSplitting.Handlers;

public class ClientNetworkHandler
{
    private readonly ICoreClientAPI _capi;
    private readonly IClientNetworkChannel _channel;


    public ClientNetworkHandler(ICoreClientAPI capi)
    {
        _capi = capi;

        _channel = capi.Network.GetChannel("treesplitting");

        capi.Event.MouseDown += OnMouseDown;
    }

    private void OnMouseDown(MouseEvent e)
    {
        if (e.Button != EnumMouseButton.Left) return;

        // We assume that if the mouse isn't grabbed the player is in some ui and therefore we dont interact.
        if (!_capi.Input.MouseGrabbed) return;


        BlockSelection sel = _capi.World.Player.CurrentBlockSelection;
        if (sel is null) return;

        Block block = _capi.World.BlockAccessor.GetBlock(sel.Position);

        if (block is not (BlockChoppingBlockTop or BlockChoppingBlock))
            return;

        ItemSlot activeSlot = _capi.World.Player.InventoryManager.ActiveHotbarSlot;
        ItemStack heldItemStack = activeSlot.Itemstack;

        if (heldItemStack?.Item?.Tool is not (EnumTool.Axe or EnumTool.Saw or EnumTool.Chisel or EnumTool.Knife)) return;

        
        if (heldItemStack?.Item?.Tool is EnumTool.Chisel )
        {
            string offhand = _capi.World.Player.InventoryManager.OffhandHotbarSlot.Itemstack?.Item?.Code.ToString();

            if (offhand is null || !offhand.Contains("hammer"))
            {
                //The game does this automatically _capi.TriggerIngameError(new AssetLocation("treesplitting"), "chiselhammer", "You need to hold a hammer in your offhand to chisel wood voxels.");
                return;     
            }
           
        }
        
        BlockPos pos = sel.Position;

        if (block is BlockChoppingBlockTop) pos = pos.DownCopy();

        BEChoppingBlock be = _capi.World.BlockAccessor.GetBlockEntity<BEChoppingBlock>(pos);

        if (be is null) return;

        if (sel.SelectionBoxIndex <= 0 || sel.SelectionBoxIndex >= be.SelectionBoxes.Length) return;
        e.Handled = true;

        // Calculate voxel coord

        Cuboidf box = be.SelectionBoxes[sel.SelectionBoxIndex];

        int x = (int)(box.X1 * 16);
        int y = (int)((box.Y1 * 16) - 10);
        int z = (int)(box.Z1 * 16);

        int mode = heldItemStack.Attributes.GetInt("toolMode");

        _capi.Logger.Debug($"Calling Animation for {heldItemStack.Item.Code.Path} at {pos} with mode {mode}");
        string anim = heldItemStack.Item.GetHeldTpHitAnimation(activeSlot, _capi.World.Player.Entity);

        // So we default to "axehit" if the item is silent.
        if (string.IsNullOrEmpty(anim)) anim = "axehit";

        _capi.World.Player.Entity.StartAnimation(anim);
        _capi.World.RegisterCallback((_) => _capi.World.Player.Entity.StopAnimation(anim), 400);

        _capi.World.PlaySoundAt(new AssetLocation("game:sounds/block/chop2"), pos.X, pos.Y, pos.Z, _capi.World.Player);

        _channel.SendPacket(new ToolActionPacket()
        {
            Pos = pos,
            VoxelX = x,
            VoxelY = y,
            VoxelZ = z,
            FaceIndex = sel.Face.Index,
            ToolMode = mode,
        });
    }

}