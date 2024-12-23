using OpenTK.Graphics.OpenGL4;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Memory;
using OpenTK;
using Buffer = System.Buffer;

namespace Newtonian_Particle_Simulator.Render.Objects
{
    class TextureArrayObject : IDisposable
    {
        public readonly int ID;
        public readonly int LayerCount;
        public readonly int TextureCount;
        public readonly int Width;
        public readonly int Height;
        private const int TEXTURE_SIZE = 256;
        private const int ATLAS_SIZE = 4096;
        private const int TEXTURES_PER_ROW = ATLAS_SIZE / TEXTURE_SIZE;
        private const int TEXTURES_PER_ATLAS = (ATLAS_SIZE / TEXTURE_SIZE) * (ATLAS_SIZE / TEXTURE_SIZE);
        private const int OPTIMAL_BATCH_SIZE = 24; // Match your thread count

        private class ProcessedTexture
        {
            public byte[] Data;
            public int AtlasX;
            public int AtlasY;
            public int Layer;
            public string FileName;
        }

        public TextureArrayObject(string[] texturePaths)
        {
            if (texturePaths == null || texturePaths.Length == 0)
                throw new ArgumentException("Must provide at least one texture path");

            TextureCount = texturePaths.Length;
            Console.WriteLine($"Attempting to load {TextureCount} textures using parallel processing");

            // Calculate dimensions
            LayerCount = (int)Math.Ceiling((double)TextureCount / TEXTURES_PER_ATLAS);
            Width = ATLAS_SIZE;
            Height = ATLAS_SIZE;

            // Create and configure the texture array
            ID = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2DArray, ID);
            GL.TexStorage3D(TextureTarget3d.Texture2DArray, 1, SizedInternalFormat.Rgba8, 
                Width, Height, LayerCount);

            // Process textures in parallel
            var processedTextures = new ConcurrentDictionary<int, ProcessedTexture>();
            var options = new ParallelOptions { MaxDegreeOfParallelism = OPTIMAL_BATCH_SIZE };

            Console.WriteLine($"Processing textures using {OPTIMAL_BATCH_SIZE} parallel threads");
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            Parallel.For(0, TextureCount, options, (i) =>
            {
                try
                {
                    using (var image = Image.Load<Rgba32>(texturePaths[i]))
                    {
                        // Calculate atlas position
                        int atlasLayer = i / TEXTURES_PER_ATLAS;
                        int localIndex = i % TEXTURES_PER_ATLAS;
                        int atlasX = (localIndex % TEXTURES_PER_ROW) * TEXTURE_SIZE;
                        int atlasY = (localIndex / TEXTURES_PER_ROW) * TEXTURE_SIZE;

                        // Process image
                        image.Mutate(x => x.Resize(TEXTURE_SIZE, TEXTURE_SIZE));
                        byte[] imageData = new byte[TEXTURE_SIZE * TEXTURE_SIZE * 4];
                        image.CopyPixelDataTo(imageData);

                        processedTextures[i] = new ProcessedTexture
                        {
                            Data = imageData,
                            AtlasX = atlasX,
                            AtlasY = atlasY,
                            Layer = atlasLayer,
                            FileName = Path.GetFileName(texturePaths[i])
                        };
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Failed to load texture {texturePaths[i]}: {ex.Message}");
                    processedTextures[i] = CreateFallbackTexture(i);
                }
            });

            // Upload textures to GPU in batches by layer
            var texturesByLayer = processedTextures.Values
                .GroupBy(t => t.Layer)
                .OrderBy(g => g.Key);

            foreach (var layerGroup in texturesByLayer)
            {
                var layer = layerGroup.Key;
                var layerData = new byte[Width * Height * 4];

                // Combine all textures for this layer
                foreach (var texture in layerGroup)
                {
                    CopyTextureToAtlas(texture.Data, layerData, texture.AtlasX, texture.AtlasY);
                    Console.WriteLine($"Placing texture {texture.FileName} at position ({texture.AtlasX}, {texture.AtlasY}) in layer {layer}");
                }

                // Upload layer to GPU
                unsafe
                {
                    fixed (void* ptr = layerData)
                    {
                        GL.TexSubImage3D(TextureTarget.Texture2DArray, 0,
                            0, 0, layer,
                            Width, Height, 1,
                            OpenTK.Graphics.OpenGL4.PixelFormat.Rgba,
                            PixelType.UnsignedByte,
                            (IntPtr)ptr);
                    }
                }
            }

            stopwatch.Stop();
            SetTextureParameters();
            Console.WriteLine($"Successfully loaded {TextureCount} textures into {LayerCount} atlas layers in {stopwatch.ElapsedMilliseconds}ms");
        }

