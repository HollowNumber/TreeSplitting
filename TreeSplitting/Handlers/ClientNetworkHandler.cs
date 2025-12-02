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
    private IClientNetworkChannel _channel;


    public ClientNetworkHandler(ICoreClientAPI capi)
    {
        _capi = capi;

        _channel = capi.Network.GetChannel("treesplitting");

        capi.Event.MouseDown += OnMouseDown;
    }

    private void OnMouseDown(MouseEvent e)
    {
        if (e.Button != EnumMouseButton.Left) return;

        BlockSelection sel = _capi.World.Player.CurrentBlockSelection;
        if (sel is null) return;

        Block block = _capi.World.BlockAccessor.GetBlock(sel.Position);

        if (block is not (BlockChoppingBlockTop or BlockChoppingBlock))
            return;

        ItemStack heldItemStack = _capi.World.Player.InventoryManager.ActiveHotbarSlot.Itemstack;

        if (heldItemStack?.Item?.Tool is not (EnumTool.Axe or EnumTool.Saw or EnumTool.Chisel)) return;
        
        BlockPos pos = sel.Position;
        
        if (block is BlockChoppingBlockTop) pos = pos.DownCopy();
        
        BEChoppingBlock be = _capi.World.BlockAccessor.GetBlockEntity<BEChoppingBlock>(pos);

        if (be is null) return;

        if (sel.SelectionBoxIndex <= 0 || sel.SelectionBoxIndex >= be.SelectionBoxes.Length) return;
        e.Handled = true;

        // Calculate voxel coords

        Cuboidf box = be.SelectionBoxes[sel.SelectionBoxIndex];

        int x = (int)(box.X1 * 16);
        int y = (int)((box.Y1 * 16) - 10);
        int z = (int)(box.Z1 * 16);

        int mode = heldItemStack.Attributes.GetInt("toolMode");

        _channel.SendPacket(new ToolActionPacket()
        {
            Pos = pos,
            VoxelX = x,
            VoxelY = y,
            VoxelZ = z,
            FaceIndex = sel.Face.Index,
            ToolMode = (EnumToolMode)mode,
        });
    }
}