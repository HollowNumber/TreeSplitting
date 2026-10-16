using TreeSplitting.Item;
using TreeSplitting.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace TreeSplitting.BlockEntities;

/// <summary>
/// A partly hewn log put down in the world. Holds the item it came from, so breaking it gives back exactly that.
/// </summary>
public class BEHewnLogPlaced : BlockEntity
{
    public ItemStack? Piece { get; private set; }

    // Box around whatever wood is left, good enough to click and stand on
    public Cuboidf[] Boxes { get; private set; } = [new Cuboidf(0, 0, 0, 1, 1, 1)];

    private MeshData? mesh;

    public void SetPiece(ItemStack piece)
    {
        Piece = piece;
        Piece.StackSize = 1;
        Refresh();
        MarkDirty(true);
    }

    private void Refresh()
    {
        mesh = null;
        byte[,,]? voxels = Piece == null ? null : ItemHewnLog.GetVoxels(Piece);
        if (voxels == null) return;

        int x1 = 16, y1 = 16, z1 = 16, x2 = 0, y2 = 0, z2 = 0;
        for (int x = 0; x < 16; x++)
        for (int y = 0; y < 16; y++)
        for (int z = 0; z < 16; z++)
        {
            if (voxels[x, y, z] == 0) continue;
            x1 = System.Math.Min(x1, x); y1 = System.Math.Min(y1, y); z1 = System.Math.Min(z1, z);
            x2 = System.Math.Max(x2, x + 1); y2 = System.Math.Max(y2, y + 1); z2 = System.Math.Max(z2, z + 1);
        }
        if (x1 < x2) Boxes = [new Cuboidf(x1 / 16f, y1 / 16f, z1 / 16f, x2 / 16f, y2 / 16f, z2 / 16f)];
    }

    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
    {
        if (Api is not ICoreClientAPI capi) return base.OnTesselation(mesher, tessThreadTesselator);
        // Draw nothing until the shape arrives, otherwise a plain wood cube flashes up first
        if (Piece == null) return true;

        if (mesh == null)
        {
            byte[,,]? voxels = ItemHewnLog.GetVoxels(Piece);
            ItemStack? log = ItemHewnLog.GetLog(Piece, capi.World);
            if (voxels == null || log?.Collectible == null) return base.OnTesselation(mesher, tessThreadTesselator);
            mesh = LogMesher.GenMesh(capi, log, voxels, ItemHewnLog.GrainAxis(Piece), 0);

            // The chunk mesher wants these for every face or it doesn't draw them
            for (int i = 0; i < mesh.VerticesCount / 4; i++)
            {
                mesh.AddXyzFace(0);
                mesh.AddColorMapIndex(0, 0);
                mesh.AddRenderPass((short)EnumChunkRenderPass.Opaque);
            }
        }

        mesher.AddMeshData(mesh);
        return true;
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        if (Piece != null) tree.SetItemstack("piece", Piece);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        Piece = tree.GetItemstack("piece");
        Piece?.ResolveBlockOrItem(worldAccessForResolve);
        Refresh();

        // Redraw with the new shape
        if (Api?.Side == EnumAppSide.Client) MarkDirty(true);
    }
}
