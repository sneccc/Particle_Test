using OpenTK.Graphics.OpenGL4;
using Newtonian_Particle_Simulator.Render.Objects;
using OpenTK;

namespace Newtonian_Particle_Simulator.Render
{
    class Crosshair
    {
        private readonly ShaderProgram _shader;
        private readonly int _vao;
        private readonly int _vbo;
        private float _aspectRatio = 1.0f;
        private readonly float _size;

        // The crosshair is always at screen center (0,0) in NDC
        public Vector2 CenterNDC => Vector2.Zero;
        
        // Get center in screen coordinates (pixels)
        public Vector2 GetCenterScreen(int screenWidth, int screenHeight)
        {
            return new Vector2(screenWidth / 2f, screenHeight / 2f);
        }

        // Check if a point in NDC coordinates is near the crosshair center
        public bool IsPointNearCenter(Vector2 pointNDC, float threshold = 0.05f)
        {
            return (pointNDC - CenterNDC).Length <= threshold;
        }

        // Check if a screen point (in pixels) is near the crosshair center
        public bool IsScreenPointNearCenter(Vector2 screenPoint, int screenWidth, int screenHeight, float thresholdPixels = 10f)
        {
            Vector2 center = GetCenterScreen(screenWidth, screenHeight);
            return (screenPoint - center).Length <= thresholdPixels;
        }

        // Convert screen coordinates to NDC
        public Vector2 ScreenToNDC(Vector2 screenPoint, int screenWidth, int screenHeight)
        {
            return new Vector2(
                (2.0f * screenPoint.X) / screenWidth - 1.0f,
                1.0f - (2.0f * screenPoint.Y) / screenHeight
            );
        }

        public Crosshair(float size = 0.01f)
        {
            _size = size;
            // Initialize crosshair shader
            string vertexShaderSource = @"
                #version 330 core
                layout(location = 0) in vec2 aPos;
                uniform float aspectRatio;
                void main() {
                    vec2 pos = aPos;
                    pos.x /= aspectRatio;  // Adjust x coordinate for aspect ratio
                    gl_Position = vec4(pos, 0.0, 1.0);
                }";

            string fragmentShaderSource = @"
                #version 330 core
                out vec4 FragColor;
                void main() {
                    FragColor = vec4(1.0, 1.0, 1.0, 1.0); // White color
                }";

            _shader = new ShaderProgram(
                new Shader(ShaderType.VertexShader, vertexShaderSource),
                new Shader(ShaderType.FragmentShader, fragmentShaderSource)
            );

            // Create crosshair VAO and VBO
            _vao = GL.GenVertexArray();
            _vbo = GL.GenBuffer();

            GL.BindVertexArray(_vao);
            float[] vertices = {
                -size, 0.0f,  // Left point
                size, 0.0f,   // Right point
                0.0f, -size,  // Bottom point
                0.0f, size    // Top point
            };

            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);

            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
        }

        public void UpdateAspectRatio(float width, float height)
        {
            _aspectRatio = width / height;
        }

        public void Draw()
        {
            _shader.Use();
            _shader.Upload("aspectRatio", _aspectRatio);
            GL.BindVertexArray(_vao);
            GL.DrawArrays(PrimitiveType.Lines, 0, 4);
        }

        public void Dispose()
        {
            GL.DeleteBuffer(_vbo);
            GL.DeleteVertexArray(_vao);
            _shader?.Dispose();
        }
    }
}