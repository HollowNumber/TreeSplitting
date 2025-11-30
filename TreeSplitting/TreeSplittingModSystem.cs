using System.Collections.Generic;
using TreeSplitting.BlockEntities;
using TreeSplitting.Blocks;
using TreeSplitting.Config;
using TreeSplitting.Network;
using TreeSplitting.Utils;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace TreeSplitting;

public class TreeSplittingModSystem : ModSystem
{
    public static TreeSplittingConfig Config;
    public static List<HewingRecipe> Recipes = new();

    private IClientNetworkChannel clientChannel;
    private IServerNetworkChannel serverChannel;
    private ICoreAPI api;

    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        this.api = api;

        api.Network.RegisterChannel("treesplitting").RegisterMessageType<ChopPacket>();

        // Load Assets

        LoadRecipes(api);

        string modid = Mod.Info.ModID;

        api.RegisterBlockEntityClass(modid + ".choppingblockentity", typeof(BEChoppingBlock));
        api.RegisterBlockClass(modid + ".choppingblocktop", typeof(BlockChoppingBlockTop));
        api.RegisterBlockClass(modid + ".choppingblock", typeof(BlockChoppingBlock));
    }

    private static void LoadRecipes(ICoreAPI api)
    {
        var recipes = api.Assets.GetMany<HewingRecipe>(api.Logger, "recipes/hewing");

        foreach (var entry in recipes)
        {
            HewingRecipe recipe = entry.Value;
            recipe.Code = entry.Key;

            recipe.Resolve(api.World);
            Recipes.Add(recipe);
        }

        api.Logger.Notification("Loaded {0} tree splitting recipes", Recipes.Count);
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);

        api.Event.MouseDown += OnClientMouseDown;
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        base.StartServerSide(api);

        serverChannel = api.Network.GetChannel("treesplitting").SetMessageHandler<ChopPacket>(OnServerChopPacket);
    }

    private void OnServerChopPacket(IServerPlayer fromPlayer, ChopPacket packet)
    {
        if (fromPlayer.Entity.Pos.SquareDistanceTo(packet.Pos.ToVec3d()) > 100) return;

        BEChoppingBlock be = fromPlayer.Entity.World.BlockAccessor.GetBlockEntity(packet.Pos) as BEChoppingBlock;

        if (be != null)
        {
            be.OnUseOver(fromPlayer, new Vec3i(packet.VoxelX, packet.VoxelY, packet.VoxelZ),
                BlockFacing.ALLFACES[packet.FaceIndex], packet.ToolMode);
        }
    }

    private void OnClientMouseDown(MouseEvent e)
    {
    }
}