        private ProcessedTexture CreateFallbackTexture(int index)
        {
            int atlasLayer = index / TEXTURES_PER_ATLAS;
            int localIndex = index % TEXTURES_PER_ATLAS;
            int atlasX = (localIndex % TEXTURES_PER_ROW) * TEXTURE_SIZE;
            int atlasY = (localIndex / TEXTURES_PER_ROW) * TEXTURE_SIZE;

            byte[] imageData = new byte[TEXTURE_SIZE * TEXTURE_SIZE * 4];
            for (int y = 0; y < TEXTURE_SIZE; y++)
            {
                for (int x = 0; x < TEXTURE_SIZE; x++)
                {
                    int pixelIndex = (y * TEXTURE_SIZE + x) * 4;
                    bool isEven = ((x / 32) + (y / 32)) % 2 == 0;
                    if (isEven)
                    {
                        imageData[pixelIndex + 0] = 255; // R
                        imageData[pixelIndex + 1] = 192; // G
                        imageData[pixelIndex + 2] = 203; // B
                        imageData[pixelIndex + 3] = 255; // A
                    }
                    else
                    {
                        imageData[pixelIndex + 0] = 255;
                        imageData[pixelIndex + 1] = 192;
                        imageData[pixelIndex + 2] = 203;
                        imageData[pixelIndex + 3] = 0;
                    }
                }
            }

            return new ProcessedTexture
            {
                Data = imageData,
                AtlasX = atlasX,
                AtlasY = atlasY,
                Layer = atlasLayer,
                FileName = $"fallback_{index}"
            };
        }

        private void CopyTextureToAtlas(byte[] textureData, byte[] atlasData, int atlasX, int atlasY)
        {
            for (int y = 0; y < TEXTURE_SIZE; y++)
            {
                int sourceOffset = y * TEXTURE_SIZE * 4;
                int destOffset = ((atlasY + y) * Width + atlasX) * 4;
                Buffer.BlockCopy(textureData, sourceOffset, atlasData, destOffset, TEXTURE_SIZE * 4);
            }
        }

        private void SetTextureParameters()
        {
            GL.BindTexture(TextureTarget.Texture2DArray, ID);
            GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
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
                throw new DirectoryNotFoundException($"Texture directory not found: {directoryPath}");

            Console.WriteLine($"Loading textures from directory: {directoryPath}");
            
            var validExtensions = new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };
            var texturePaths = Directory.GetFiles(directoryPath)
                .Where(file => validExtensions.Contains(Path.GetExtension(file).ToLower()))
                .OrderBy(file => Path.GetFileName(file))
                .ToArray();

            Console.WriteLine($"Found {texturePaths.Length} texture files");

            if (texturePaths.Length == 0)
                throw new ArgumentException($"No valid texture files found in {directoryPath}");

            return new TextureArrayObject(texturePaths);
        }

        // Helper method to calculate UV coordinates for a texture in the atlas
        public static (Vector2 min, Vector2 max) GetAtlasUV(int textureIndex)
        {
            int atlasLayer = textureIndex / TEXTURES_PER_ATLAS;
            int localIndex = textureIndex % TEXTURES_PER_ATLAS;
            int atlasX = (localIndex % TEXTURES_PER_ROW) * TEXTURE_SIZE;
            int atlasY = (localIndex / TEXTURES_PER_ROW) * TEXTURE_SIZE;

            return (
                new Vector2((float)atlasX / ATLAS_SIZE, (float)atlasY / ATLAS_SIZE),
                new Vector2((float)(atlasX + TEXTURE_SIZE) / ATLAS_SIZE, (float)(atlasY + TEXTURE_SIZE) / ATLAS_SIZE)
            );
        }
    }
}