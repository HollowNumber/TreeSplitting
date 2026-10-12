using System.Collections.Generic;
using TreeSplitting.BlockEntities;
using TreeSplitting.Network;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace TreeSplitting.Handlers;

public class ServerNetworkHandler
{
    private readonly ICoreServerAPI _sapi;
    private IServerNetworkChannel _channel;

    // Holding attack swings on a timer, so ignore anyone swinging way faster than that
    private const long MinSwingGapMs = 150;
    private readonly Dictionary<string, long> _lastSwingMs = new();

    public ServerNetworkHandler(ICoreServerAPI sapi)
    {
        _sapi = sapi;

        _channel = sapi.Network.GetChannel("treesplitting")
            .SetMessageHandler<ToolActionPacket>(OnServerToolActionPacket)
            .SetMessageHandler<RecipeSelectPacket>(OnServerRecipePacket);

        sapi.Event.PlayerDisconnect += player => _lastSwingMs.Remove(player.PlayerUID);
    }

    private void OnServerRecipePacket(IServerPlayer fromPlayer, RecipeSelectPacket packet)
    {
        if (fromPlayer.Entity.Pos.SquareDistanceTo(packet.Pos.ToVec3d()) > 100) return;

        BEChoppingBlock be = fromPlayer.Entity.World.BlockAccessor.GetBlockEntity<BEChoppingBlock>(packet.Pos);

        if (be is null) return;

        be.SetSelectedRecipe(new AssetLocation(packet.RecipeCode));
    }

    private void OnServerToolActionPacket(IServerPlayer fromPlayer, ToolActionPacket packet)
    {
        if (fromPlayer.Entity.Pos.SquareDistanceTo(packet.Pos.ToVec3d()) > 100) return;

        BEChoppingBlock be = fromPlayer.Entity.World.BlockAccessor.GetBlockEntity<BEChoppingBlock>(packet.Pos);

        if (be is null) return;
        if (packet.FaceIndex < 0 || packet.FaceIndex >= BlockFacing.ALLFACES.Length) return;

        long now = _sapi.World.ElapsedMilliseconds;
        if (_lastSwingMs.TryGetValue(fromPlayer.PlayerUID, out long last) && now - last < MinSwingGapMs) return;
        _lastSwingMs[fromPlayer.PlayerUID] = now;

        be.OnUseOver(fromPlayer, new Vec3i(packet.VoxelX, packet.VoxelY, packet.VoxelZ),
            BlockFacing.ALLFACES[packet.FaceIndex], packet.ToolMode);
    }
}