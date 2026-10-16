using System.Collections.Generic;
using TreeSplitting.BlockEntities;
using TreeSplitting.Logic;
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
            .SetMessageHandler<RecipeSelectPacket>(OnServerRecipePacket)
            .SetMessageHandler<SawMarksPacket>(OnServerSawMarksPacket);

        sapi.Event.PlayerDisconnect += player => _lastSwingMs.Remove(player.PlayerUID);

        // The server decides how the axe works, tell every player when they join
        sapi.Event.PlayerNowPlaying += player =>
        {
            var config = TreeSplittingModSystem.GetConfig(sapi);
            _channel.SendPacket(new ConfigSyncPacket
            {
                HitsPerPlane = config.HitsPerPlane
            }, player);
        };
    }

    private void OnServerRecipePacket(IServerPlayer fromPlayer, RecipeSelectPacket packet)
    {
        if (fromPlayer.Entity.Pos.SquareDistanceTo(packet.Pos.ToVec3d()) > 100) return;

        BEChoppingBlock be = fromPlayer.Entity.World.BlockAccessor.GetBlockEntity<BEChoppingBlock>(packet.Pos);

        if (be is null) return;

        be.SetSelectedRecipe(new AssetLocation(packet.RecipeCode));
    }

    private void OnServerSawMarksPacket(IServerPlayer fromPlayer, SawMarksPacket packet)
    {
        if (fromPlayer.Entity.Pos.SquareDistanceTo(packet.Pos.ToVec3d()) > 100) return;

        BEChoppingBlock be = fromPlayer.Entity.World.BlockAccessor.GetBlockEntity<BEChoppingBlock>(packet.Pos);
        if (be is null) return;

        if (packet.Clear)
        {
            be.SetSawMarks(fromPlayer, null);
            return;
        }

        bool InGrid(int v) => v is >= 0 and < 16;
        if (packet.FaceIndex < 0 || packet.FaceIndex >= BlockFacing.ALLFACES.Length) return;
        if (!InGrid(packet.X1) || !InGrid(packet.Y1) || !InGrid(packet.Z1) || !InGrid(packet.X2) || !InGrid(packet.Y2) || !InGrid(packet.Z2)) return;

        be.SetSawMarks(fromPlayer, new SawMarks(new Vec3i(packet.X1, packet.Y1, packet.Z1), new Vec3i(packet.X2, packet.Y2, packet.Z2),
            BlockFacing.ALLFACES[packet.FaceIndex]));
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