using System;
using System.Diagnostics;
using OpenTK;
using OpenTK.Input;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL4;
using Newtonian_Particle_Simulator.Render;
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
            
            // Update ImGui
            _imGuiController.Update(this, (float)e.Time);

            // Create ImGui UI
            ImGui.Begin("Controls");
            if (ImGui.SliderFloat("Scale Factor", ref _scaleFactor, 0.1f, 10.0f))
            {
                particleSimulator.SetScaleFactor(_scaleFactor);
            }
            ImGui.Text($"FPS: {FPS}");
            ImGui.Text($"Mode: {particleSimulator.CurrentMode}");
            ImGui.Text("Press 'T' to toggle between Flying and Interactive modes");
            ImGui.Text("Press 'E' to toggle cursor visibility");
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

                // Check if ImGui is using the input
                var io = ImGui.GetIO();
                if (!io.WantCaptureMouse && !io.WantCaptureKeyboard)
                {
                    if (KeyboardManager.IsKeyTouched(Key.V))
                        VSync = VSync == VSyncMode.Off ? VSyncMode.On : VSyncMode.Off;

                    if (KeyboardManager.IsKeyTouched(Key.E))
                    {
                        CursorVisible = !CursorVisible;
                        CursorGrabbed = !CursorGrabbed;

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

            VSync = VSyncMode.Off;

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
            GL.ClearColor(1.0f, 1.0f, 1.0f, 1.0f); // White background
            base.OnLoad(e);
        }

        protected override void OnResize(EventArgs e)
        {
            if (Width != 0 && Height != 0)
            {
                GL.Viewport(0, 0, Width, Height);
                projection = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(103.0f), (float)Width / Height, 0.1f, 1000f);
                _imGuiController?.WindowResized(Width, Height);
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
            _imGuiController?.Dispose();
            base.OnUnload(e);
        }
    }
}
