using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TreeSplitting.BlockEntities;
using TreeSplitting.Blocks;
using TreeSplitting.Config;
using TreeSplitting.Network;
using TreeSplitting.Utils;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace TreeSplitting;

public class TreeSplittingModSystem : ModSystem
{
    //public static TreeSplittingConfig Config;
    public static List<HewingRecipe> Recipes = new();

    private IClientNetworkChannel clientChannel;
    private IServerNetworkChannel serverChannel;
    private ICoreAPI api;
    
    private Harmony patcher;

    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        this.api = api;

        api.Network.RegisterChannel("treesplitting").RegisterMessageType<ChopPacket>()
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

        clientChannel = api.Network.GetChannel("treesplitting");

        api.Event.MouseDown += OnClientMouseDown;
    }



    public override void StartServerSide(ICoreServerAPI api)
    {
        base.StartServerSide(api);

        serverChannel = api.Network.GetChannel("treesplitting").SetMessageHandler<ChopPacket>(OnServerChopPacket)
            .SetMessageHandler<RecipeSelectPacket>(OnServerRecipePacket);
    }

    private void OnServerRecipePacket(IServerPlayer fromPlayer, RecipeSelectPacket packet)
    {
        if (fromPlayer.Entity.Pos.SquareDistanceTo(packet.Pos.ToVec3d()) > 100) return;

        BEChoppingBlock be = fromPlayer.Entity.World.BlockAccessor.GetBlockEntity(packet.Pos) as BEChoppingBlock;
        if (be != null)
        {
            be.SetSelectedRecipe(new AssetLocation(packet.RecipeCode));
        }
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

    private void OnClientMouseDown(MouseEvent args)
    {
        if (args.Button != EnumMouseButton.Left) return;

        ICoreClientAPI capi = (ICoreClientAPI)api;
        if (capi.World.Player == null) return;

        // 2. Raycast
        BlockSelection sel = capi.World.Player.CurrentBlockSelection;
        if (sel == null) return;

        Block block = capi.World.BlockAccessor.GetBlock(sel.Position);

        // 3. Is it our block?
        if (block is BlockChoppingBlock || block is BlockChoppingBlockTop)
        {
            // 4. Check Axe
            ItemStack held = capi.World.Player.InventoryManager.ActiveHotbarSlot.Itemstack;
            if (held?.Item?.Tool != EnumTool.Axe) return;

            BlockPos bePos = sel.Position;
            if (block is BlockChoppingBlockTop) bePos = bePos.DownCopy();

            BEChoppingBlock be = capi.World.BlockAccessor.GetBlockEntity(bePos) as BEChoppingBlock;
            if (be == null) return;

            // Index 0 is Stump. We let vanilla break stump if they hit it.
            if (sel.SelectionBoxIndex > 0 && sel.SelectionBoxIndex < be.SelectionBoxes.Length)
            {
                args.Handled = true;

                // Calculate Voxel Coords locally
                Cuboidf box = be.SelectionBoxes[sel.SelectionBoxIndex];
                int x = (int)(box.X1 * 16);
                int y = (int)((box.Y1 * 16) - 10);
                int z = (int)(box.Z1 * 16);

                // Get Tool Mode
                int modeIndex = held.Attributes.GetInt("toolMode");
                

                capi.Logger.Debug("Tool Mode: {0}", modeIndex);
                
                // Send Packet
                clientChannel.SendPacket(new ChopPacket()
                {
                    Pos = bePos,
                    VoxelX = x,
                    VoxelY = y,
                    VoxelZ = z,
                    FaceIndex = sel.Face.Index,
                    ToolMode = (EnumToolMode)modeIndex
                });
               

                AnimationMetaData AnimationMetaData = new AnimationMetaData()
                {
                    Code = "axechop",
                    Animation = "axechop",
                    AnimationSpeed = 1f,
                    Weight = 10,
                    BlendMode = EnumAnimationBlendMode.Average,
                    EaseInSpeed = 999f, // Start immediately
                    EaseOutSpeed = 999f, // End immediately
                    TriggeredBy = new AnimationTrigger()
                    {
                        OnControls = new[] { EnumEntityActivity.Idle },
                        MatchExact = false
                    }

                }.Init();
                
                capi.World.Player.Entity.AnimManager.StartAnimation(AnimationMetaData);

                capi.World.RegisterCallback((dt) => { capi.World.Player.Entity.AnimManager.StopAnimation(AnimationMetaData.Code); },
                    500);
                
                capi.World.PlaySoundAt(new AssetLocation("game:sounds/block/chop2"), bePos.X, bePos.Y, bePos.Z);
                
            }
        }
    }
}