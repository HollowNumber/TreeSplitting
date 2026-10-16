using TreeSplitting.BlockEntities;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace TreeSplitting.Rendering;

/// <summary>
/// Builds the mesh for a log from its voxels. Used by the chopping block and by the partly hewn log item.
/// </summary>
public static class LogMesher
{
    private static readonly int WhiteColor = ColorUtil.ToRgba(255, 255, 255, 255);
    // Heartwood is a bit darker than the sapwood ring around it
    private static readonly int HeartwoodColor = ColorUtil.ToRgba(255, 215, 200, 185);

    /// <summary>
    /// yStart is how many voxels up the log starts, 10 on the chopping block (on top of the stump), 0 for the item
    /// </summary>
    public static MeshData GenMesh(ICoreClientAPI capi, ItemStack log, byte[,,] voxels, int grainAxis, float yStart)
    {
        // Start big enough for a full log so it doesn't keep resizing
        MeshData mesh = new MeshData(4096, 6144, false, true, true, true);

        CollectibleObject collectible = log.Collectible;
        ITexPositionSource texSource;
        if (collectible is Block block)
            texSource = capi.Tesselator.GetTextureSource(block);
        else
            texSource = capi.Tesselator.GetTextureSource((Vintagestory.API.Common.Item)collectible);

        // A missing key can come back as the unknown texture instead of null, so skip that too and try the next one
        TextureAtlasPosition unknown = capi.BlockTextureAtlas.UnknownTexturePosition;
        TextureAtlasPosition? Pick(params string[] keys)
        {
            foreach (string key in keys)
            {
                TextureAtlasPosition? tex = texSource[key];
                if (tex != null && tex != unknown) return tex;
            }
            return null;
        }

        // End grain with the rings for the ends of the log
        TextureAtlasPosition topTex = Pick("up", "top", "end", "all", "base", "texture") ?? unknown;

        // Bark for the outside. Logs have it on the sides, the half log calls it bark
        TextureAtlasPosition sideTex = Pick("bark", "north", "south", "east", "west", "side", "all", "base", "texture") ?? topTex;

        // Grain runs up (Y) for a standing log, along X for one lying down.
        // a and b are the axes across the log, the end grain texture is mapped u = a, v = b
        int grain = grainAxis;
        int axisA = grainAxis == 0 ? 2 : 0;
        int axisB = grainAxis == 1 ? 2 : 1;

        int[] voxel = new int[3];
        for (int x = 0; x < 16; x++)
        for (int y = 0; y < 16; y++)
        for (int z = 0; z < 16; z++)
        {
            byte material = voxels[x, y, z];
            if (material == 0) continue;
            voxel[0] = x; voxel[1] = y; voxel[2] = z;

            for (int face = 0; face < 6; face++)
            {
                int[] dir = FaceDirs[face];
                int nx = x + dir[0], ny = y + dir[1], nz = z + dir[2];
                // Only faces that touch air
                if (nx is >= 0 and < 16 && ny is >= 0 and < 16 && nz is >= 0 and < 16 && voxels[nx, ny, nz] != 0) continue;

                AddVoxelFace(mesh, voxel, face, material, grain, axisA, axisB, topTex, sideTex, yStart);
            }
        }

        return mesh;
    }

    // +X, -X, +Y, -Y, +Z, -Z
    private static readonly int[][] FaceDirs =
    [
        [1, 0, 0], [-1, 0, 0], [0, 1, 0], [0, -1, 0], [0, 0, 1], [0, 0, -1]
    ];


