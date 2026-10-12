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

    public void RegenMesh(ItemStack? workItem, byte[,,] voxels, bool[,,]? targetVoxels = null)
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

        for (int x = 0; x < 16; x++)
        {
            for (int y = 0; y < 16; y++)
            {
                for (int z = 0; z < 16; z++)
                {
                    byte voxelMaterial = voxels[x, y, z];
                    if (voxelMaterial != 0)
                    {
                        float py = y + yStart;
                        float xMin = x / 16f;
                        float yMin = py / 16f;
                        float zMin = z / 16f;
                        float xMax = (x + 1) / 16f;
                        float yMax = (py + 1) / 16f;
                        float zMax = (z + 1) / 16f;

                        // Calculate UVs based on voxel position in 16x16 grid for seamless tiling
                        float uvXMin = x / 16f;
                        float uvXMax = (x + 1) / 16f;
                        float uvZMin = z / 16f;
                        float uvZMax = (z + 1) / 16f;
                        float uvYMin = y / 16f;
                        float uvYMax = (y + 1) / 16f;

                        // Bark gets the bark texture on its sides, inner wood gets the rings (see below)
                        bool isInnerWood = (voxelMaterial != (byte)EnumWoodMaterial.Bark);

                        // Face culling - only render faces exposed to air
                        // UP face (Y+) - normal pointing up (0, 1, 0)
                        if (y == 15 || voxels[x, y + 1, z] == 0)
                        {
                            AddFace(mesh, topTex,
                                xMin, yMax, zMin,
                                xMax, yMax, zMin,
                                xMax, yMax, zMax,
                                xMin, yMax, zMax,
                                uvXMin, uvZMin, uvXMax, uvZMax,
                                VertexFlags.PackNormal(0, 1, 0));
                        }

                        // DOWN face (Y-) - normal pointing down (0, -1, 0)
                        if (y == 0 || voxels[x, y - 1, z] == 0)
                        {
                            AddFace(mesh, topTex,
                                xMin, yMin, zMax,
                                xMax, yMin, zMax,
                                xMax, yMin, zMin,
                                xMin, yMin, zMin,
                                uvXMin, uvZMin, uvXMax, uvZMax,
                                VertexFlags.PackNormal(0, -1, 0));
                        }

                        // Cut sides of the inner wood: take the row of the end grain texture at this face's depth and
                        // stretch it up the face. Rings show up as stripes that line up with the rings on top, like real wood
                        // (the end grain is 32px so +0.25/+0.75 of a voxel picks the texel right at the surface)
                        float ringRowZNorth = (z + 0.25f) / 16f;
                        float ringRowZSouth = (z + 0.75f) / 16f;
                        float ringRowXWest = (x + 0.25f) / 16f;
                        float ringRowXEast = (x + 0.75f) / 16f;

                        // NORTH face (Z-) - normal pointing north (0, 0, -1)
                        if (z == 0 || voxels[x, y, z - 1] == 0)
                        {
                            if (isInnerWood)
                                AddFaceUv(mesh, topTex,
                                    xMax, yMin, zMin, uvXMax, ringRowZNorth,
                                    xMin, yMin, zMin, uvXMin, ringRowZNorth,
                                    xMin, yMax, zMin, uvXMin, ringRowZNorth,
                                    xMax, yMax, zMin, uvXMax, ringRowZNorth,
                                    VertexFlags.PackNormal(0, 0, -1));
                            else
                                AddFace(mesh, sideTex,
                                    xMax, yMin, zMin,
                                    xMin, yMin, zMin,
                                    xMin, yMax, zMin,
                                    xMax, yMax, zMin,
                                    uvXMin, uvYMin, uvXMax, uvYMax,
                                    VertexFlags.PackNormal(0, 0, -1));
                        }

                        // SOUTH face (Z+) - normal pointing south (0, 0, 1)
                        if (z == 15 || voxels[x, y, z + 1] == 0)
                        {
                            if (isInnerWood)
                                AddFaceUv(mesh, topTex,
                                    xMin, yMin, zMax, uvXMin, ringRowZSouth,
                                    xMax, yMin, zMax, uvXMax, ringRowZSouth,
                                    xMax, yMax, zMax, uvXMax, ringRowZSouth,
                                    xMin, yMax, zMax, uvXMin, ringRowZSouth,
                                    VertexFlags.PackNormal(0, 0, 1));
                            else
                                AddFace(mesh, sideTex,
                                    xMin, yMin, zMax,
                                    xMax, yMin, zMax,
                                    xMax, yMax, zMax,
                                    xMin, yMax, zMax,
                                    uvXMin, uvYMin, uvXMax, uvYMax,
                                    VertexFlags.PackNormal(0, 0, 1));
                        }

                        // WEST face (X-) - normal pointing west (-1, 0, 0)
                        if (x == 0 || voxels[x - 1, y, z] == 0)
                        {
                            if (isInnerWood)
                                AddFaceUv(mesh, topTex,
                                    xMin, yMin, zMin, ringRowXWest, uvZMin,
                                    xMin, yMin, zMax, ringRowXWest, uvZMax,
                                    xMin, yMax, zMax, ringRowXWest, uvZMax,
                                    xMin, yMax, zMin, ringRowXWest, uvZMin,
                                    VertexFlags.PackNormal(-1, 0, 0));
                            else
                                AddFace(mesh, sideTex,
                                    xMin, yMin, zMin,
                                    xMin, yMin, zMax,
                                    xMin, yMax, zMax,
                                    xMin, yMax, zMin,
                                    uvZMin, uvYMin, uvZMax, uvYMax,
                                    VertexFlags.PackNormal(-1, 0, 0));
                        }

                        // EAST face (X+) - normal pointing east (1, 0, 0)
                        if (x == 15 || voxels[x + 1, y, z] == 0)
                        {
                            if (isInnerWood)
                                AddFaceUv(mesh, topTex,
                                    xMax, yMin, zMax, ringRowXEast, uvZMax,
                                    xMax, yMin, zMin, ringRowXEast, uvZMin,
                                    xMax, yMax, zMin, ringRowXEast, uvZMin,
                                    xMax, yMax, zMax, ringRowXEast, uvZMax,
                                    VertexFlags.PackNormal(1, 0, 0));
                            else
                                AddFace(mesh, sideTex,
                                    xMax, yMin, zMax,
                                    xMax, yMin, zMin,
                                    xMax, yMax, zMin,
                                    xMax, yMax, zMax,
                                    uvZMin, uvYMin, uvZMax, uvYMax,
                                    VertexFlags.PackNormal(1, 0, 0));
                        }
                    }
                }
            }
        }

        if (meshRef != null) meshRef.Dispose();
        meshRef = api.Render.UploadMesh(mesh);


        RegenOutlineMesh(voxels, targetVoxels, yStart);
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
    /// Adds a single quad face to the mesh with proper texture coordinates. This shit so messy lol
    /// </summary>
    /// <param name="mesh">The mesh to add the face to</param>
    /// <param name="tex">The texture atlas position</param>
    /// <param name="x0">Vertex 0 X</param>
    /// <param name="y0">Vertex 0 Y</param>
    /// <param name="z0">Vertex 0 Z</param>
    /// <param name="x1">Vertex 1 X</param>
    /// <param name="y1">Vertex 1 Y</param>
    /// <param name="z1">Vertex 1 Z</param>
    /// <param name="x2">Vertex 2 X</param>
    /// <param name="y2">Vertex 2 Y</param>
    /// <param name="z2">Vertex 2 Z</param>
    /// <param name="x3">Vertex 3 X</param>
    /// <param name="y3">Vertex 3 Y</param>
    /// <param name="z3">Vertex 3 Z</param>
    /// <param name="uvMinX">UV X minimum (normalized 0-1)</param>
    /// <param name="uvMinY">UV Y minimum (normalized 0-1)</param>
    /// <param name="uvMaxX">UV X maximum (normalized 0-1)</param>
    /// <param name="uvMaxY">UV Y maximum (normalized 0-1)</param>
    /// <param name="normalFlags">Packed normal flags for lighting</param>
    /// <summary>
    /// Same as AddFace but you give the uv (0-1 in the texture) for each corner yourself
    /// </summary>
    private void AddFaceUv(MeshData mesh, TextureAtlasPosition tex,
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

    private void AddFace(MeshData mesh, TextureAtlasPosition tex,
        float x0, float y0, float z0,
        float x1, float y1, float z1,
        float x2, float y2, float z2,
        float x3, float y3, float z3,
        float uvMinX, float uvMinY, float uvMaxX, float uvMaxY,
        int normalFlags = 0)
    {
        int baseIndex = mesh.VerticesCount;

        // Convert normalized UV coordinates (0-1) to texture atlas coordinates
        float u0 = tex.x1 + uvMinX * (tex.x2 - tex.x1);
        float u1 = tex.x1 + uvMaxX * (tex.x2 - tex.x1);
        float v0 = tex.y1 + uvMinY * (tex.y2 - tex.y1);
        float v1 = tex.y1 + uvMaxY * (tex.y2 - tex.y1);

        // Add 4 vertices for the quad with UV and color
        mesh.AddVertex(x0, y0, z0, u0, v0, WhiteColor);
        mesh.AddVertex(x1, y1, z1, u1, v0, WhiteColor);
        mesh.AddVertex(x2, y2, z2, u1, v1, WhiteColor);
        mesh.AddVertex(x3, y3, z3, u0, v1, WhiteColor);

        // Set flags for proper lighting (normals)
        if (mesh.Flags != null && normalFlags != 0)
        {
            mesh.Flags[baseIndex] = normalFlags;
            mesh.Flags[baseIndex + 1] = normalFlags;
            mesh.Flags[baseIndex + 2] = normalFlags;
            mesh.Flags[baseIndex + 3] = normalFlags;
        }

        // Add indices for two triangles (CCW winding)
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
