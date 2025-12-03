using System.Collections.Generic;
using HarmonyLib;
using TreeSplitting.BlockEntities;
using TreeSplitting.Blocks;
using TreeSplitting.Item;
using TreeSplitting.Recipes;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

// TODO: Add localisation with the en.json file.
// TODO: Add WorldInteractions to the ChoppingBlock. 

namespace TreeSplitting;

public class TreeSplittingModSystem : ModSystem
{
    //public static TreeSplittingConfig Config;

    private ICoreAPI api;
    
    private Harmony patcher;

    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        this.api = api;


        // Load Assets


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


}