using System;
using TreeSplitting.BlockEntities;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace TreeSplitting.Rendering;

public class WoodWorkItemRenderer : IRenderer, IDisposable
{
    private ICoreClientAPI api;
    private BlockPos pos;
    private BEChoppingBlock be;
    private MeshRef meshRef;
    private MeshRef overlayMeshRef; 
    private Matrixf ModelMat = new Matrixf();
        
    // Pre-computed white color for vertices (full brightness)
    private static readonly int WhiteColor = ColorUtil.ToRgba(255, 255, 255, 255);

    public double RenderOrder => 0.5;
    public int RenderRange => 24;

    public WoodWorkItemRenderer(BEChoppingBlock be, BlockPos pos, ICoreClientAPI api)
    {
        this.api = api;
        this.pos = pos;
        this.be = be;
    }

    public void RegenMesh(ItemStack workItem, byte[,,] voxels, byte[,,] targetVoxels = null)
    {
        if (workItem == null)
        {
            meshRef?.Dispose();
            meshRef = null;
            overlayMeshRef?.Dispose();
            overlayMeshRef = null;
            return;
        }

        // 1. Main Wood Mesh - with flags for proper lighting/normals
        MeshData mesh = new MeshData(24, 36, false, true, true, true);
            
        float pixelSize = 1f / 16f; 
        float yStart = 10.0f; 

        CollectibleObject collectible = workItem.Item ?? (CollectibleObject)workItem.Block;
        ITexPositionSource texSource;
        if (collectible is Block block)
            texSource = api.Tesselator.GetTextureSource(block);
        else
            texSource = api.Tesselator.GetTextureSource((Item)collectible);

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

                        // Determine which texture to use for side faces based on material type
                        // - Bark (3) = outer bark layer, use side/bark texture
                        // - Heartwood (1) and Sapwood (2) = inner wood, use top/wood grain texture
                        //   (Both represent exposed inner wood when visible)
                        TextureAtlasPosition sideTexForVoxel = (voxelMaterial == (byte)EnumWoodMaterial.Bark) ? sideTex : topTex;
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

                        // NORTH face (Z-) - normal pointing north (0, 0, -1)
                        // For inner wood (heartwood/sapwood), rotate UV 90 degrees so grain runs vertically
                        if (z == 0 || voxels[x, y, z - 1] == 0)
                        {
                            if (isInnerWood)
                                AddFace(mesh, sideTexForVoxel,
                                    xMax, yMin, zMin,
                                    xMin, yMin, zMin,
                                    xMin, yMax, zMin,
                                    xMax, yMax, zMin,
                                    uvYMin, uvXMin, uvYMax, uvXMax,
                                    VertexFlags.PackNormal(0, 0, -1));
                            else
                                AddFace(mesh, sideTexForVoxel,
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
                                AddFace(mesh, sideTexForVoxel,
                                    xMin, yMin, zMax,
                                    xMax, yMin, zMax,
                                    xMax, yMax, zMax,
                                    xMin, yMax, zMax,
                                    uvYMin, uvXMin, uvYMax, uvXMax,
                                    VertexFlags.PackNormal(0, 0, 1));
                            else
                                AddFace(mesh, sideTexForVoxel,
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
                                AddFace(mesh, sideTexForVoxel,
                                    xMin, yMin, zMin,
                                    xMin, yMin, zMax,
                                    xMin, yMax, zMax,
                                    xMin, yMax, zMin,
                                    uvYMin, uvZMin, uvYMax, uvZMax,
                                    VertexFlags.PackNormal(-1, 0, 0));
                            else
                                AddFace(mesh, sideTexForVoxel,
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
                                AddFace(mesh, sideTexForVoxel,
                                    xMax, yMin, zMax,
                                    xMax, yMin, zMin,
                                    xMax, yMax, zMin,
                                    xMax, yMax, zMax,
                                    uvYMin, uvZMin, uvYMax, uvZMax,
                                    VertexFlags.PackNormal(1, 0, 0));
                            else
                                AddFace(mesh, sideTexForVoxel,
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


        // 2. Green Overlay Mesh
        if (overlayMeshRef != null) { overlayMeshRef.Dispose(); overlayMeshRef = null; }
            
        if (targetVoxels != null)
        {
            MeshData overlayMesh = new MeshData(24, 36);
            overlayMesh.SetMode(EnumDrawMode.Lines);
                
            // Standard Green Color
            int greenCol = ColorUtil.ToRgba(255, 0,255, 0);

            for (int x = 0; x < 16; x++)
            {
                for (int y = 0; y < 16; y++)
                {
                    for (int z = 0; z < 16; z++)
                    {
                        // Show highlight if it's a Target Voxel AND it currently exists
                        if (targetVoxels[x, y, z] != 0 && voxels[x, y, z] != 0)
                        {
                            float py = y + yStart;
                            float xMin = x / 16f;
                            float yMin = py / 16f;
                            float zMin = z / 16f;
                                
                            float expand = 0.02f; 
                                
                            // Inline box generation for performance (avoiding GetCube allocations)
                            AddWireframeBox(overlayMesh, 
                                xMin, yMin, zMin,
                                xMin + pixelSize, yMin + pixelSize, zMin + pixelSize,
                                greenCol);
                        }
                    }
                }
            }

            if (overlayMesh.VerticesCount > 0)
            {
                overlayMeshRef = api.Render.UploadMesh(overlayMesh);
            }
        }
    }

    /// <summary>
    /// Adds a single quad face to the mesh with proper texture coordinates.
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

    private void AddWireframeBox(MeshData mesh, float x1, float y1, float z1, float x2, float y2, float z2, int color)
    {
        int i = mesh.VerticesCount;
            
        // Use the explicit overload for x,y,z,u,v,color
        mesh.AddVertex(x1, y1, z1, 0, 0, color); 
        mesh.AddVertex(x2, y1, z1, 0, 0, color); 
        mesh.AddVertex(x2, y1, z2, 0, 0, color); 
        mesh.AddVertex(x1, y1, z2, 0, 0, color); 
        mesh.AddVertex(x1, y2, z1, 0, 0, color); 
        mesh.AddVertex(x2, y2, z1, 0, 0, color); 
        mesh.AddVertex(x2, y2, z2, 0, 0, color); 
        mesh.AddVertex(x1, y2, z2, 0, 0, color); 

        // Indices
        mesh.AddIndex(i+0); mesh.AddIndex(i+1);
        mesh.AddIndex(i+1); mesh.AddIndex(i+2);
        mesh.AddIndex(i+2); mesh.AddIndex(i+3);
        mesh.AddIndex(i+3); mesh.AddIndex(i+0);

        mesh.AddIndex(i+4); mesh.AddIndex(i+5);
        mesh.AddIndex(i+5); mesh.AddIndex(i+6);
        mesh.AddIndex(i+6); mesh.AddIndex(i+7);
        mesh.AddIndex(i+7); mesh.AddIndex(i+4);

        mesh.AddIndex(i+0); mesh.AddIndex(i+4);
        mesh.AddIndex(i+1); mesh.AddIndex(i+5);
        mesh.AddIndex(i+2); mesh.AddIndex(i+6);
        mesh.AddIndex(i+3); mesh.AddIndex(i+7);
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (meshRef == null) return;
        IRenderAPI rpi = api.Render;
        Vec3d camPos = api.World.Player.Entity.CameraPos;
        rpi.GlDisableCullFace();
        IStandardShaderProgram prog = rpi.PreparedStandardShader(pos.X, pos.Y, pos.Z);
        prog.ModelMatrix = ModelMat.Identity().Translate(pos.X - camPos.X, pos.Y - camPos.Y, pos.Z - camPos.Z).Values;
        prog.ViewMatrix = rpi.CameraMatrixOriginf;
        prog.ProjectionMatrix = rpi.CurrentProjectionMatrix;
        prog.Tex2D = api.BlockTextureAtlas.Positions[0].atlasTextureId;
            
        rpi.RenderMesh(meshRef);
            
        if (overlayMeshRef != null)
        {
            prog.ExtraGlow = 255;
            rpi.RenderMesh(overlayMeshRef);
            prog.ExtraGlow = 0;
        }

        prog.Stop();
    }

    public void Dispose() 
    { 
        api.Event.UnregisterRenderer(this, EnumRenderStage.Opaque); 
        meshRef?.Dispose(); 
        overlayMeshRef?.Dispose(); 
    }
}