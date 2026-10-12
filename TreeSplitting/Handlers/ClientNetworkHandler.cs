using TreeSplitting.BlockEntities;
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

    // Hold to chop, like the anvil. Started by a click on the log, keeps swinging while the button is held
    private bool _holding;
    private long _lastSwingMs;


    public ClientNetworkHandler(ICoreClientAPI capi)
    {
        _capi = capi;

        _channel = capi.Network.GetChannel("treesplitting");

        capi.Event.MouseDown += OnMouseDown;
        capi.Event.MouseUp += OnMouseUp;
        capi.Event.RegisterGameTickListener(OnTick, 50);
    }

    private void OnMouseDown(MouseEvent e)
    {
        if (e.Button != EnumMouseButton.Left) return;

        SwingResult result = TrySwing();
        if (result == SwingResult.None) return;

        // If you miss the log, don't let the game break the chopping block instead
        e.Handled = true;

        if (result == SwingResult.Swung)
        {
            _holding = true;
            _lastSwingMs = _capi.ElapsedMilliseconds;
        }
    }

    private void OnMouseUp(MouseEvent e)
    {
        if (e.Button == EnumMouseButton.Left) _holding = false;
    }

    private void OnTick(float dt)
    {
        if (!_holding) return;

        // Let go, or tabbed into a menu
        if (!_capi.Input.MouseButton.Left || !_capi.Input.MouseGrabbed)
        {
            _holding = false;
            return;
        }

        int interval = TreeSplittingModSystem.GetConfig(_capi).SwingIntervalMs;
        if (_capi.ElapsedMilliseconds - _lastSwingMs < interval) return;

        // Keep holding even if you look away for a moment, it just won't swing until you're back on the log
        if (TrySwing() == SwingResult.Swung) _lastSwingMs = _capi.ElapsedMilliseconds;
    }

    private enum SwingResult
    {
        None,       // not a log we can work, leave the click alone
        Blocked,    // looking at a log but not a voxel, eat the click
        Swung
    }

    private SwingResult TrySwing()
    {
        // We assume that if the mouse isn't grabbed the player is in some ui and therefore we dont interact.
        if (!_capi.Input.MouseGrabbed) return SwingResult.None;

        BlockSelection sel = _capi.World.Player.CurrentBlockSelection;
        if (sel is null) return SwingResult.None;

        BEChoppingBlock? be = BEChoppingBlock.AtPosition(_capi.World.BlockAccessor, sel.Position);
        if (be is null) return SwingResult.None;

        ItemSlot activeSlot = _capi.World.Player.InventoryManager.ActiveHotbarSlot;
        ItemStack heldItemStack = activeSlot.Itemstack;

        EnumTool? tool = heldItemStack?.Collectible?.Tool;
        if (heldItemStack is null || tool is not (EnumTool.Axe or EnumTool.Saw or EnumTool.Chisel or EnumTool.Knife)) return SwingResult.None;

        if (tool is EnumTool.Chisel)
        {
            if (_capi.World.Player.InventoryManager.OffhandHotbarSlot.Itemstack?.Collectible?.Tool is not EnumTool.Hammer)
            {
                //The game does this automatically _capi.TriggerIngameError(new AssetLocation("treesplitting"), "chiselhammer", "You need to hold a hammer in your offhand to chisel wood voxels.");
                return SwingResult.None;
            }

        }

        Vec3i? voxel = be.GetSelectedVoxel(sel.SelectionBoxIndex);
        if (voxel is null) return be.WorkItemStack != null ? SwingResult.Blocked : SwingResult.None;

        BlockPos pos = be.Pos;
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
            VoxelX = voxel.X,
            VoxelY = voxel.Y,
            VoxelZ = voxel.Z,
            FaceIndex = sel.Face.Index,
            ToolMode = mode,
        });

        return SwingResult.Swung;
    }

}
