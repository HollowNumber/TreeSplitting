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

    public ServerNetworkHandler(ICoreServerAPI sapi)
    {
        _sapi = sapi;

        _channel = sapi.Network.GetChannel("treesplitting")
            .SetMessageHandler<ToolActionPacket>(OnServerToolActionPacket)
            .SetMessageHandler<RecipeSelectPacket>(OnServerRecipePacket);
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

        be.OnUseOver(fromPlayer, new Vec3i(packet.VoxelX, packet.VoxelY, packet.VoxelZ),
            BlockFacing.ALLFACES[packet.FaceIndex], packet.ToolMode);
    }
}