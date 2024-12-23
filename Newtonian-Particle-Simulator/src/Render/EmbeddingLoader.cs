using System;
using System.IO;
using System.Text;
using System.Linq;
using OpenTK;
using UMAP;
using System.Diagnostics;
using System.Collections.Generic;

namespace Newtonian_Particle_Simulator.Render
{
    public class EmbeddingLoader
    {
        public class EmbeddingData
        {
            public float[][] Embeddings { get; set; }
            public string[] FilePaths { get; set; }
            public Vector3[] Positions { get; set; }
        }

        public static EmbeddingData LoadFromFolder(string folderPath, float scaleFactor = 10.0f, int maxEntries = 0)
        {
            if (!Directory.Exists(folderPath))
                throw new DirectoryNotFoundException($"Folder not found: {folderPath}");

            var embeddingsPath = Path.Combine(folderPath, "image_embeddings.npy");
            var filePathsPath = Path.Combine(folderPath, "file_paths.npy");

            if (!File.Exists(embeddingsPath) || !File.Exists(filePathsPath))
                throw new FileNotFoundException("Required NPY files not found in folder");

            Console.WriteLine("Loading embeddings...");
            var embeddings = LoadNpyFile(embeddingsPath, maxEntries > 0 ? maxEntries : -1);
            var filePaths = LoadNpyStrings(filePathsPath, maxEntries > 0 ? maxEntries : -1);

            Console.WriteLine($"\nLoaded {embeddings.Length} embeddings and {filePaths.Length} file paths");
            Console.WriteLine($"Each embedding has {embeddings[0].Length} dimensions");
            
            // Print first embedding's values for verification
            Console.WriteLine("\nFirst embedding's values:");
            for (int i = 0; i < Math.Min(10, embeddings[0].Length); i++)
            {
                Console.Write($"{embeddings[0][i]:F6} ");
            }
            Console.WriteLine("...");

            Console.WriteLine("\nReducing dimensions with UMAP...");
            var reducedEmbeddings = ReduceDimensionsWithUMAP(embeddings);

            var positions = new Vector3[reducedEmbeddings.Length];
            for (int i = 0; i < reducedEmbeddings.Length; i++)
            {
                positions[i] = new Vector3(
                    reducedEmbeddings[i][0] * scaleFactor,
                    reducedEmbeddings[i][1] * scaleFactor,
                    reducedEmbeddings[i][2] * scaleFactor
                );
            }

            return new EmbeddingData
            {
                Embeddings = embeddings,
                FilePaths = filePaths,
                Positions = positions
            };
        }

        private static float[][] ReduceDimensionsWithUMAP(float[][] vectors)
        {
            Console.WriteLine($"Running UMAP on {vectors.Length} vectors of dimension {vectors[0].Length}");
            
            var umap = new Umap(
                dimensions: 3,
                numberOfNeighbors: 5,
                random: DefaultRandomGenerator.Instance
            );

            Console.WriteLine("Initializing UMAP...");
            var numberOfEpochs = umap.InitializeFit(vectors);
            Console.WriteLine($"Running {numberOfEpochs} epochs...");
            
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            for (var i = 0; i < numberOfEpochs; i++)
            {
                umap.Step();
                if (i % 10 == 0)
                {
                    Console.WriteLine($"Epoch {i}/{numberOfEpochs} ({(i * 100.0f / numberOfEpochs):F1}%)");
                }
            }

            stopwatch.Stop();
            Console.WriteLine($"UMAP completed in {stopwatch.ElapsedMilliseconds}ms");

            return umap.GetEmbedding();
        }

