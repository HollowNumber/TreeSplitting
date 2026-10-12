using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TreeSplitting.BlockEntities;
using TreeSplitting.Blocks;
using TreeSplitting.Handlers;
using TreeSplitting.Item;
using TreeSplitting.Network;
using TreeSplitting.Recipes;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

// TODO: Add localisation with the en.json file.
// TODO: Add WorldInteractions to the ChoppingBlock.

namespace TreeSplitting;

public class TreeSplittingModSystem : ModSystem
{
    //public static TreeSplittingConfig Config;

    /// <summary>
    /// Hewing recipes for this side. Kept per mod system instance so the client and server
    /// of a singleplayer world don't share (and race on) one list.
    /// </summary>
    public List<HewingRecipe> Recipes { get; private set; } = new();

    private ClientNetworkHandler? _clientNetworkHandler;
    private ServerNetworkHandler? _serverNetworkHandler;
    private ICoreAPI? api;

    private Harmony? patcher;

    public static List<HewingRecipe> GetRecipes(ICoreAPI api)
    {
        return api.ModLoader.GetModSystem<TreeSplittingModSystem>()?.Recipes ?? new List<HewingRecipe>();
    }

    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        this.api = api;

        api.Network.RegisterChannel("treesplitting").RegisterMessageType<ToolActionPacket>()
            .RegisterMessageType<RecipeSelectPacket>();

        string modid = Mod.Info.ModID;


        if (!Harmony.HasAnyPatches(modid))
        {
            patcher = new Harmony(modid);

            patcher.PatchCategory(modid);
        }

        api.RegisterBlockEntityClass(modid + ".choppingblockentity", typeof(BEChoppingBlock));
        api.RegisterBlockClass(modid + ".choppingblocktop", typeof(BlockChoppingBlockTop));
        api.RegisterBlockClass(modid + ".choppingblock", typeof(BlockChoppingBlock));

        api.RegisterItemClass(modid + ".itemsaw", typeof(ItemSaw));

    }

    // Assets must not be read in Start(); AssetsLoaded is the earliest stage they are guaranteed to be available.
    public override void AssetsLoaded(ICoreAPI api)
    {
        base.AssetsLoaded(api);

        LoadRecipes(api);
    }

    // Blocks and items are registered by now, so recipe ingredients/outputs can be resolved.
    public override void AssetsFinalize(ICoreAPI api)
    {
        base.AssetsFinalize(api);

        foreach (var recipe in Recipes)
        {
            recipe.Resolve(api.World);
        }
    }


    public override void Dispose()
    {
        base.Dispose();

        patcher?.UnpatchAll(Mod.Info.ModID);
    }

    private void LoadRecipes(ICoreAPI api)
    {
        var recipes = api.Assets.GetMany<HewingRecipe>(api.Logger, "recipes/hewing");

        Recipes = recipes
            .Select(entry =>
            {
                HewingRecipe recipe = entry.Value;
                recipe.Code = entry.Key;
                recipe.ParsePattern();
                return recipe;
            })
            // Deduplicate
            .GroupBy(r => r.Code)
            .Select(g => g.First())
            .ToList();

        api.Logger.Notification("Loaded {0} tree splitting recipes", Recipes.Count);
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);

        _clientNetworkHandler = new ClientNetworkHandler(api);
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        base.StartServerSide(api);

        _serverNetworkHandler = new ServerNetworkHandler(api);
    }

}
