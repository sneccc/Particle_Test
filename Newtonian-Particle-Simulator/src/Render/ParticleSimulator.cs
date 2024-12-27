using System;
using System.IO;
using OpenTK;
using OpenTK.Input;
using OpenTK.Graphics.OpenGL4;
using Newtonian_Particle_Simulator.Render.Objects;

namespace Newtonian_Particle_Simulator.Render
{
    class ParticleSimulator
    {
        public readonly int NumParticles;
        private readonly BufferObject particleBuffer;
        private readonly BufferObject indexBuffer;
        private readonly ShaderProgram shaderProgram;
        private readonly TextureArrayObject particleTextures;
        private readonly Vector3[] originalPositions;
        private float currentScaleFactor = 1.0f;
        private Vector3 currentAxisScales = new Vector3(1.0f);
        private int selectedParticleIndex = -1;
        private bool isFilteredMode = false;
        private const float NEIGHBOR_DISTANCE_THRESHOLD = 5.0f; // Adjust this value to control how close particles need to be

        public unsafe ParticleSimulator(Particle[] particles, string[] texturePaths = null)
        {
            NumParticles = particles.Length;
            originalPositions = new Vector3[NumParticles];
            for (int i = 0; i < NumParticles; i++)
            {
                originalPositions[i] = particles[i].Position;
            }

            shaderProgram = new ShaderProgram(
                new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/particles/vertex.glsl")),
                new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/particles/fragment.glsl")));

            shaderProgram.Use();
            int textureLocation = GL.GetUniformLocation(shaderProgram.ID, "particleTextures");
            GL.Uniform1(textureLocation, 0); // Texture unit 0
            shaderProgram.Upload(8, 1.0f);  // Initialize dynamic scale to 1.0
            shaderProgram.Upload("axisScales", currentAxisScales);  // Initialize axis scales to 100%

            particleBuffer = new BufferObject(BufferRangeTarget.ShaderStorageBuffer, 0);
            particleBuffer.ImmutableAllocate(sizeof(Particle) * (nint)NumParticles, particles[0], BufferStorageFlags.None);

            uint[] indices = new uint[NumParticles * 6];
            for (uint i = 0; i < NumParticles; i++)
            {
                uint baseVertex = i * 4;
                uint baseIndex = i * 6;

                // First triangle
                indices[baseIndex + 0] = baseVertex + 0;
                indices[baseIndex + 1] = baseVertex + 1;
                indices[baseIndex + 2] = baseVertex + 2;

                // Second triangle
                indices[baseIndex + 3] = baseVertex + 2;
                indices[baseIndex + 4] = baseVertex + 1;
                indices[baseIndex + 5] = baseVertex + 3;
            }

            indexBuffer = new BufferObject();
            fixed (void* ptr = &indices[0])
            {
                indexBuffer.ImmutableAllocate(sizeof(uint) * indices.Length, (IntPtr)ptr, BufferStorageFlags.None);
            }

            try
            {
                if (texturePaths != null)
                {
                    // Load textures from provided paths
                    particleTextures = new TextureArrayObject(texturePaths);
                    Console.WriteLine($"Loaded {particleTextures.TextureCount} textures from provided paths");
                }
                else
                {
                    // Load textures from default directory
                    particleTextures = TextureArrayObject.LoadFromDirectory("res/textures/particles");
                    Console.WriteLine($"Found {particleTextures.TextureCount} textures in default directory");
                }
                shaderProgram.Upload("numTextures", particleTextures.TextureCount);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load particle textures: {ex.Message}");
                particleTextures = new TextureArrayObject(new[] { "res/textures/particle.png" });
                shaderProgram.Upload("numTextures", 1);
            }

            IsRunning = true;

            // Set up static OpenGL state
            GL.Enable(EnableCap.Blend);
            GL.BlendEquation(BlendEquationMode.FuncAdd);
            GL.BlendFuncSeparate(
                BlendingFactorSrc.SrcAlpha,
                BlendingFactorDest.OneMinusSrcAlpha,
                BlendingFactorSrc.One,
                BlendingFactorDest.One
            );

            GL.Enable(EnableCap.DepthTest);
            GL.DepthMask(true);
            GL.DepthFunc(DepthFunction.Lequal);

            GL.Enable(EnableCap.PrimitiveRestart);
            GL.PrimitiveRestartIndex(uint.MaxValue);

            shaderProgram.Upload("particleSize", 0.7f);
        }

        private bool _isRunning;
        private string _currentMode = "Interactive Mode (Right Click)";
        public string CurrentMode => _currentMode;

        public bool IsRunning
        {
            get
            {
                return _isRunning;
            }

            set
            {
                _isRunning = value;
                _currentMode = value ? "Interactive Mode (Right Click)":"Flying Mode";
                shaderProgram.Upload(3, _isRunning ? 1.0f : 0.0f);
            }
        }
        public void Run(float dT, Matrix4 view, Matrix4 projection, Vector3 camPos)
        {
            // Save OpenGL state
            bool depthTest = GL.GetBoolean(GetPName.DepthTest);
            bool blend = GL.GetBoolean(GetPName.Blend);
            BlendingFactor blendSrcFactor = (BlendingFactor)GL.GetInteger(GetPName.BlendSrc);
            BlendingFactor blendDstFactor = (BlendingFactor)GL.GetInteger(GetPName.BlendDst);

            // Set up particle rendering state
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            shaderProgram.Use();
            particleTextures.Use(TextureUnit.Texture0);

            // Update uniforms
            shaderProgram.Upload(0, dT);
            shaderProgram.Upload(4, view * projection);
            shaderProgram.Upload(6, camPos);
            shaderProgram.Upload(8, currentScaleFactor);  // Ensure scale factor is set every frame

            GL.BindBuffer(BufferTarget.ElementArrayBuffer, indexBuffer.ID);
            GL.DrawElements(PrimitiveType.Triangles, NumParticles * 6, DrawElementsType.UnsignedInt, 0);

            GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit);

            // Restore OpenGL state
            if (!depthTest) GL.Disable(EnableCap.DepthTest);
            if (!blend) GL.Disable(EnableCap.Blend);
            else GL.BlendFunc(blendSrcFactor, blendDstFactor);
        }

