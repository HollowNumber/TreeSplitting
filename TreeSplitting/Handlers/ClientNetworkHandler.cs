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
    private static readonly AssetLocation ChopSound = new("game:sounds/block/chop2");


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

        BEChoppingBlock? be = BEChoppingBlock.AtPosition(_capi.World.BlockAccessor, sel.Position);
        if (be is null) return;

        ItemSlot activeSlot = _capi.World.Player.InventoryManager.ActiveHotbarSlot;
        ItemStack heldItemStack = activeSlot.Itemstack;

        EnumTool? tool = heldItemStack?.Collectible?.Tool;
        if (heldItemStack is null || tool is not (EnumTool.Axe or EnumTool.Saw or EnumTool.Chisel or EnumTool.Knife)) return;

        if (tool is EnumTool.Chisel)
        {
            if (_capi.World.Player.InventoryManager.OffhandHotbarSlot.Itemstack?.Collectible?.Tool is not EnumTool.Hammer)
            {
                //The game does this automatically _capi.TriggerIngameError(new AssetLocation("treesplitting"), "chiselhammer", "You need to hold a hammer in your offhand to chisel wood voxels.");
                return;
            }

        }

        BlockPos pos = be.Pos;

        // If you miss the log, don't let the game break the chopping block instead
        if (be.WorkItemStack != null) e.Handled = true;

        Vec3i? voxel = be.GetSelectedVoxel(sel.SelectionBoxIndex);
        if (voxel is null) return;
        e.Handled = true;

        int x = voxel.X;
        int y = voxel.Y;
        int z = voxel.Z;

        int mode = heldItemStack.Attributes.GetInt("toolMode");

        string anim = heldItemStack.Collectible.GetHeldTpHitAnimation(activeSlot, _capi.World.Player.Entity);

        // So we default to "axehit" if the item is silent.
        if (string.IsNullOrEmpty(anim)) anim = "axehit";

        _capi.World.Player.Entity.StartAnimation(anim);
        _capi.World.RegisterCallback((_) => _capi.World.Player.Entity.StopAnimation(anim), 400);

        _capi.World.PlaySoundAt(ChopSound, pos.X, pos.Y, pos.Z, _capi.World.Player);

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