    /// <summary>
    /// Adds one face of a voxel and picks the texture for it:
    /// - faces looking down the grain (the log's ends) get the end grain with the rings
    /// - bark gets the bark texture running along the log
    /// - cut sides of the inner wood get the row of the end grain at that depth stretched along the grain, so the rings
    ///   show up as stripes that line up with the rings on the end, like real wood (end grain is 32px, so +0.25/+0.75 of
    ///   a voxel picks the texel right at the surface)
    /// </summary>
    private static void AddVoxelFace(MeshData mesh, int[] voxel, int face, byte material, int grain, int axisA, int axisB,
        TextureAtlasPosition endTex, TextureAtlasPosition barkTex, float yStart)
    {
        bool isBark = material == (byte)EnumWoodMaterial.Bark;
        int color = material == (byte)EnumWoodMaterial.Heartwood ? HeartwoodColor : WhiteColor;
        int[] dir = FaceDirs[face];
        int normal = dir[0] != 0 ? 0 : dir[1] != 0 ? 1 : 2;
        bool positive = dir[normal] > 0;
        int t1 = (normal + 1) % 3, t2 = (normal + 2) % 3;

        float[][] corners = [new float[3], new float[3], new float[3], new float[3]];
        float[] cornerU = new float[4];
        float[] cornerV = new float[4];

        // Corners in voxel units, (0,0) (1,0) (1,1) (0,1) across the face, counter clockwise seen from outside.
        // Faces looking the negative way go round the other way, the world culls back faces even if the chopping block doesn't
        for (int i = 0; i < 4; i++)
        {
            float[] c = corners[i];
            int k = positive ? i : (4 - i) % 4;
            c[normal] = voxel[normal] + (positive ? 1 : 0);
            c[t1] = voxel[t1] + (k is 1 or 2 ? 1 : 0);
            c[t2] = voxel[t2] + (k is 2 or 3 ? 1 : 0);
        }

        TextureAtlasPosition tex;
        if (normal == grain)
        {
            tex = endTex;
            for (int i = 0; i < 4; i++) { cornerU[i] = corners[i][axisA] / 16f; cornerV[i] = corners[i][axisB] / 16f; }
        }
        else if (isBark)
        {
            // Across the log on u, along the grain on v
            int across = normal == axisA ? axisB : axisA;
            tex = barkTex;
            for (int i = 0; i < 4; i++) { cornerU[i] = corners[i][across] / 16f; cornerV[i] = corners[i][grain] / 16f; }
        }
        else
        {
            tex = endTex;
            float pinned = (voxel[normal] + (positive ? 0.75f : 0.25f)) / 16f;
            for (int i = 0; i < 4; i++)
            {
                cornerU[i] = normal == axisA ? pinned : corners[i][axisA] / 16f;
                cornerV[i] = normal == axisB ? pinned : corners[i][axisB] / 16f;
            }
        }

        AddFaceUv(mesh, tex, color,
            corners[0][0] / 16f, (corners[0][1] + yStart) / 16f, corners[0][2] / 16f, cornerU[0], cornerV[0],
            corners[1][0] / 16f, (corners[1][1] + yStart) / 16f, corners[1][2] / 16f, cornerU[1], cornerV[1],
            corners[2][0] / 16f, (corners[2][1] + yStart) / 16f, corners[2][2] / 16f, cornerU[2], cornerV[2],
            corners[3][0] / 16f, (corners[3][1] + yStart) / 16f, corners[3][2] / 16f, cornerU[3], cornerV[3],
            VertexFlags.PackNormal(dir[0], dir[1], dir[2]));
    }

    /// <summary>
    /// One quad, with the uv (0-1 in the texture) given for each corner
    /// </summary>
    private static void AddFaceUv(MeshData mesh, TextureAtlasPosition tex, int color,
        float x0, float y0, float z0, float u0, float v0,
        float x1, float y1, float z1, float u1, float v1,
        float x2, float y2, float z2, float u2, float v2,
        float x3, float y3, float z3, float u3, float v3,
        int normalFlags)
    {
        int baseIndex = mesh.VerticesCount;
        float w = tex.x2 - tex.x1;
        float h = tex.y2 - tex.y1;

        mesh.AddVertex(x0, y0, z0, tex.x1 + u0 * w, tex.y1 + v0 * h, color);
        mesh.AddVertex(x1, y1, z1, tex.x1 + u1 * w, tex.y1 + v1 * h, color);
        mesh.AddVertex(x2, y2, z2, tex.x1 + u2 * w, tex.y1 + v2 * h, color);
        mesh.AddVertex(x3, y3, z3, tex.x1 + u3 * w, tex.y1 + v3 * h, color);
        mesh.AddTextureId(tex.atlasTextureId);

        if (mesh.Flags != null)
        {
            for (int i = 0; i < 4; i++) mesh.Flags[baseIndex + i] = normalFlags;
        }

        mesh.AddIndex(baseIndex + 0);
        mesh.AddIndex(baseIndex + 1);
        mesh.AddIndex(baseIndex + 2);
        mesh.AddIndex(baseIndex + 0);
        mesh.AddIndex(baseIndex + 2);
        mesh.AddIndex(baseIndex + 3);
    }
}
