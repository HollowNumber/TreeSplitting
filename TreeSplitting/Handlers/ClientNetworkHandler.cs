using TreeSplitting.BlockEntities;
using TreeSplitting.Enums;
using TreeSplitting.Logic;
using TreeSplitting.Network;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace TreeSplitting.Handlers;

public class ClientNetworkHandler
{
    private readonly ICoreClientAPI _capi;
    private readonly IClientNetworkChannel _channel;

    // Hold to chop, like the anvil. Started by a click on the log, keeps swinging while the button is held
    private bool _holding;
    private long _lastSwingMs;

    // Heavy hits swing slower when holding down attack
    private const float HeavyHitSwingMultiplier = 1.6f;

    // One loop of the hammerandchisel animation, 20 frames at 30 fps
    private const int ChiselLoopMs = 700;
    private string? _swingAnim;
    private long _swingAnimStopAt;


    public ClientNetworkHandler(ICoreClientAPI capi)
    {
        _capi = capi;

        _channel = capi.Network.GetChannel("treesplitting")
            .SetMessageHandler<ConfigSyncPacket>(OnConfigSync);

        capi.Event.MouseDown += OnMouseDown;
        capi.Event.MouseUp += OnMouseUp;
        capi.Event.RegisterGameTickListener(OnTick, 50);
    }

    // Go by the server's settings, not the ones in this player's own config file
    private void OnConfigSync(ConfigSyncPacket packet)
    {
        TreeSplittingModSystem.GetConfig(_capi).HitsPerPlane = packet.HitsPerPlane;
    }

    private void OnMouseDown(MouseEvent e)
    {
        if (e.Button != EnumMouseButton.Left) return;

        SwingResult result = TrySwing();
        if (result == SwingResult.None) return;

        // If the swing misses the log, don't let the game break the chopping block instead
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

        var config = TreeSplittingModSystem.GetConfig(_capi);
        int interval = config.SwingIntervalMs;

        // Heavy hits swing slower
        ItemStack? held = _capi.World.Player.InventoryManager.ActiveHotbarSlot.Itemstack;
        if (held?.Collectible?.Tool == EnumTool.Axe &&
            held.Attributes.GetInt("toolMode") == (int)EnumAxeToolModes.HeavyHit)
        {
            interval = (int)(interval * HeavyHitSwingMultiplier);
        }

        if (_capi.ElapsedMilliseconds - _lastSwingMs < interval) return;

        // Keep holding even when looking away for a moment, it just won't swing until the log is targeted again
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

        // The chisel gets the hammer and chisel loop, same one the anvil work uses
        string anim = tool == EnumTool.Chisel ? "hammerandchisel" : heldItemStack.Collectible.GetHeldTpHitAnimation(activeSlot, _capi.World.Player.Entity);

        // So we default to "axehit" if the item is silent.
        if (string.IsNullOrEmpty(anim)) anim = "axehit";

        PlaySwingAnimation(anim, tool == EnumTool.Chisel ? ChiselLoopMs : 400);

        var config = TreeSplittingModSystem.GetConfig(_capi);
        if (tool == EnumTool.Axe)
        {
            // Run the same cut on a copy so the right sound plays straight away instead of after the server answers.
            // The server plays it for everyone else
            var player = _capi.World.Player.Entity.Pos;
            EnumChopResult chop = AxeCut.Cut((byte[,,])be.Voxels.Clone(), voxel,
                player.X - (pos.X + 0.5), player.Z - (pos.Z + 0.5), (EnumAxeToolModes)mode, config);
            ChopSounds.Play(_capi.World, pos, chop, _capi.World.Player);
        }
        else
        {
            ChopSounds.Play(_capi.World, pos, ChopSounds.ForTool(tool.Value, _capi.World), _capi.World.Player);
        }

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

    // Keeps the animation going while swings keep coming instead of stopping it halfway through every loop
    private void PlaySwingAnimation(string anim, int durationMs)
    {
        var entity = _capi.World.Player.Entity;
        if (_swingAnim != null && _swingAnim != anim) entity.StopAnimation(_swingAnim);

        _swingAnim = anim;
        entity.StartAnimation(anim);
        _swingAnimStopAt = _capi.World.ElapsedMilliseconds + durationMs;

        _capi.World.RegisterCallback(_ =>
        {
            if (_swingAnim != anim || _capi.World.ElapsedMilliseconds < _swingAnimStopAt) return;
            entity.StopAnimation(anim);
            _swingAnim = null;
        }, durationMs);
    }
}