        public void ProcessInputs(GameWindow gameWindow, in Vector3 camPos, in Matrix4 view, in Matrix4 projection)
        {
            if (gameWindow.CursorVisible)
            {
                if (MouseManager.RightButton == ButtonState.Pressed)
                {
                    System.Drawing.Point windowSpaceCoords = gameWindow.PointToClient(new System.Drawing.Point(MouseManager.WindowPositionX, MouseManager.WindowPositionY));
                    windowSpaceCoords.Y = gameWindow.Height - windowSpaceCoords.Y;
                    Vector2 normalizedDeviceCoords = Vector2.Divide(new Vector2(windowSpaceCoords.X, windowSpaceCoords.Y), new Vector2(gameWindow.Width, gameWindow.Height)) * 2.0f - new Vector2(1.0f);
                    Vector3 dir = GetWorldSpaceRay(projection.Inverted(), view.Inverted(), normalizedDeviceCoords);

                    Vector3 pointOfMass = camPos + dir * 25.0f;
                    shaderProgram.Upload(1, pointOfMass);
                    shaderProgram.Upload(2, 1.0f);
                }
                else
                    shaderProgram.Upload(2, 0.0f);
            }
            else
            {
                // Handle left click in fly mode
                if (MouseManager.IsButtonTouched(MouseButton.Left) && selectedParticleIndex != -1)
                {
                    isFilteredMode = !isFilteredMode;
                    if (isFilteredMode)
                    {
                        // Upload the selected particle's position for distance calculations
                        Vector3 selectedPos = originalPositions[selectedParticleIndex] * currentScaleFactor * currentAxisScales;
                        shaderProgram.Upload("selectedParticlePos", selectedPos);
                    }
                    shaderProgram.Upload("isFilteredMode", isFilteredMode);
                    shaderProgram.Upload("neighborThreshold", NEIGHBOR_DISTANCE_THRESHOLD);
                }
            }

            if (KeyboardManager.IsKeyTouched(Key.T))
            {
                IsRunning = !IsRunning;
                // Reset filtered mode when switching modes
                if (isFilteredMode)
                {
                    isFilteredMode = false;
                    shaderProgram.Upload("isFilteredMode", false);
                }
            }

            shaderProgram.Upload(4, view * projection);
        }

        public static Vector3 GetWorldSpaceRay(Matrix4 inverseProjection, Matrix4 inverseView, Vector2 normalizedDeviceCoords)
        {
            Vector4 rayEye = new Vector4(normalizedDeviceCoords.X, normalizedDeviceCoords.Y, -1.0f, 1.0f) * inverseProjection; rayEye.Z = -1.0f; rayEye.W = 0.0f;
            return (rayEye * inverseView).Xyz.Normalized();
        }

        public void Dispose()
        {
            particleTextures?.Dispose();
            shaderProgram?.Dispose();
            particleBuffer?.Dispose();
            indexBuffer?.Dispose();
        }

        public void SetParticleSize(float size)
        {
            shaderProgram.Use();
            shaderProgram.Upload("particleSize", size);
        }

        public unsafe void SetScaleFactor(float newScaleFactor)
        {
            currentScaleFactor = newScaleFactor;
            shaderProgram.Use();
            shaderProgram.Upload(8, newScaleFactor);  // Update dynamic scale uniform
        }

        public unsafe void SetAxisScales(Vector3 scales)
        {
            currentAxisScales = scales;
            shaderProgram.Use();
            shaderProgram.Upload("axisScales", scales);
        }

        public void UpdateSelection(Vector3 cameraPos, Vector3 viewDir)
        {
            float closestDistance = float.MaxValue;
            int closestParticle = -1;
            float selectionThreshold = 0.5f;  // Increased threshold
            float maxDistance = 50.0f;  // Maximum distance to consider particles
            
            // Use viewDir directly as our ray direction since it's already normalized
            Vector3 rayDir = viewDir;
            
            for (int i = 0; i < NumParticles; i++)
            {
                Vector3 particlePos = originalPositions[i] * currentScaleFactor * currentAxisScales;
                Vector3 toParticle = particlePos - cameraPos;
                
                // Project particle onto ray
                float dot = Vector3.Dot(toParticle, rayDir);
                if (dot <= 0 || dot > maxDistance) continue; // Behind camera or too far
                
                Vector3 projection = rayDir * dot;
                Vector3 toRay = toParticle - projection;
                float distanceToRay = toRay.Length;
                
                if (distanceToRay < selectionThreshold && dot < closestDistance)
                {
                    closestDistance = dot;
                    closestParticle = i;
                }
            }

            // Only update selection if we found a valid particle or if we had a valid selection before
            if (closestParticle != -1 || selectedParticleIndex != -1)
            {
                if (selectedParticleIndex != closestParticle)
                {
                    //Console.WriteLine($"Selection changed: {selectedParticleIndex} -> {closestParticle} (distance: {(closestParticle != -1 ? closestDistance : 0):F3})");
                    selectedParticleIndex = closestParticle;
                    shaderProgram.Use();
                    shaderProgram.Upload(9, selectedParticleIndex);
                }
            }
        }
    }
}
