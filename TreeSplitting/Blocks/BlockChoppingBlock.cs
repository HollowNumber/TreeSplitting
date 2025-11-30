using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using TreeSplitting.BlockEntities; // Makes sure we can see your BE class

namespace TreeSplitting.Blocks
{
    public class BlockChoppingBlock : Block
    {
        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            // 1. Get the Block Entity
            BEChoppingBlock? be = world.BlockAccessor.GetBlockEntity(blockSel.Position) as BEChoppingBlock;
            
            // If something is wrong (no BE), just do default behavior
            if (be == null) return base.OnBlockInteractStart(world, byPlayer, blockSel);

            // 2. Route the Interaction based on WHAT was clicked
            // blockSel.SelectionBoxIndex comes from the array we define in GetSelectionBoxes below.
            
            // Index 0 is ALWAYS the "Base Block" (The Stump)
            if (blockSel.SelectionBoxIndex == 0)
            {
                // Delegate "Stump" interactions (Place Log, Take Log) to the BE helper
                if (be.OnInteract(byPlayer, blockSel)) return true;
            }
            else
            {
                // Index > 0 is a "Voxel" (The Log)
                // We need to tell the BE *which* voxel was clicked.
                
                if (blockSel.SelectionBoxIndex < be.SelectionBoxes.Length)
                {
                    Cuboidf? box = be.SelectionBoxes[blockSel.SelectionBoxIndex];
                    
                    // Reverse engineer the Voxel Coordinate (Vec3i) from the Selection Box
                    // In the BE, boxes are created as: x/16f, y/16f, z/16f
                    // So we multiply by 16 to get the integer coordinate back.
                    if (box != null)
                    {
                        int x = (int)(box.X1 * 16);
                        int y = (int)(box.Y1 * 16);
                        int z = (int)(box.Z1 * 16);
                        
                        Vec3i voxelPos = new Vec3i(x, y, z);

                        // Trigger the "Chop" logic in the BE
                        be.OnUseOver(byPlayer, voxelPos, blockSel.Face);
                        
                        // Return true so the game knows "We handled this click, don't do anything else"
                        return true;
                    }
                }
            }

            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }


        // ====================================================================
        // 2. SELECTION BOXES (Wireframes)
        // ====================================================================
        // This merges the "Stump" box with the dynamic "Log" boxes so the player can look at them.
        public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            BEChoppingBlock? be = blockAccessor.GetBlockEntity(pos) as BEChoppingBlock;
            
            // If we have a valid BE and it has active voxels...
            if (be != null && be.SelectionBoxes.Length > 0)
            {
                // 1. Create a copy of the BE's boxes
                // We clone it so we don't accidentally modify the BE's stored data
                Cuboidf[] combinedBoxes = (Cuboidf[])be.SelectionBoxes.Clone();

                // 2. Fill in Index 0 (The Stump)
                // The BE intentionally leaves index 0 as null.
                // We fetch the "Default" shape (defined in your blocktype JSON) to fill it.
                Cuboidf[] stumpBoxes = base.GetSelectionBoxes(blockAccessor, pos);
                
                if (stumpBoxes.Length > 0)
                {
                    combinedBoxes[0] = stumpBoxes[0];
                }
                else
                {
                    // Fallback if JSON has no shape (shouldn't happen)
                    combinedBoxes[0] = Cuboidf.Default();
                }

                return combinedBoxes;
            }

            // Fallback: If no log is present, just show the Stump box
            return base.GetSelectionBoxes(blockAccessor, pos);
        }
    }
}