using OpenTK;
using OpenTK.Graphics.OpenGL4;
using System;
using Newtonian_Particle_Simulator.Render.Objects;
using System.Drawing;
using System.Drawing.Imaging;

namespace Newtonian_Particle_Simulator.Render
{
    class Skybox : IDisposable
    {
        private readonly BufferObject vbo;
        private readonly BufferObject ebo;
        private readonly int vao;
        private readonly ShaderProgram shader;
        private readonly TextureObject hdriTexture;

        private static readonly float[] skyboxVertices = {
            // positions          
            -1.0f,  1.0f, -1.0f,
            -1.0f, -1.0f, -1.0f,
             1.0f, -1.0f, -1.0f,
             1.0f,  1.0f, -1.0f,
            -1.0f,  1.0f,  1.0f,
            -1.0f, -1.0f,  1.0f,
             1.0f, -1.0f,  1.0f,
             1.0f,  1.0f,  1.0f,
        };

        private static readonly uint[] skyboxIndices = {
            // back
            0, 1, 2,
            2, 3, 0,
            // front
            4, 5, 6,
            6, 7, 4,
            // left
            4, 5, 1,
            1, 0, 4,
            // right
            3, 2, 6,
            6, 7, 3,
            // top
            4, 0, 3,
            3, 7, 4,
            // bottom
            1, 5, 6,
            6, 2, 1
        };

        public Skybox(string hdriPath)
        {
            // Create and bind VAO
            vao = GL.GenVertexArray();
            GL.BindVertexArray(vao);

            // Create and setup VBO
            vbo = new BufferObject();
            unsafe
            {
                fixed (void* v = &skyboxVertices[0])
                {
                    vbo.ImmutableAllocate(sizeof(float) * skyboxVertices.Length, (IntPtr)v, BufferStorageFlags.None);
                }
            }

            // Create and setup EBO
            ebo = new BufferObject();
            unsafe
            {
                fixed (void* i = &skyboxIndices[0])
                {
                    ebo.ImmutableAllocate(sizeof(uint) * skyboxIndices.Length, (IntPtr)i, BufferStorageFlags.None);
                }
            }

            // Setup vertex attributes
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo.ID);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);

            // Create and compile shaders
            shader = new ShaderProgram(
                new Shader(ShaderType.VertexShader, System.IO.File.ReadAllText("res/shaders/skybox/vertex.glsl")),
                new Shader(ShaderType.FragmentShader, System.IO.File.ReadAllText("res/shaders/skybox/fragment.glsl"))
            );

            // Load HDRI texture
            hdriTexture = new TextureObject(hdriPath);
            GL.BindTexture(TextureTarget.Texture2D, hdriTexture.ID);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        }

        public void Draw(Matrix4 view, Matrix4 projection)
        {
            // Save OpenGL state
            bool depthTest = GL.GetBoolean(GetPName.DepthTest);
            int lastDepthFunc = GL.GetInteger(GetPName.DepthFunc);

            // Configure OpenGL state for skybox
            GL.DepthFunc(DepthFunction.Lequal);

            shader.Use();
            shader.Upload("view", view);
            shader.Upload("projection", projection);

            GL.BindVertexArray(vao);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo.ID);
            hdriTexture.Use(TextureUnit.Texture0);

            GL.DrawElements(PrimitiveType.Triangles, skyboxIndices.Length, DrawElementsType.UnsignedInt, 0);

            // Restore OpenGL state
            if (!depthTest) GL.Disable(EnableCap.DepthTest);
            GL.DepthFunc((DepthFunction)lastDepthFunc);
        }

        public void Dispose()
        {
            vbo?.Dispose();
            ebo?.Dispose();
            shader?.Dispose();
            hdriTexture?.Dispose();
            GL.DeleteVertexArray(vao);
        }
    }
} 