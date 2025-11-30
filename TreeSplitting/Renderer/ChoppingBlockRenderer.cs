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

        public WoodWorkItemRenderer(BEChoppingBlock be, BlockPos pos, ICoreClientAPI? api)
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
            
            // CONSTANTS (Must match BEChoppingBlock exactly)
            float pixelSize = 1f / 16f;
            float yStart = 10.0f; // The exact same 'yStart' variable from SelectionBoxes

            // Texture Lookup
            CollectibleObject collectible = workItem.Item ?? (CollectibleObject)workItem.Block;
            ITexPositionSource texSource = api.Tesselator.GetTextureSource((Block)collectible);
            TextureAtlasPosition tPos = null;
            
            if (collectible is Block block)
            {
                tPos = texSource["up"] ?? texSource["top"] ?? texSource["north"] ?? texSource["all"];
                if (tPos == null && block.Textures != null)
                {
                    foreach (var val in block.Textures) { tPos = texSource[val.Key]; if (tPos != null) break; }
                }
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
                            // 1. Calculate py exactly like SelectionBox
                            float py = y + yStart;

                            // 2. Calculate Min/Max exactly like SelectionBox
                            // Note: x/16f is implicitly (float)x/16f
                            float xMin = x / 16f;
                            float yMin = py / 16f;
                            float zMin = z / 16f;

                            float xMax = (x + 1) / 16f;
                            float yMax = (py + 1) / 16f;
                            float zMax = (z + 1) / 16f;

                            // 3. Convert Min/Max to Center/Size for GetCube
                            // Size is always 1/16
                            Vec3f center = new Vec3f(
                                (xMin + xMax) * 0.5f,
                                (yMin + yMax) * 0.5f,
                                (zMin + zMax) * 0.5f
                            );

                            MeshData cube = CubeMeshUtil.GetCube(pixelSize/2f, pixelSize/2f, pixelSize/2f, center);

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
            
            // GET BLOCK ROTATION
            Block block = api.World.BlockAccessor.GetBlock(pos);
            float rotY = block.Shape.rotateY; // This is usually 0 for basic blocks.
            // For directional blocks, we check the block's rotation behavior.
            
            // Actually, simplest way is to ignore rotation if we force the block to be non-rotatable.
            // But if it IS rotated:
            
            prog.ModelMatrix = ModelMat
                .Identity()
                .Translate(pos.X - camPos.X, pos.Y - camPos.Y, pos.Z - camPos.Z)
                // .RotateY(rotY) // Apply if needed
                .Values;

            prog.ViewMatrix = rpi.CameraMatrixOriginf;
            prog.ProjectionMatrix = rpi.CurrentProjectionMatrix;
            prog.Tex2D = api.BlockTextureAtlas.Positions[0].atlasTextureId;

            rpi.RenderMesh(meshRef);
            
            prog.Stop();
        }

        public void Dispose()
        {
            api.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
            meshRef?.Dispose();
        }
    }
}