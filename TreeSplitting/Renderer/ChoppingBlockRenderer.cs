using System;
using TreeSplitting.BlockEntities;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace TreeSplitting.Rendering;

public class WoodWorkItemRenderer : IRenderer, IDisposable
{
    private ICoreClientAPI api;
    private BlockPos pos;
    private BEChoppingBlock be;
    private MeshRef? meshRef;
    private MeshRef? overlayMeshRef;
    private Matrixf ModelMat = new Matrixf();
    private Vec4f outlineColorMul = new Vec4f(1, 1, 1, 1);
    private readonly Vec3f origin = new Vec3f(0, 0, 0);

    private static readonly int WhiteColor = ColorUtil.ToRgba(255, 255, 255, 255);

    public double RenderOrder => 0.5;
    public int RenderRange => 24;

    public WoodWorkItemRenderer(BEChoppingBlock be, BlockPos pos, ICoreClientAPI api)
    {
        this.api = api;
        this.pos = pos;
        this.be = be;

        // Same as the vanilla anvil, log in the opaque pass and the outline after everything else
        api.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "treesplitting-workitem");
        api.Event.RegisterRenderer(this, EnumRenderStage.AfterFinalComposition, "treesplitting-workitem");
    }

    public void RegenMesh(ItemStack? workItem, byte[,,] voxels, bool[,,]? targetVoxels = null, bool horizontal = false)
    {
        if (workItem == null)
        {
            meshRef?.Dispose();
            meshRef = null;
            overlayMeshRef?.Dispose();
            overlayMeshRef = null;
            return;
        }

        // Start big enough for a full log so it doesn't keep resizing
        MeshData mesh = new MeshData(4096, 6144, false, true, true, true);

        float yStart = 10.0f;

        CollectibleObject collectible = workItem.Item ?? (CollectibleObject)workItem.Block;
        ITexPositionSource texSource;
        if (collectible is Block block)
            texSource = api.Tesselator.GetTextureSource(block);
        else
            texSource = api.Tesselator.GetTextureSource((Vintagestory.API.Common.Item)collectible);

        // Retrieve Top texture (for UP/DOWN faces - end grain/rings)
        TextureAtlasPosition topTex = texSource["up"] ?? texSource["top"];
        if (topTex == null) topTex = texSource["all"] ?? texSource["base"] ?? texSource["texture"];
        if (topTex == null) topTex = api.BlockTextureAtlas.UnknownTexturePosition;

        // Retrieve Side/Bark texture for the outermost Bark voxels
        // The block's texSource should have the side texture mapped to directional keys
        TextureAtlasPosition sideTex = texSource["north"] ?? texSource["south"] ?? texSource["east"] ?? texSource["west"];
        if (sideTex == null) sideTex = texSource["side"] ?? texSource["bark"];
        if (sideTex == null) sideTex = texSource["all"] ?? texSource["base"] ?? texSource["texture"];
        // Final fallback - use topTex (wood grain) if no side texture found
        if (sideTex == null) sideTex = topTex;

        // Grain runs up (Y) for a standing log, along X for one lying down.
        // a and b are the axes across the log, the end grain texture is mapped u = a, v = b
        int grain = horizontal ? 0 : 1;
        int axisA = horizontal ? 2 : 0;
        int axisB = horizontal ? 1 : 2;

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

                AddVoxelFace(mesh, voxel, face, material == (byte)EnumWoodMaterial.Bark, grain, axisA, axisB, topTex, sideTex, yStart);
            }
        }

        if (meshRef != null) meshRef.Dispose();
        meshRef = api.Render.UploadMesh(mesh);


        RegenOutlineMesh(voxels, targetVoxels, yStart);
    }

    // +X, -X, +Y, -Y, +Z, -Z
    private static readonly int[][] FaceDirs =
    [
        [1, 0, 0], [-1, 0, 0], [0, 1, 0], [0, -1, 0], [0, 0, 1], [0, 0, -1]
    ];

    private readonly float[][] corners = [new float[3], new float[3], new float[3], new float[3]];
    private readonly float[] cornerU = new float[4];
    private readonly float[] cornerV = new float[4];

    /// <summary>
    /// Adds one face of a voxel and picks the texture for it:
    /// - faces looking down the grain (the log's ends) get the end grain with the rings
    /// - bark gets the bark texture running along the log
    /// - cut sides of the inner wood get the row of the end grain at that depth stretched along the grain, so the rings
    ///   show up as stripes that line up with the rings on the end, like real wood (end grain is 32px, so +0.25/+0.75 of
    ///   a voxel picks the texel right at the surface)
    /// </summary>
    private void AddVoxelFace(MeshData mesh, int[] voxel, int face, bool isBark, int grain, int axisA, int axisB,
        TextureAtlasPosition endTex, TextureAtlasPosition barkTex, float yStart)
    {
        int[] dir = FaceDirs[face];
        int normal = dir[0] != 0 ? 0 : dir[1] != 0 ? 1 : 2;
        bool positive = dir[normal] > 0;
        int t1 = (normal + 1) % 3, t2 = (normal + 2) % 3;

        // Corners in voxel units, (0,0) (1,0) (1,1) (0,1) across the face. Culling is off so winding doesn't matter
        for (int i = 0; i < 4; i++)
        {
            float[] c = corners[i];
            c[normal] = voxel[normal] + (positive ? 1 : 0);
            c[t1] = voxel[t1] + (i is 1 or 2 ? 1 : 0);
            c[t2] = voxel[t2] + (i is 2 or 3 ? 1 : 0);
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

        AddFaceUv(mesh, tex,
            corners[0][0] / 16f, (corners[0][1] + yStart) / 16f, corners[0][2] / 16f, cornerU[0], cornerV[0],
            corners[1][0] / 16f, (corners[1][1] + yStart) / 16f, corners[1][2] / 16f, cornerU[1], cornerV[1],
            corners[2][0] / 16f, (corners[2][1] + yStart) / 16f, corners[2][2] / 16f, cornerU[2], cornerV[2],
            corners[3][0] / 16f, (corners[3][1] + yStart) / 16f, corners[3][2] / 16f, cornerU[3], cornerV[3],
            VertexFlags.PackNormal(dir[0], dir[1], dir[2]));
    }

    /// <summary>
    /// Orange box around every voxel you still need to cut, same as the anvil and clay forming.
    /// Uses the game's colour presets so colour-blind mode works. Skips voxels buried in the log, you can't see them anyway.
    /// </summary>
    private void RegenOutlineMesh(byte[,,] voxels, bool[,,]? targetVoxels, float yStart)
    {
        overlayMeshRef?.Dispose();
        overlayMeshRef = null;

        if (targetVoxels == null) return;

        MeshData outlineMesh = new MeshData(24, 36, false, false, true, false);
        outlineMesh.SetMode(EnumDrawMode.Lines);

        int removeCol = api.ColorPreset.GetColor("voxelColorOrange");
        MeshData cubeMesh = LineMeshUtil.GetCube(removeCol);
        // GetCube is -1 to 1, shrink it down to one voxel
        for (int i = 0; i < cubeMesh.xyz.Length; i++)
        {
            cubeMesh.xyz[i] = cubeMesh.xyz[i] / 32f + 1 / 32f;
        }
        MeshData voxelMeshOffset = cubeMesh.Clone();

        for (int x = 0; x < 16; x++)
        {
            for (int y = 0; y < 16; y++)
            {
                for (int z = 0; z < 16; z++)
                {
                    bool hasWood = voxels[x, y, z] != (byte)EnumWoodMaterial.Empty;
                    bool keepWood = targetVoxels[x, y, z];
                    if (!hasWood || keepWood) continue;

                    bool exposed =
                        y == 15 || voxels[x, y + 1, z] == 0 ||
                        y == 0 || voxels[x, y - 1, z] == 0 ||
                        x == 0 || voxels[x - 1, y, z] == 0 ||
                        x == 15 || voxels[x + 1, y, z] == 0 ||
                        z == 0 || voxels[x, y, z - 1] == 0 ||
                        z == 15 || voxels[x, y, z + 1] == 0;
                    if (!exposed) continue;

                    float px = x / 16f;
                    float py = (y + yStart) / 16f;
                    float pz = z / 16f;

                    for (int i = 0; i < cubeMesh.xyz.Length; i += 3)
                    {
                        voxelMeshOffset.xyz[i] = px + cubeMesh.xyz[i];
                        voxelMeshOffset.xyz[i + 1] = py + cubeMesh.xyz[i + 1];
                        voxelMeshOffset.xyz[i + 2] = pz + cubeMesh.xyz[i + 2];
                    }

                    outlineMesh.AddMeshData(voxelMeshOffset);
                }
            }
        }

        if (outlineMesh.VerticesCount > 0)
        {
            overlayMeshRef = api.Render.UploadMesh(outlineMesh);
        }
    }

    private void RenderRecipeOutline()
    {
        if (overlayMeshRef == null || api.HideGuis) return;

        // Only show it when holding a tool you can use on the log, like clay forming does with clay
        EnumTool? tool = api.World.Player?.InventoryManager?.ActiveHotbarSlot?.Itemstack?.Collectible?.Tool;
        if (tool is not (EnumTool.Axe or EnumTool.Saw or EnumTool.Chisel or EnumTool.Knife)) return;

        IRenderAPI rpi = api.Render;
        EntityPos plrPos = api.World.Player!.Entity.Pos;
        Vec3d camPos = api.World.Player.Entity.CameraPos;

        ModelMat.Set(rpi.CameraMatrixOriginf).Translate(pos.X - camPos.X, pos.Y - camPos.Y, pos.Z - camPos.Z);
        // Fades out between 5 and 10 blocks away
        outlineColorMul.A = 1 - GameMath.Clamp((float)Math.Sqrt(plrPos.SquareDistanceTo(pos.X, pos.Y, pos.Z)) / 5 - 1f, 0, 1);

        float linewidth = api.Settings.Float["wireframethickness"];
        rpi.LineWidth = linewidth;
        rpi.GLEnableDepthTest();
        rpi.GlToggleBlend(true);

        IShaderProgram prog = rpi.GetEngineShader(EnumShaderProgram.Wireframe);
        prog.Use();
        prog.Uniform("origin", origin);
        prog.UniformMatrix("projectionMatrix", rpi.CurrentProjectionMatrix);
        prog.UniformMatrix("modelViewMatrix", ModelMat.Values);

        prog.Uniform("colorIn", outlineColorMul);
        rpi.RenderMesh(overlayMeshRef);
        prog.Stop();

        if (linewidth != 1.6f) rpi.LineWidth = 1.6f;

        if (RuntimeEnv.OS != OS.Mac) return;
        // Mac needs these reset after, vanilla does the same
        rpi.GLDisableDepthTest();
        rpi.GlToggleBlend(false);
    }

    /// <summary>
    /// One quad, you give the uv (0-1 in the texture) for each corner yourself
    /// </summary>
    private static void AddFaceUv(MeshData mesh, TextureAtlasPosition tex,
        float x0, float y0, float z0, float u0, float v0,
        float x1, float y1, float z1, float u1, float v1,
        float x2, float y2, float z2, float u2, float v2,
        float x3, float y3, float z3, float u3, float v3,
        int normalFlags)
    {
        int baseIndex = mesh.VerticesCount;
        float w = tex.x2 - tex.x1;
        float h = tex.y2 - tex.y1;

        mesh.AddVertex(x0, y0, z0, tex.x1 + u0 * w, tex.y1 + v0 * h, WhiteColor);
        mesh.AddVertex(x1, y1, z1, tex.x1 + u1 * w, tex.y1 + v1 * h, WhiteColor);
        mesh.AddVertex(x2, y2, z2, tex.x1 + u2 * w, tex.y1 + v2 * h, WhiteColor);
        mesh.AddVertex(x3, y3, z3, tex.x1 + u3 * w, tex.y1 + v3 * h, WhiteColor);

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

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (meshRef == null) return;

        if (stage == EnumRenderStage.AfterFinalComposition)
        {
            RenderRecipeOutline();
            return;
        }

        IRenderAPI rpi = api.Render;
        Vec3d camPos = api.World.Player.Entity.CameraPos;
        rpi.GlDisableCullFace();
        IStandardShaderProgram prog = rpi.PreparedStandardShader(pos.X, pos.Y, pos.Z);
        prog.ModelMatrix = ModelMat.Identity().Translate(pos.X - camPos.X, pos.Y - camPos.Y, pos.Z - camPos.Z).Values;
        prog.ViewMatrix = rpi.CameraMatrixOriginf;
        prog.ProjectionMatrix = rpi.CurrentProjectionMatrix;
        prog.Tex2D = api.BlockTextureAtlas.Positions[0].atlasTextureId;

        rpi.RenderMesh(meshRef);

        prog.Stop();
    }

    public void Dispose()
    {
        api.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        api.Event.UnregisterRenderer(this, EnumRenderStage.AfterFinalComposition);
        meshRef?.Dispose();
        overlayMeshRef?.Dispose();
    }
}
