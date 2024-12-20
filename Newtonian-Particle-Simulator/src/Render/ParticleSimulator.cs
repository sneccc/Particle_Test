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
        private readonly TextureObject particleTexture;

        public unsafe ParticleSimulator(Particle[] particles)
        {
            NumParticles = particles.Length;

            shaderProgram = new ShaderProgram(
                new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/particles/vertex.glsl")),
                new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/particles/fragment.glsl")));

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

            particleTexture = new TextureObject("res/textures/particle.png");
            
            IsRunning = true;
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.Enable(EnableCap.PrimitiveRestart);
            GL.PrimitiveRestartIndex(uint.MaxValue);
            
            shaderProgram.Upload(5, 0.3f);
        }

        private bool _isRunning;
        public bool IsRunning
        {
            get
            {
                return _isRunning;
            }

            set
            {
                _isRunning = value;
                shaderProgram.Upload(3, _isRunning ? 1.0f : 0.0f);
            }
        }
        public void Run(float dT, Matrix4 view, Matrix4 projection, Vector3 camPos)
        {
            GL.ClearColor(1.0f, 1.0f, 1.0f, 1.0f);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            
            // Enable depth testing but keep writing to depth buffer
            GL.Enable(EnableCap.DepthTest);
            GL.DepthMask(true);
            GL.DepthFunc(DepthFunction.Lequal);
            
            // Set up blending for opaque parts
            GL.Enable(EnableCap.Blend);
            GL.BlendEquation(BlendEquationMode.FuncAdd);
            GL.BlendFuncSeparate(
                BlendingFactorSrc.SrcAlpha,
                BlendingFactorDest.OneMinusSrcAlpha,
                BlendingFactorSrc.One,
                BlendingFactorDest.One
            );
            
            shaderProgram.Use();
            particleTexture.Use(TextureUnit.Texture0);
            
            shaderProgram.Upload(0, dT);
            shaderProgram.Upload(4, view * projection);
            shaderProgram.Upload(6, camPos);
            
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, indexBuffer.ID);
            GL.DrawElements(PrimitiveType.Triangles, NumParticles * 6, DrawElementsType.UnsignedInt, 0);
            
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit);
        }

        public void ProcessInputs(GameWindow gameWindow, in Vector3 camPos, in Matrix4 view, in Matrix4 projection)
        {
            if (gameWindow.CursorVisible)
            {
                if (MouseManager.LeftButton == ButtonState.Pressed)
                {
                    System.Drawing.Point windowSpaceCoords = gameWindow.PointToClient(new System.Drawing.Point(MouseManager.WindowPositionX, MouseManager.WindowPositionY)); windowSpaceCoords.Y = gameWindow.Height - windowSpaceCoords.Y; // [0, Width][0, Height]
                    Vector2 normalizedDeviceCoords = Vector2.Divide(new Vector2(windowSpaceCoords.X, windowSpaceCoords.Y), new Vector2(gameWindow.Width, gameWindow.Height)) * 2.0f - new Vector2(1.0f); // [-1.0, 1.0][-1.0, 1.0]
                    Vector3 dir = GetWorldSpaceRay(projection.Inverted(), view.Inverted(), normalizedDeviceCoords);

                    Vector3 pointOfMass = camPos + dir * 25.0f;
                    shaderProgram.Upload(1, pointOfMass);
                    shaderProgram.Upload(2, 1.0f);
                }
                else
                    shaderProgram.Upload(2, 0.0f);
            }

            if (KeyboardManager.IsKeyTouched(Key.T))
                IsRunning = !IsRunning;

            shaderProgram.Upload(4, view * projection);
        }

        public static Vector3 GetWorldSpaceRay(Matrix4 inverseProjection, Matrix4 inverseView, Vector2 normalizedDeviceCoords)
        {
            Vector4 rayEye = new Vector4(normalizedDeviceCoords.X, normalizedDeviceCoords.Y, -1.0f, 1.0f) * inverseProjection; rayEye.Z = -1.0f; rayEye.W = 0.0f;
            return (rayEye * inverseView).Xyz.Normalized();
        }

        public void Dispose()
        {
            particleTexture?.Dispose();
            shaderProgram?.Dispose();
            particleBuffer?.Dispose();
            indexBuffer?.Dispose();
        }
    }
}