        private static float[][] LoadNpyFile(string filePath, int maxEntries = -1)
        {
            using (var stream = File.OpenRead(filePath))
            using (var reader = new BinaryReader(stream))
            {
                // Read and print first 16 bytes for debugging
                byte[] initialBytes = reader.ReadBytes(16);
                Console.WriteLine("First 16 bytes of file:");
                Console.WriteLine("Hex: " + BitConverter.ToString(initialBytes));
                Console.WriteLine("ASCII: " + string.Join("", initialBytes.Select(b => b >= 32 && b <= 126 ? ((char)b).ToString() : "?")));
                
                stream.Position = 0;

                // Read NPY header
                var magicBytes = reader.ReadBytes(6);
                Console.WriteLine($"Magic bytes (hex): {BitConverter.ToString(magicBytes)}");

                // Check magic number (0x93 'NUMPY')
                if (!(magicBytes.Length == 6 && 
                      magicBytes[0] == 0x93 && 
                      magicBytes[1] == (byte)'N' && 
                      magicBytes[2] == (byte)'U' && 
                      magicBytes[3] == (byte)'M' && 
                      magicBytes[4] == (byte)'P' && 
                      magicBytes[5] == (byte)'Y'))
                {
                    throw new Exception($"Invalid NPY file format. Expected '93-4E-55-4D-50-59', got '{BitConverter.ToString(magicBytes)}'");
                }

                var major = reader.ReadByte();
                var minor = reader.ReadByte();
                Console.WriteLine($"NPY version: {major}.{minor}");
                
                // Read header length
                ushort headerLen;
                if (major == 1)
                {
                    headerLen = reader.ReadUInt16();
                    Console.WriteLine($"Header length (v1): {headerLen}");
                }
                else if (major == 2)
                {
                    headerLen = (ushort)reader.ReadUInt32();
                    Console.WriteLine($"Header length (v2): {headerLen}");
                }
                else
                    throw new Exception($"Unsupported NPY version: {major}.{minor}");

                // Read header
                var headerBytes = reader.ReadBytes(headerLen);
                var headerStr = Encoding.ASCII.GetString(headerBytes);
                Console.WriteLine($"Header string: {headerStr}");

                // Parse header dictionary
                var headerDict = ParseHeaderDict(headerStr);
                
                // Get data type and shape
                if (!headerDict.TryGetValue("descr", out var dtype))
                    throw new Exception("Missing 'descr' in header");
                if (!headerDict.TryGetValue("shape", out var shapeStr))
                    throw new Exception("Missing 'shape' in header");

                Console.WriteLine($"Data type: {dtype}");
                Console.WriteLine($"Shape string: {shapeStr}");

                // Parse shape
                var shapeParts = shapeStr.Trim('(', ')', ' ').Split(',');
                var shape = new int[shapeParts.Length];
                for (int i = 0; i < shapeParts.Length; i++)
                {
                    if (!string.IsNullOrWhiteSpace(shapeParts[i]))
                        shape[i] = int.Parse(shapeParts[i].Trim());
                }

                Console.WriteLine($"Parsed shape: [{string.Join(", ", shape)}]");

                var numElements = maxEntries > 0 ? Math.Min(maxEntries, shape[0]) : shape[0];
                var vectorSize = shape.Length > 1 ? shape[1] : 1;
                var result = new float[numElements][];

                var isLittleEndian = dtype.StartsWith("<");
                var dataType = dtype.Substring(1);
                
                Console.WriteLine($"Reading {numElements} vectors of size {vectorSize}");
                Console.WriteLine($"Data is {(isLittleEndian ? "little" : "big")} endian, type: {dataType}");

                // Try to read first few values for debugging
                try
                {
                    long dataStart = stream.Position;
                    Console.WriteLine("\nFirst few vectors (showing first 10 values each):");
                    for (int i = 0; i < Math.Min(3, numElements); i++)
                    {
                        result[i] = new float[vectorSize];
                        for (int j = 0; j < vectorSize; j++)
                        {
                            switch (dataType)
                            {
                                case "f4":
                                    result[i][j] = reader.ReadSingle();
                                    break;
                                case "f8":
                                    result[i][j] = (float)reader.ReadDouble();
                                    break;
                                default:
                                    throw new Exception($"Unsupported data type: {dataType}");
                            }
                        }
                        Console.WriteLine($"Vector {i}: [{string.Join(", ", result[i].Take(10).Select(v => v.ToString("F6")))}]...");
                    }
                    stream.Position = dataStart;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error reading sample values: {ex.Message}");
                }

                // Read all data
                for (int i = 0; i < numElements; i++)
                {
                    result[i] = new float[vectorSize];
                    for (int j = 0; j < vectorSize; j++)
                    {
                        switch (dataType)
                        {
                            case "f4":
                                result[i][j] = reader.ReadSingle();
                                break;
                            case "f8":
                                result[i][j] = (float)reader.ReadDouble();
                                break;
                            default:
                                throw new Exception($"Unsupported data type: {dataType}");
                        }
                    }
                }

                return result;
            }
        }

