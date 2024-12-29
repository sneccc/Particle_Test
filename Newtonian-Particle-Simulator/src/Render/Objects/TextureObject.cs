using OpenTK.Graphics.OpenGL4;
using System.Drawing;
using System.Drawing.Imaging;
using System;
using System.IO;
using ImageMagick;

namespace Newtonian_Particle_Simulator.Render.Objects
{
    class TextureObject : IDisposable
    {
        public readonly int ID;

        public TextureObject(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Texture file not found: {path}");
            }

            ID = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, ID);

            string extension = Path.GetExtension(path).ToLower();
            if (extension == ".hdr")
            {
                LoadHDR(path);
            }
            else
            {
                LoadRegularImage(path);
            }

            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        }

        private void LoadRegularImage(string path)
        {
            try
            {
                using (var image = new Bitmap(path))
                {
                    var data = image.LockBits(
                        new Rectangle(0, 0, image.Width, image.Height),
                        ImageLockMode.ReadOnly,
                        System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                    GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
                        image.Width, image.Height, 0, OpenTK.Graphics.OpenGL4.PixelFormat.Bgra,
                        PixelType.UnsignedByte, data.Scan0);

                    image.UnlockBits(data);
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to load regular image {path}: {ex.Message}", ex);
            }
        }

        private void LoadHDR(string path)
        {
            try
            {
                using (var image = new MagickImage(path))
                {
                    // Convert to RGB format
                    image.Format = MagickFormat.Rgb;
                    
                    // Get the pixel data
                    var pixels = image.GetPixels();
                    int width = (int)image.Width;
                    int height = (int)image.Height;
                    
                    // Create a float array to store RGB values
                    var floatPixels = new float[width * height * 3];
                    
                    // Copy pixel data to float array
                    for (int i = 0; i < width * height; i++)
                    {
                        var pixel = pixels.GetPixel(i % width, i / width);
                        floatPixels[i * 3] = (float)pixel.GetChannel(0) / 65535.0f;     // R
                        floatPixels[i * 3 + 1] = (float)pixel.GetChannel(1) / 65535.0f; // G
                        floatPixels[i * 3 + 2] = (float)pixel.GetChannel(2) / 65535.0f; // B
                    }

                    unsafe
                    {
                        // Upload to GPU as floating point texture
                        fixed (void* ptr = &floatPixels[0])
                        {
                            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgb16f,
                                width, height, 0, OpenTK.Graphics.OpenGL4.PixelFormat.Rgb,
                                PixelType.Float, (IntPtr)ptr);
                        }
                    }

                    Console.WriteLine($"Loaded HDR image: {width}x{height}");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to load HDR image {path}: {ex.Message}", ex);
            }
        }

        public void Use(TextureUnit unit = TextureUnit.Texture0)
        {
            GL.ActiveTexture(unit);
            GL.BindTexture(TextureTarget.Texture2D, ID);
        }

        public void Dispose()
        {
            GL.DeleteTexture(ID);
        }
    }
} 