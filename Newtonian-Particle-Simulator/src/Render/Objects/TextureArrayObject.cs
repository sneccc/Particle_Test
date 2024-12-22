using OpenTK.Graphics.OpenGL4;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Newtonian_Particle_Simulator.Render.Objects
{
    class TextureArrayObject : IDisposable
    {
        public readonly int ID;
        public readonly int LayerCount;
        public readonly int Width;
        public readonly int Height;

        public TextureArrayObject(string[] texturePaths, int? forcedWidth = null, int? forcedHeight = null)
        {
            if (texturePaths == null || texturePaths.Length == 0)
                throw new ArgumentException("Must provide at least one texture path");

            LayerCount = texturePaths.Length;
            ID = GL.GenTexture();

            // First pass: determine dimensions if not forced
            if (!forcedWidth.HasValue || !forcedHeight.HasValue)
            {
                DetermineOptimalDimensions(texturePaths, out int maxWidth, out int maxHeight);
                Width = forcedWidth ?? maxWidth;
                Height = forcedHeight ?? maxHeight;
            }
            else
            {
                Width = forcedWidth.Value;
                Height = forcedHeight.Value;
            }

            GL.BindTexture(TextureTarget.Texture2DArray, ID);

            // Allocate storage for the texture array
            GL.TexStorage3D(TextureTarget3d.Texture2DArray, 1, SizedInternalFormat.Rgba8,
                Width, Height, LayerCount);

            // Load and process each texture
            for (int i = 0; i < texturePaths.Length; i++)
            {
                try
                {
                    using (var processedImage = LoadAndProcessImage(texturePaths[i], Width, Height))
                    {
                        var data = processedImage.LockBits(
                            new Rectangle(0, 0, Width, Height),
                            ImageLockMode.ReadOnly,
                            System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                        GL.TexSubImage3D(TextureTarget.Texture2DArray, 0,
                            0, 0, i, // xoffset, yoffset, layer
                            Width, Height, 1, // width, height, depth
                            OpenTK.Graphics.OpenGL4.PixelFormat.Bgra,
                            PixelType.UnsignedByte,
                            data.Scan0);

                        processedImage.UnlockBits(data);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Failed to load texture {texturePaths[i]}: {ex.Message}");
                    // Load a fallback texture (pink checkerboard pattern)
                    using (var fallbackTexture = CreateFallbackTexture(Width, Height))
                    {
                        var data = fallbackTexture.LockBits(
                            new Rectangle(0, 0, Width, Height),
                            ImageLockMode.ReadOnly,
                            System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                        GL.TexSubImage3D(TextureTarget.Texture2DArray, 0,
                            0, 0, i,
                            Width, Height, 1,
                            OpenTK.Graphics.OpenGL4.PixelFormat.Bgra,
                            PixelType.UnsignedByte,
                            data.Scan0);

                        fallbackTexture.UnlockBits(data);
                    }
                }
            }

            // Set texture parameters
            GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        }

        private void DetermineOptimalDimensions(string[] paths, out int maxWidth, out int maxHeight)
        {
            maxWidth = 0;
            maxHeight = 0;

            foreach (var path in paths)
            {
                try
                {
                    using (var image = LoadImageFromAnyFormat(path))
                    {
                        maxWidth = Math.Max(maxWidth, image.Width);
                        maxHeight = Math.Max(maxHeight, image.Height);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Failed to load {path} for dimension check: {ex.Message}");
                }
            }

            // Ensure we have at least some minimal dimensions
            maxWidth = Math.Max(maxWidth, 16);
            maxHeight = Math.Max(maxHeight, 16);

            // Round up to nearest power of 2 if not already
            maxWidth = NextPowerOfTwo(maxWidth);
            maxHeight = NextPowerOfTwo(maxHeight);
        }

        private static Bitmap LoadImageFromAnyFormat(string path)
        {
            // Handle different file extensions
            string ext = Path.GetExtension(path).ToLower();

            using (var stream = File.OpenRead(path))
            {
                // Special handling for specific formats could go here
                // For now, we'll let System.Drawing handle what it can
                return new Bitmap(stream);
            }
        }

        private static Bitmap LoadAndProcessImage(string path, int targetWidth, int targetHeight)
        {
            using (var originalImage = LoadImageFromAnyFormat(path))
            {
                var processedImage = new Bitmap(targetWidth, targetHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                using (var graphics = Graphics.FromImage(processedImage))
                {
                    graphics.CompositingMode = CompositingMode.SourceOver;
                    graphics.CompositingQuality = CompositingQuality.HighQuality;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.SmoothingMode = SmoothingMode.HighQuality;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

                    // Center the image if aspect ratios don't match
                    float scale = Math.Min((float)targetWidth / originalImage.Width,
                                         (float)targetHeight / originalImage.Height);

                    float scaledWidth = originalImage.Width * scale;
                    float scaledHeight = originalImage.Height * scale;
                    float offsetX = (targetWidth - scaledWidth) / 2;
                    float offsetY = (targetHeight - scaledHeight) / 2;

                    graphics.Clear(Color.Transparent); // Ensure transparency
                    graphics.DrawImage(originalImage,
                        new RectangleF(offsetX, offsetY, scaledWidth, scaledHeight));
                }

                return processedImage;
            }
        }

        private static Bitmap CreateFallbackTexture(int width, int height)
        {
            var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                // Create a checkerboard pattern in pink/transparent
                int cellSize = Math.Max(width, height) / 8;
                using (var brush1 = new SolidBrush(Color.FromArgb(255, 255, 192, 203))) // Pink
                using (var brush2 = new SolidBrush(Color.FromArgb(0, 255, 192, 203))) // Transparent pink
                {
                    for (int y = 0; y < height; y += cellSize)
                    {
                        for (int x = 0; x < width; x += cellSize)
                        {
                            graphics.FillRectangle(
                                ((x + y) / cellSize) % 2 == 0 ? brush1 : brush2,
                                x, y, cellSize, cellSize);
                        }
                    }
                }
            }
            return bitmap;
        }

        private static int NextPowerOfTwo(int value)
        {
            value--;
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            value++;
            return value;
        }

        public void Use(TextureUnit unit = TextureUnit.Texture0)
        {
            GL.ActiveTexture(unit);
            GL.BindTexture(TextureTarget.Texture2DArray, ID);
        }

        public void Dispose()
        {
            GL.DeleteTexture(ID);
        }

        public static TextureArrayObject LoadFromDirectory(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
            {
                Console.WriteLine($"Directory not found: {directoryPath}");
                throw new DirectoryNotFoundException($"Texture directory not found: {directoryPath}");
            }

            var validExtensions = new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };
            var texturePaths = Directory.GetFiles(directoryPath)
                .Where(file => validExtensions.Contains(Path.GetExtension(file).ToLower()))
                .ToArray();

            Console.WriteLine($"Found texture files: {string.Join(", ", texturePaths)}");

            if (texturePaths.Length == 0)
                throw new ArgumentException($"No valid texture files found in {directoryPath}");

            return new TextureArrayObject(texturePaths);
        }
    }
}