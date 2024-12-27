using System;
using System.Diagnostics;
using OpenTK;
using OpenTK.Input;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL4;
using Newtonian_Particle_Simulator.Render;
using Newtonian_Particle_Simulator.Render.Objects;
using System.IO;
using ImGuiNET;

namespace Newtonian_Particle_Simulator
{
    public enum PositionLoadingMode
    {
        Random,
        FromNPY
    }

    class MainWindow : GameWindow
    {
        private ImGuiController _imGuiController;
        private float _scaleFactor = 1.0f;
        private float _particleSize = 0.7f;
        private Vector3 _axisScales = new Vector3(1.0f);
        private float _backgroundColor = 0.0f;
        private Crosshair _crosshair;

        public MainWindow() 
            : base(832, 832, new GraphicsMode(0, 0, 0, 0), "idk man") { /*WindowState = WindowState.Fullscreen;*/ }

        private readonly Camera camera = new Camera(new Vector3(0, 0, 15), new Vector3(0, 1, 0));
        private Matrix4 projection;

        private int frames = 0, FPS;

        protected override void OnRenderFrame(FrameEventArgs e)
        {
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            // Save OpenGL state
            int lastProgram = GL.GetInteger(GetPName.CurrentProgram);
            bool depthTest = GL.GetBoolean(GetPName.DepthTest);
            bool blend = GL.GetBoolean(GetPName.Blend);
            
            // Render particles
            particleSimulator.Run((float)e.Time, camera.View, projection, camera.Position);
            
            // Update particle selection in fly mode
            if (!CursorVisible)
            {
                particleSimulator.UpdateSelection(camera.Position, camera.ViewDir);
            }
            
            // Draw crosshair in fly mode
            if (!CursorVisible)
            {
                _crosshair.Draw();
            }

            // Update ImGui
            _imGuiController.Update(this, (float)e.Time);

            // Create ImGui UI
            ImGui.Begin("Controls");
            
            // Background color control
            ImGui.Separator();
            ImGui.Text("Background");
            if (ImGui.SliderFloat("Brightness", ref _backgroundColor, 0.0f, 1.0f, "%.2f"))
            {
                GL.ClearColor(_backgroundColor, _backgroundColor, _backgroundColor, 1.0f);
            }

            ImGui.Separator();
            // Logarithmic scale factor slider
            float logScale = (float)Math.Log(_scaleFactor, 2); // Convert to log scale
            if (ImGui.SliderFloat("Scale Factor (log2)", ref logScale, -2f, 2f))
            {
                float newScale = (float)Math.Pow(2, logScale); // Convert back from log scale
                _scaleFactor = newScale;
                particleSimulator.SetScaleFactor(_scaleFactor);
            }
            ImGui.Text($"Actual scale: {_scaleFactor:F3}x"); // Show actual scale value
            
            // Particle size slider
            if (ImGui.SliderFloat("Particle Size", ref _particleSize, 0.1f, 2.0f))
            {
                particleSimulator.SetParticleSize(_particleSize);
            }

            // Axis scale sliders (0% to 100%)
            ImGui.Separator();
            ImGui.Text("Axis Scales");
            bool axisScaleChanged = false;
            
            float xScale = _axisScales.X * 100f; // Convert to percentage
            if (ImGui.SliderFloat("X Axis", ref xScale, 0f, 100f, "%.0f%%"))
            {
                _axisScales.X = xScale / 100f;
                axisScaleChanged = true;
            }
            
            float yScale = _axisScales.Y * 100f;
            if (ImGui.SliderFloat("Y Axis", ref yScale, 0f, 100f, "%.0f%%"))
            {
                _axisScales.Y = yScale / 100f;
                axisScaleChanged = true;
            }
            
            float zScale = _axisScales.Z * 100f;
            if (ImGui.SliderFloat("Z Axis", ref zScale, 0f, 100f, "%.0f%%"))
            {
                _axisScales.Z = zScale / 100f;
                axisScaleChanged = true;
            }

            if (axisScaleChanged)
            {
                particleSimulator.SetAxisScales(_axisScales);
            }

            ImGui.Text($"FPS: {FPS}");
            ImGui.Text($"Mode: {particleSimulator.CurrentMode}");
            ImGui.Text("Press 'T' to toggle between Flying and Interactive modes");
            ImGui.End();

            // Render ImGui
            _imGuiController.Render();

            // Restore OpenGL state
            GL.UseProgram(lastProgram);
            if (depthTest) GL.Enable(EnableCap.DepthTest);
            else GL.Disable(EnableCap.DepthTest);
            if (blend) GL.Enable(EnableCap.Blend);
            else GL.Disable(EnableCap.Blend);
            
            SwapBuffers();
            frames++;
            base.OnRenderFrame(e);
        }

