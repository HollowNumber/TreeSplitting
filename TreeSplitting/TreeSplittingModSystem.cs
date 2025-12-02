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
    public static List<HewingRecipe> Recipes = new();

    private ClientNetworkHandler _clientNetworkHandler;
    private ServerNetworkHandler _serverNetworkHandler;
    private ICoreAPI api;
    
    private Harmony patcher;

    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        this.api = api;

        api.Network.RegisterChannel("treesplitting").RegisterMessageType<ToolActionPacket>()
            .RegisterMessageType<RecipeSelectPacket>();

        // Load Assets

        LoadRecipes(api);

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


    public override void Dispose()
    {
        base.Dispose();
        
        patcher?.UnpatchAll(Mod.Info.ModID);
    }

    private static void LoadRecipes(ICoreAPI api)
    {
        //Recipes.Clear();

        var recipes = api.Assets.GetMany<HewingRecipe>(api.Logger, "recipes/hewing");
        

        foreach (var entry in recipes)
        {
            HewingRecipe recipe = entry.Value;
            recipe.Code = entry.Key;

            recipe.Resolve(api.World);
            Recipes.Add(recipe);
        }
        
        // Deduplicate
        Recipes = Recipes.GroupBy(r => r.Code).Select(g => g.First()).ToList();

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