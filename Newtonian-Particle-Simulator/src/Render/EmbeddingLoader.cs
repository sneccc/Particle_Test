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

        public static EmbeddingData LoadFromFolder(string folderPath, float scaleFactor = 1.0f, int maxEntries = 0)
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
            
            if (embeddings[0].Length != 3)
            {
                throw new Exception($"Expected 3D coordinates in NPY file, but got {embeddings[0].Length} dimensions");
            }

            var positions = new Vector3[embeddings.Length];
            for (int i = 0; i < embeddings.Length; i++)
            {
                positions[i] = new Vector3(
                    embeddings[i][0] * scaleFactor,
                    embeddings[i][1] * scaleFactor,
                    embeddings[i][2] * scaleFactor
                );
            }

            return new EmbeddingData
            {
                Embeddings = embeddings,
                FilePaths = filePaths,
                Positions = positions
            };
        }

        private static float[][] NormalizeEmbeddings(float[][] embeddings)
        {
            var normalizedEmbeddings = new float[embeddings.Length][];
            
            for (int i = 0; i < embeddings.Length; i++)
            {
                normalizedEmbeddings[i] = new float[embeddings[i].Length];
                
                // Calculate magnitude
                float magnitude = 0;
                for (int j = 0; j < embeddings[i].Length; j++)
                {
                    magnitude += embeddings[i][j] * embeddings[i][j];
                }
                magnitude = (float)Math.Sqrt(magnitude);
                
                // Normalize
                if (magnitude > 0)
                {
                    for (int j = 0; j < embeddings[i].Length; j++)
                    {
                        normalizedEmbeddings[i][j] = embeddings[i][j] / magnitude;
                    }
                }
                else
                {
                    Array.Copy(embeddings[i], normalizedEmbeddings[i], embeddings[i].Length);
                }
            }
            
            return normalizedEmbeddings;
        }

        private static float[][] ReduceDimensionsWithUMAP(float[][] vectors)
        {
            Console.WriteLine($"Running UMAP on {vectors.Length} vectors of dimension {vectors[0].Length}");
            
            var umap = new Umap(
                dimensions: 3,
                numberOfNeighbors: 15,  // Increased from 5 to 15 for better global structure
                random: DefaultRandomGenerator.Instance,
                customNumberOfEpochs: 1000
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
                Console.WriteLine($"Raw shape string bytes: {BitConverter.ToString(Encoding.ASCII.GetBytes(shapeStr))}");

                // Parse shape - handle both (N, 3) and (N,) formats
                Console.WriteLine($"Before replace: '{shapeStr}'");
                var noSpaces = shapeStr.Replace(" ", "");
                Console.WriteLine($"After space removal: '{noSpaces}'");
                var trimmed = noSpaces.Trim('(', ')', ' ');
                Console.WriteLine($"After trim: '{trimmed}'");
                
                var shapeParts = trimmed.Split(',')
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToArray();

                Console.WriteLine($"Shape parts after split: [{string.Join("', '", shapeParts)}]");
                
                // Parse the numbers from the shape parts
                var shape = shapeParts.Select(s => 
                {
                    Console.WriteLine($"Parsing shape part: '{s}'");
                    return int.Parse(s.Trim());
                }).ToArray();
                Console.WriteLine($"Parsed shape: [{string.Join(", ", shape)}]");

                // For embeddings file, we expect (N, 3)
                if (Path.GetFileName(filePath) == "image_embeddings.npy")
                {
                    if (shape.Length != 2 || shape[1] != 3)
                    {
                        throw new Exception($"Expected embeddings array with shape (N, 3), got shape: [{string.Join(", ", shape)}], original shape string: '{shapeStr}'");
                    }
                }

                var numElements = maxEntries > 0 ? Math.Min(maxEntries, shape[0]) : shape[0];
                var vectorSize = shape[1];  // We know it's a 2D array now
                var result = new float[numElements][];

                var isLittleEndian = dtype.StartsWith("<");
                var dataType = dtype.Substring(1);
                
                Console.WriteLine($"Reading {numElements} vectors of size {vectorSize}");
                Console.WriteLine($"Data is {(isLittleEndian ? "little" : "big")} endian, type: {dataType}");

                long dataStart = stream.Position;

                // Try to read first few values for debugging
                try
                {
                    Console.WriteLine("\nFirst few vectors (showing all values):");
                    for (int i = 0; i < Math.Min(3, numElements); i++)
                    {
                        result[i] = new float[vectorSize];  // Initialize the array
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
                        Console.WriteLine($"Vector {i}: [{string.Join(", ", result[i].Select(v => v.ToString("F6")))}]");
                    }
                    stream.Position = dataStart;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error reading sample values: {ex.Message}");
                    stream.Position = dataStart;  // Make sure we reset position even on error
                }

                Console.WriteLine("\nInitializing arrays...");
                // Initialize all arrays before reading
                for (int i = 0; i < numElements; i++)
                {
                    result[i] = new float[vectorSize];
                }

                Console.WriteLine("Reading all embeddings...");
                // Read all data
                for (int i = 0; i < numElements; i++)
                {
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
                    
                    // Add progress indicator for large datasets
                    if (i % 1000 == 0 || i == numElements - 1)
                    {
                        Console.WriteLine($"Loading embeddings: {i + 1}/{numElements} ({((i + 1) * 100.0f / numElements):F1}%)");
                    }
                }

                Console.WriteLine("Finished reading embeddings.");

                return result;
            }
        }

        private static Dictionary<string, string> ParseHeaderDict(string headerStr)
        {
            var dict = new Dictionary<string, string>();
            Console.WriteLine($"Parsing header: '{headerStr}'");
            
            // Remove the curly braces
            var content = headerStr.Trim('{', '}', ' ');
            
            // Split by comma but not within parentheses
            var parts = new List<string>();
            var currentPart = "";
            var parenthesesCount = 0;
            
            for (int i = 0; i < content.Length; i++)
            {
                char c = content[i];
                if (c == '(') parenthesesCount++;
                else if (c == ')') parenthesesCount--;
                
                if (c == ',' && parenthesesCount == 0)
                {
                    if (!string.IsNullOrWhiteSpace(currentPart))
                        parts.Add(currentPart);
                    currentPart = "";
                }
                else
                {
                    currentPart += c;
                }
            }
            if (!string.IsNullOrWhiteSpace(currentPart))
                parts.Add(currentPart);

            foreach (var part in parts)
            {
                Console.WriteLine($"Processing part: '{part}'");
                var keyValue = part.Split(new[] { ':' }, 2);
                if (keyValue.Length == 2)
                {
                    var key = keyValue[0].Trim().Trim('\'');
                    var value = keyValue[1].Trim().Trim('\'');
                    Console.WriteLine($"Key: '{key}', Value: '{value}'");
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
                Console.WriteLine($"Raw shape string bytes: {BitConverter.ToString(Encoding.ASCII.GetBytes(shapeStr))}");

                // Parse shape - handle both (N, 3) and (N,) formats
                Console.WriteLine($"Before replace: '{shapeStr}'");
                var noSpaces = shapeStr.Replace(" ", "");
                Console.WriteLine($"After space removal: '{noSpaces}'");
                var trimmed = noSpaces.Trim('(', ')', ' ');
                Console.WriteLine($"After trim: '{trimmed}'");
                
                var shapeParts = trimmed.Split(',')
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToArray();

                Console.WriteLine($"Shape parts after split: [{string.Join("', '", shapeParts)}]");
                
                // Parse the numbers from the shape parts
                var shape = shapeParts.Select(s => 
                {
                    Console.WriteLine($"Parsing shape part: '{s}'");
                    return int.Parse(s.Trim());
                }).ToArray();
                Console.WriteLine($"Parsed shape: [{string.Join(", ", shape)}]");

                // For embeddings file, we expect (N, 3)
                if (Path.GetFileName(filePath) == "image_embeddings.npy")
                {
                    if (shape.Length != 2 || shape[1] != 3)
                    {
                        throw new Exception($"Expected embeddings array with shape (N, 3), got shape: [{string.Join(", ", shape)}], original shape string: '{shapeStr}'");
                    }
                }

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