        protected override void OnUpdateFrame(FrameEventArgs e)
        {
            if (fpsTimer.ElapsedMilliseconds >= 1000)
            {
                FPS = frames;
                Title = $"shaders ftw, FPS: {FPS}";
                frames = 0;
                fpsTimer.Restart();
            }

            if (Focused)
            {
                KeyboardManager.Update();
                MouseManager.Update();

                // Always process Escape key
                if (KeyboardManager.IsKeyDown(Key.Escape))
                    Close();

                // Only check ImGui input capture when cursor is visible
                bool canProcessInput = CursorVisible ? 
                    !ImGui.GetIO().WantCaptureMouse && !ImGui.GetIO().WantCaptureKeyboard : 
                    true;

                if (canProcessInput)
                {
                    if (KeyboardManager.IsKeyTouched(Key.V))
                        VSync = VSync == VSyncMode.Off ? VSyncMode.On : VSyncMode.Off;

                    if (KeyboardManager.IsKeyTouched(Key.T))
                    {
                        // Toggle both mode and cursor state
                        CursorVisible = !CursorVisible;
                        CursorGrabbed = !CursorVisible;  // Grab cursor when invisible
                        particleSimulator.IsRunning = !CursorVisible;  // Flying mode when cursor is hidden

                        if (!CursorVisible)
                        {
                            MouseManager.Update();
                            camera.Velocity = Vector3.Zero;
                        }
                    }

                    if (KeyboardManager.IsKeyTouched(Key.F11))
                        WindowState = WindowState == WindowState.Normal ? WindowState.Fullscreen : WindowState.Normal;

                    particleSimulator.ProcessInputs(this, camera.Position, camera.View, projection);
                    if (!CursorVisible)
                        camera.ProcessInputs((float)e.Time);
                }
            }

            base.OnUpdateFrame(e);
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            _imGuiController.PressChar(e.KeyChar);
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);
            // Additional handling if needed
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);
            // Additional handling if needed
        }

        protected override void OnMouseMove(MouseMoveEventArgs e)
        {
            base.OnMouseMove(e);
            // Additional handling if needed
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            // Additional handling if needed
        }

        private readonly Stopwatch fpsTimer = Stopwatch.StartNew();
        private ParticleSimulator particleSimulator;

        protected override void OnLoad(EventArgs e)
        {
            Console.WriteLine($"OpenGL: {Helper.APIVersion}");
            Console.WriteLine($"GLSL: {GL.GetString(StringName.ShadingLanguageVersion)}");
            Console.WriteLine($"GPU: {GL.GetString(StringName.Renderer)}");

            if (!Helper.IsCoreExtensionAvailable("GL_ARB_direct_state_access", 4.5))
                throw new NotSupportedException("Your system does not support GL_ARB_direct_state_access");

            if (!Helper.IsCoreExtensionAvailable("GL_ARB_buffer_storage", 4.4))
                throw new NotSupportedException("Your system does not support GL_ARB_buffer_storage");

            // Initialize ImGui
            _imGuiController = new ImGuiController(Width, Height);

            // Initialize crosshair
            _crosshair = new Crosshair(0.01f);
            _crosshair.UpdateAspectRatio(Width, Height);  // Set initial aspect ratio

            VSync = VSyncMode.Off;

            // Set initial background color to black
            GL.ClearColor(_backgroundColor, _backgroundColor, _backgroundColor, 1.0f);

            int numParticles;
            PositionLoadingMode loadingMode;
            string folderPath = "";

            Console.WriteLine("Select position loading mode:");
            Console.WriteLine("0 - Random positions");
            Console.WriteLine("1 - Load from embeddings folder");
            
            while (!Enum.TryParse(Console.ReadLine(), out loadingMode) || !Enum.IsDefined(typeof(PositionLoadingMode), loadingMode))
            {
                Console.WriteLine("Invalid input. Please enter 0 or 1.");
            }

            if (loadingMode == PositionLoadingMode.FromNPY)
            {
                Console.Write("Enter folder path containing image_embeddings.npy and file_paths.npy: ");
                folderPath = Console.ReadLine();
                while (!Directory.Exists(folderPath) || 
                       !File.Exists(Path.Combine(folderPath, "image_embeddings.npy")) || 
                       !File.Exists(Path.Combine(folderPath, "file_paths.npy")))
                {
                    Console.WriteLine("Invalid folder path or missing required files. Please try again:");
                    folderPath = Console.ReadLine();
                }

                int maxImages;
                Console.Write("Enter number of images to load (0 for all): ");
                while (!int.TryParse(Console.ReadLine(), out maxImages) || maxImages < 0)
                {
                    Console.WriteLine("Please enter a valid number (0 or greater):");
                }

                var embeddingData = EmbeddingLoader.LoadFromFolder(folderPath, maxEntries: maxImages);
                numParticles = embeddingData.Positions.Length;
                Console.WriteLine($"Processing {numParticles} particles...");

                Particle[] particles = new Particle[numParticles];
                for (int i = 0; i < numParticles; i++)
                {
                    particles[i].Position = embeddingData.Positions[i];
                    particles[i].Velocity = Vector3.Zero;
                }

                particleSimulator = new ParticleSimulator(particles, embeddingData.FilePaths);
            }
            else
            {
                do
                {
                    Console.Write($"Number of particles: ");
                } while ((!int.TryParse(Console.ReadLine(), out numParticles)) || numParticles < 0);

                Particle[] particles = new Particle[numParticles];
                Random rng = new Random();
                for (int i = 0; i < particles.Length; i++)
                {
                    particles[i].Position = new Vector3(
                        rng.NextSingle() * 100 - 50,
                        rng.NextSingle() * 100 - 50,
                        -rng.NextSingle() * 100
                    );
                    particles[i].Velocity = Vector3.Zero;
                }

                particleSimulator = new ParticleSimulator(particles);
            }

            GC.Collect();
            base.OnLoad(e);
        }

        protected override void OnResize(EventArgs e)
        {
            if (Width != 0 && Height != 0)
            {
                GL.Viewport(0, 0, Width, Height);
                projection = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(103.0f), (float)Width / Height, 0.1f, 1000f);
                _imGuiController?.WindowResized(Width, Height);
                _crosshair?.UpdateAspectRatio(Width, Height);
            }

            base.OnResize(e);
        }

        protected override void OnFocusedChanged(EventArgs e)
        {
            if (Focused)
                MouseManager.Update();
        }

        protected override void OnUnload(EventArgs e)
        {
            _crosshair?.Dispose();
            _imGuiController?.Dispose();
            base.OnUnload(e);
        }
    }
}
