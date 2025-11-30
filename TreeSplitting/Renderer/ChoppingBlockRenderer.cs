using System;
using TreeSplitting.BlockEntities;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace TreeSplitting.Rendering
{
    public class WoodWorkItemRenderer : IRenderer, IDisposable
    {
        private ICoreClientAPI api;
        private BlockPos pos;
        private BEChoppingBlock be;
        private MeshRef meshRef;
        private MeshRef overlayMeshRef; 
        private Matrixf ModelMat = new Matrixf();

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

            // 1. Main Wood Mesh
            MeshData mesh = new MeshData(24, 36);
            
            float pixelSize = 1f / 16f; 
            float radius = (pixelSize / 2f); 
            float yStart = 10.0f; 

            CollectibleObject collectible = workItem.Item ?? (CollectibleObject)workItem.Block;
            ITexPositionSource texSource;
            if (collectible is Block block)
                texSource = api.Tesselator.GetTextureSource(block);
            else
                texSource = api.Tesselator.GetTextureSource((Item)collectible);

            TextureAtlasPosition tPos = null;
            if (collectible is Block b)
            {
                tPos = texSource["up"] ?? texSource["top"] ?? texSource["north"] ?? texSource["all"];
                if (tPos == null && b.Textures != null) { foreach (var val in b.Textures) { tPos = texSource[val.Key]; if (tPos != null) break; } }
            }
            if (tPos == null) tPos = texSource["all"] ?? texSource["base"] ?? texSource["texture"];
            if (tPos == null) tPos = api.BlockTextureAtlas.UnknownTexturePosition;

            for (int x = 0; x < 16; x++)
            {
                for (int y = 0; y < 16; y++)
                {
                    for (int z = 0; z < 16; z++)
                    {
                        if (voxels[x, y, z] != 0)
                        {
                            float py = y + yStart;
                            float xMin = x / 16f;
                            float yMin = py / 16f;
                            float zMin = z / 16f;
                            
                            Vec3f corner = new Vec3f(xMin, yMin, zMin);
                            MeshData cube = CubeMeshUtil.GetCube(radius, radius, radius, corner);

                            for (int i = 0; i < cube.Uv.Length; i++)
                            {
                                if (i % 2 == 0) cube.Uv[i] = tPos.x1 + (cube.Uv[i] * (tPos.x2 - tPos.x1));
                                else cube.Uv[i] = tPos.y1 + (cube.Uv[i] * (tPos.y2 - tPos.y1));
                            }
                            cube.Rgba.Fill((byte)255);
                            mesh.AddMeshData(cube);
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
                int greenCol = ColorUtil.ToRgba(255, 0, 255, 0);

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
}