using System;
using TreeSplitting.BlockEntities;
using TreeSplitting.Logic;
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
    private MeshRef? marksMeshRef;
    private Matrixf ModelMat = new Matrixf();
    private Vec4f outlineColorMul = new Vec4f(1, 1, 1, 1);
    private readonly Vec3f origin = new Vec3f(0, 0, 0);

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

    public void RegenMesh(ItemStack? workItem, byte[,,] voxels, bool[,,]? targetVoxels = null, int grainAxis = 1)
    {
        if (workItem == null)
        {
            meshRef?.Dispose();
            meshRef = null;
            overlayMeshRef?.Dispose();
            overlayMeshRef = null;
            return;
        }

        float yStart = 10.0f;
        MeshData mesh = LogMesher.GenMesh(api, workItem, voxels, grainAxis, yStart);

        if (meshRef != null) meshRef.Dispose();
        meshRef = api.Render.UploadMesh(mesh);


        RegenOutlineMesh(voxels, targetVoxels, yStart);
    }

    /// <summary>
    /// Orange box around every voxel that still needs cutting, same as the anvil and clay forming.
    /// Uses the game's colour presets so colour-blind mode works. Skips voxels buried in the log, they can't be seen anyway.
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

    /// <summary>
    /// The saw marks: the two corners bright, the cells between them faint. It's what the player picked, not a hit preview
    /// </summary>
    public void SetSawMarks(SawMarks? marks)
    {
        marksMeshRef?.Dispose();
        marksMeshRef = null;
        if (marks == null) return;

        MeshData mesh = new MeshData(24, 36, false, false, true, false);
        mesh.SetMode(EnumDrawMode.Lines);

        void AddCube(Vec3i v, int color)
        {
            MeshData cube = LineMeshUtil.GetCube(color);
            for (int i = 0; i < cube.xyz.Length; i += 3)
            {
                cube.xyz[i] = cube.xyz[i] / 32f + 1 / 32f + v.X / 16f;
                cube.xyz[i + 1] = cube.xyz[i + 1] / 32f + 1 / 32f + (v.Y + 10f) / 16f;
                cube.xyz[i + 2] = cube.xyz[i + 2] / 32f + 1 / 32f + v.Z / 16f;
            }
            mesh.AddMeshData(cube);
        }

        int faint = ColorUtil.ToRgba(140, 120, 200, 255);
        int bright = ColorUtil.ToRgba(255, 255, 255, 255);
        foreach (Vec3i cell in marks.FaceCells()) AddCube(cell, faint);
        AddCube(marks.A, bright);
        AddCube(marks.B, bright);

        marksMeshRef = api.Render.UploadMesh(mesh);
    }

    private void RenderRecipeOutline()
    {
        if ((overlayMeshRef == null && marksMeshRef == null) || api.HideGuis) return;

        // Only show it when holding a tool that works on the log, like clay forming does with clay
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
        if (overlayMeshRef != null) rpi.RenderMesh(overlayMeshRef);
        if (marksMeshRef != null)
        {
            // The marks sit right on the wood and got lost in it, draw them on top so they show before anything's cut
            rpi.GLDisableDepthTest();
            rpi.RenderMesh(marksMeshRef);
            rpi.GLEnableDepthTest();
        }
        prog.Stop();

        if (linewidth != 1.6f) rpi.LineWidth = 1.6f;

        if (RuntimeEnv.OS != OS.Mac) return;
        // Mac needs these reset after, vanilla does the same
        rpi.GLDisableDepthTest();
        rpi.GlToggleBlend(false);
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
        marksMeshRef?.Dispose();
    }
}
