using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TreeSplitting.BlockEntities;
using TreeSplitting.Config;
using TreeSplitting.Blocks;
using TreeSplitting.Handlers;
using TreeSplitting.Item;
using TreeSplitting.Network;
using TreeSplitting.Recipes;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

// TODO: Add localisation with the en.json file.

namespace TreeSplitting;

public class TreeSplittingModSystem : ModSystem
{
    public TreeSplittingConfig Config { get; private set; } = new();

    public static TreeSplittingConfig GetConfig(ICoreAPI api)
    {
        return api.ModLoader.GetModSystem<TreeSplittingModSystem>()?.Config ?? new TreeSplittingConfig();
    }

    /// <summary>
    /// The server loads these, the recipe registry sends them to clients since they never load the recipes folder.
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

        LoadConfig(api);

        api.Network.RegisterChannel("treesplitting").RegisterMessageType<ToolActionPacket>()
            .RegisterMessageType<RecipeSelectPacket>()
            .RegisterMessageType<ConfigSyncPacket>()
            .RegisterMessageType<SawMarksPacket>();

        Recipes = api.RegisterRecipeRegistry<RecipeRegistryGeneric<HewingRecipe>>("treesplitting:hewingrecipes").Recipes;

        string modid = Mod.Info.ModID;


        if (!Harmony.HasAnyPatches(modid))
        {
            patcher = new Harmony(modid);

            patcher.PatchCategory(modid);
        }

        api.RegisterBlockEntityClass(modid + ".choppingblockentity", typeof(BEChoppingBlock));
        api.RegisterBlockEntityClass(modid + ".hewnlogplaced", typeof(BEHewnLogPlaced));
        api.RegisterBlockClass(modid + ".hewnlogplaced", typeof(BlockHewnLogPlaced));
        api.RegisterBlockClass(modid + ".choppingblocktop", typeof(BlockChoppingBlockTop));
        api.RegisterBlockClass(modid + ".choppingblock", typeof(BlockChoppingBlock));
        api.RegisterBlockBehaviorClass(modid + ".makechoppingblock", typeof(BlockBehaviorMakeChoppingBlock));

        api.RegisterItemClass(modid + ".itemsaw", typeof(ItemSaw));
        api.RegisterItemClass(modid + ".itemhewnlog", typeof(ItemHewnLog));

    }

    private void LoadConfig(ICoreAPI api)
    {
        try
        {
            Config = api.LoadModConfig<TreeSplittingConfig>(TreeSplittingConfig.FileName) ?? new TreeSplittingConfig();
        }
        catch (System.Exception e)
        {
            api.Logger.Error("Couldn't read {0}, using the defaults: {1}", TreeSplittingConfig.FileName, e.Message);
            Config = new TreeSplittingConfig();
        }

        // Write it back so new settings show up in the file
        api.StoreModConfig(Config, TreeSplittingConfig.FileName);
    }

    // Can't read assets in Start(), they might not be there yet
    public override void AssetsLoaded(ICoreAPI api)
    {
        base.AssetsLoaded(api);

        if (api.Side == EnumAppSide.Server) LoadRecipes(api);
    }

    // Blocks and items are loaded by now so we can resolve the recipes
    public override void AssetsFinalize(ICoreAPI api)
    {
        base.AssetsFinalize(api);

        if (api.Side != EnumAppSide.Server) return;

        foreach (var recipe in Recipes)
        {
            recipe.Resolve(api.World);
        }

        if (Config.DisableVanillaLogRecipes) RemoveVanillaLogRecipes(api);
    }

    // Grid recipes that turn logs into what the chopping block makes
    private static readonly string[] HewnOutputs = ["plank-", "firewood", "agedfirewood", "debarkedlog-", "supportbeam-"];

    private void RemoveVanillaLogRecipes(ICoreAPI api)
    {
        bool TakesLog(GridRecipe recipe) => recipe.Ingredients?.Values.Any(i =>
            i.Code?.Path is { } path && (path.StartsWith("log") || path.StartsWith("debarkedlog"))) == true;

        bool MakesHewnThing(GridRecipe recipe) => recipe.Output?.Code?.Path is { } path && HewnOutputs.Any(path.StartsWith);

        List<GridRecipe> removed = api.World.GridRecipes.Where(r => TakesLog(r) && MakesHewnThing(r)).ToList();
        api.World.GridRecipes.RemoveAll(removed.Contains);

        // The fast lookup might already know about them, take them out of there too
        foreach (var list in api.World.FastSearchRecipesByIngredient.Values) list.RemoveAll(r => r is GridRecipe g && removed.Contains(g));

        api.Logger.Notification("Turned off {0} vanilla log recipes, hewing makes those now", removed.Count);
    }


    public override void Dispose()
    {
        base.Dispose();

        patcher?.UnpatchAll(Mod.Info.ModID);
    }

    private void LoadRecipes(ICoreAPI api)
    {
        var recipes = api.Assets.GetMany<HewingRecipe>(api.Logger, "recipes/hewing");

        // Don't replace the list, the registry needs the same one or nothing gets synced
        Recipes.Clear();
        Recipes.AddRange(recipes
            .Select(entry =>
            {
                HewingRecipe recipe = entry.Value;
                recipe.Code = entry.Key;
                recipe.ParsePattern();
                return recipe;
            })
            // Deduplicate
            .GroupBy(r => r.Code)
            .Select(g => g.First()));

        api.Logger.Notification("Loaded {0} tree splitting recipes", Recipes.Count);
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);

        api.Event.LevelFinalize += () => api.Logger.Notification("Received {0} tree splitting recipes from the server", Recipes.Count);

        _clientNetworkHandler = new ClientNetworkHandler(api);
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        base.StartServerSide(api);

        _serverNetworkHandler = new ServerNetworkHandler(api);
    }

}
