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
        private Matrixf ModelMat = new Matrixf();

        public double RenderOrder => 0.5;
        public int RenderRange => 24;

        public WoodWorkItemRenderer(BEChoppingBlock be, BlockPos pos, ICoreClientAPI api)
        {
            this.api = api;
            this.pos = pos;
            this.be = be;
        }

        public void RegenMesh(ItemStack workItem, byte[,,] voxels)
        {
            if (workItem == null)
            {
                meshRef?.Dispose();
                meshRef = null;
                return;
            }

            MeshData mesh = new MeshData(24, 36);
            
            float pixelSize = 1f / 16f; 
            float radius = (pixelSize / 2f); // 0.03125
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
                        if (voxels[x, y, z] != (byte)EnumWoodMaterial.Empty)
                        {
                            float py = y + yStart;
                            
                            // Center Calculation
                            float xMin = x / 16f;
                            float yMin = py / 16f;
                            float zMin = z / 16f;
                            
                            
                            Vec3f corner = new Vec3f(
                                xMin,
                                yMin,
                                zMin 
                            );

                            // GetCube with RADIUS
                            MeshData cube = CubeMeshUtil.GetCube(radius, radius, radius, corner);

                            // UV Mapping
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
            prog.Stop();
        }

        public void Dispose() { api.Event.UnregisterRenderer(this, EnumRenderStage.Opaque); meshRef?.Dispose(); }
    }
}