        private static Dictionary<string, string> ParseHeaderDict(string headerStr)
        {
            var dict = new Dictionary<string, string>();
            var parts = headerStr.Trim('{', '}', ' ').Split(',');
            
            foreach (var part in parts)
            {
                var keyValue = part.Split(':');
                if (keyValue.Length == 2)
                {
                    var key = keyValue[0].Trim().Trim('\'');
                    var value = keyValue[1].Trim().Trim('\'');
                    dict[key] = value;
                }
            }
            
            return dict;
        }

        private static string[] LoadNpyStrings(string filePath, int maxEntries = -1)
        {
            using (var stream = File.OpenRead(filePath))
            using (var reader = new BinaryReader(stream))
            {
                // Read and print first 16 bytes for debugging
                byte[] initialBytes = reader.ReadBytes(16);
                Console.WriteLine("First 16 bytes of file paths file:");
                Console.WriteLine("Hex: " + BitConverter.ToString(initialBytes));
                Console.WriteLine("ASCII: " + string.Join("", initialBytes.Select(b => b >= 32 && b <= 126 ? ((char)b).ToString() : "?")));
                
                stream.Position = 0;

                // Read NPY header
                var magicBytes = reader.ReadBytes(6);
                if (!(magicBytes.Length == 6 && 
                      magicBytes[0] == 0x93 && 
                      magicBytes[1] == (byte)'N' && 
                      magicBytes[2] == (byte)'U' && 
                      magicBytes[3] == (byte)'M' && 
                      magicBytes[4] == (byte)'P' && 
                      magicBytes[5] == (byte)'Y'))
                {
                    throw new Exception($"Invalid NPY file format. Expected '93-4E-55-4D-50-59', got '{BitConverter.ToString(magicBytes)}'");
                }

                var major = reader.ReadByte();
                var minor = reader.ReadByte();
                Console.WriteLine($"NPY version: {major}.{minor}");
                
                // Read header length
                ushort headerLen;
                if (major == 1)
                {
                    headerLen = reader.ReadUInt16();
                    Console.WriteLine($"Header length (v1): {headerLen}");
                }
                else if (major == 2)
                {
                    headerLen = (ushort)reader.ReadUInt32();
                    Console.WriteLine($"Header length (v2): {headerLen}");
                }
                else
                    throw new Exception($"Unsupported NPY version: {major}.{minor}");

                // Read header
                var headerBytes = reader.ReadBytes(headerLen);
                var headerStr = Encoding.ASCII.GetString(headerBytes);
                Console.WriteLine($"Header string: {headerStr}");

                // Parse header dictionary
                var headerDict = ParseHeaderDict(headerStr);
                
                // Get data type and shape
                if (!headerDict.TryGetValue("descr", out var dtype))
                    throw new Exception("Missing 'descr' in header");
                if (!headerDict.TryGetValue("shape", out var shapeStr))
                    throw new Exception("Missing 'shape' in header");

                Console.WriteLine($"Data type: {dtype}");
                Console.WriteLine($"Shape string: {shapeStr}");

                // Parse shape
                var shapeParts = shapeStr.Trim('(', ')', ' ').Split(',');
                var numStrings = maxEntries > 0 ? Math.Min(maxEntries, int.Parse(shapeParts[0].Trim())) : int.Parse(shapeParts[0].Trim());
                Console.WriteLine($"Number of strings to read: {numStrings}");

                var strings = new string[numStrings];
                var charCount = int.Parse(dtype.Substring(2)); // Get the character count from <U112
                Console.WriteLine($"String length from dtype: {charCount}");

                for (int i = 0; i < numStrings; i++)
                {
                    var bytes = new byte[charCount * 4];
                    reader.Read(bytes, 0, bytes.Length);
                    
                    // Convert UTF-32 little endian to string
                    var chars = new List<char>();
                    for (int j = 0; j < bytes.Length; j += 4)
                    {
                        int codePoint = bytes[j] | (bytes[j + 1] << 8) | (bytes[j + 2] << 16) | (bytes[j + 3] << 24);
                        if (codePoint == 0) break; // End of string
                        chars.Add((char)codePoint);
                    }
                    strings[i] = new string(chars.ToArray());

                    if (i < 3)
                    {
                        Console.WriteLine($"String {i}: {strings[i]}");
                    }
                }

                return strings;
            }
        }
    }
} 