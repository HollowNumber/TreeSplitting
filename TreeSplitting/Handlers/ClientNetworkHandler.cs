using TreeSplitting.BlockEntities;
using TreeSplitting.Enums;
using TreeSplitting.Logic;
using TreeSplitting.Network;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
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

    // Saw between marks: the block, the first corner and its face, then both corners once the second is set
    private BlockPos? _markPos;
    private Vec3i? _markA;
    private int _markFace;
    private SawMarks? _marks;


    public ClientNetworkHandler(ICoreClientAPI capi)
    {
        _capi = capi;

        _channel = capi.Network.GetChannel("treesplitting")
            .SetMessageHandler<ConfigSyncPacket>(OnConfigSync)
            .SetMessageHandler<SawMarksPacket>(OnSawMarksPacket);

        capi.Event.MouseDown += OnMouseDown;
        capi.Event.MouseUp += OnMouseUp;
        capi.Event.RegisterGameTickListener(OnTick, 50);
    }

    // Go by the server's settings, not the ones in this player's own config file
    private void OnConfigSync(ConfigSyncPacket packet)
    {
        TreeSplittingModSystem.GetConfig(_capi).HitsPerPlane = packet.HitsPerPlane;
    }

    // The server's done with the marks (all sawn out, or the log moved)
    private void OnSawMarksPacket(SawMarksPacket packet)
    {
        if (packet.Clear && packet.Pos.Equals(_markPos)) ClearMarks(false);
    }

    private void ClearMarks(bool tellServer)
    {
        if (_markPos == null) return;

        BEChoppingBlock.AtPosition(_capi.World.BlockAccessor, _markPos)?.ShowSawMarks(null);
        if (tellServer) _channel.SendPacket(new SawMarksPacket { Pos = _markPos, Clear = true });

        _markPos = null;
        _markA = null;
        _marks = null;
    }

    private void OnMouseDown(MouseEvent e)
    {
        if (e.Button != EnumMouseButton.Left) return;

        SwingResult result = TrySwing(true);
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
        if (_markPos != null)
        {
            ItemStack? saw = _capi.World.Player.InventoryManager.ActiveHotbarSlot.Itemstack;
            if (saw?.Collectible?.Tool != EnumTool.Saw || saw.Attributes.GetInt("toolMode") != (int)EnumSawToolModes.SawBetweenMarks) ClearMarks(true);
        }

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
        if (TrySwing(false) == SwingResult.Swung) _lastSwingMs = _capi.ElapsedMilliseconds;
    }

    private enum SwingResult
    {
        None,       // not a log we can work, leave the click alone
        Blocked,    // looking at a log but not a voxel, eat the click
        Swung
    }

    private SwingResult TrySwing(bool fresh)
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

        var config = TreeSplittingModSystem.GetConfig(_capi);

        bool marking = tool == EnumTool.Saw && mode == (int)EnumSawToolModes.SawBetweenMarks;
        if (_markPos != null && (!marking || !_markPos.Equals(pos))) ClearMarks(true);

        if (marking)
        {
            // Sneak + click drops the marks
            if (_capi.World.Player.Entity.Controls.ShiftKey)
            {
                if (fresh) ClearMarks(true);
                return SwingResult.Blocked;
            }

            // Marks only get set on a fresh click, holding the button doesn't drop a second one on the same spot
            if (_marks == null)
            {
                if (!fresh) return SwingResult.Blocked;
                return SetMark(be, pos, voxel, sel.Face);
            }
        }

        // The chisel gets the hammer and chisel loop, same one the anvil work uses, the marked saw saws back and forth
        string anim = marking ? "saw"
            : tool == EnumTool.Chisel ? "hammerandchisel"
            : heldItemStack.Collectible.GetHeldTpHitAnimation(activeSlot, _capi.World.Player.Entity);

        // So we default to "axehit" if the item is silent.
        if (string.IsNullOrEmpty(anim)) anim = "axehit";

        if (marking) PlaySwingAnimation(anim, config.SwingIntervalMs + 150, SawLoopMs / (float)config.SwingIntervalMs);
        else PlaySwingAnimation(anim, tool == EnumTool.Chisel ? ChiselLoopMs : 400);
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

    private SwingResult SetMark(BEChoppingBlock be, BlockPos pos, Vec3i voxel, BlockFacing face)
    {
        if (_markA == null)
        {
            _markPos = pos.Copy();
            _markA = voxel;
            _markFace = face.Index;
            be.ShowSawMarks(new SawMarks(voxel, voxel, face));
            _capi.TriggerIngameError(this, "sawmarks-second", Lang.Get("treesplitting:sawmarks-hint-second"));
            return SwingResult.Blocked;
        }

        if (face.Index != _markFace)
        {
            _capi.TriggerIngameError(this, "sawmarksface", Lang.Get("treesplitting:ingameerror-sawmarks-face"));
            return SwingResult.Blocked;
        }

        SawMarks marks = new SawMarks(_markA, voxel, face);
        if (!marks.StartsAtOpenEdge(be.Voxels))
        {
            _capi.TriggerIngameError(this, "sawmarksopen", Lang.Get("treesplitting:ingameerror-sawmarks-open"));
            return SwingResult.Blocked;
        }

        _marks = marks;
        be.ShowSawMarks(_marks);
        _capi.TriggerIngameError(this, "sawmarks-saw", Lang.Get("treesplitting:sawmarks-hint-saw"));
        _channel.SendPacket(new SawMarksPacket
        {
            Pos = pos, FaceIndex = face.Index,
            X1 = _markA.X, Y1 = _markA.Y, Z1 = _markA.Z,
            X2 = voxel.X, Y2 = voxel.Y, Z2 = voxel.Z
        });
        return SwingResult.Blocked;
    }

    // One loop of the vanilla saw animation, 30 frames at 30 fps
    private const int SawLoopMs = 1000;

    // Keeps the animation going while swings keep coming instead of stopping it halfway through every loop
    private void PlaySwingAnimation(string anim, int durationMs, float speed = 1f)
    {
        var entity = _capi.World.Player.Entity;
        if (_swingAnim != null && _swingAnim != anim) entity.StopAnimation(_swingAnim);

        _swingAnim = anim;
        // Sped up so one loop is one stroke
        if (speed != 1f && entity.Properties.Client.AnimationsByMetaCode.TryGetValue(anim, out AnimationMetaData? meta))
        {
            AnimationMetaData faster = meta.Clone();
            faster.AnimationSpeed = speed;
            entity.AnimManager.StartAnimation(faster);
        }
        else entity.StartAnimation(anim);
        _swingAnimStopAt = _capi.World.ElapsedMilliseconds + durationMs;

        _capi.World.RegisterCallback(_ =>
        {
            if (_swingAnim != anim || _capi.World.ElapsedMilliseconds < _swingAnimStopAt) return;
            entity.StopAnimation(anim);
            _swingAnim = null;
        }, durationMs);
    }
}
