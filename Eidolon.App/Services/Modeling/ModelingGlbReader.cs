using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;

namespace Eidolon.App.Services.Modeling
{
    internal static class ModelingGlbReader
    {
        internal const int VertexComponentCount = 9;
        private const int MaximumFileBytes = 128 * 1024 * 1024;
        private const int MaximumJsonBytes = 8 * 1024 * 1024;
        private const int MaximumDrawVertices = 1_500_000;
        private const int MaximumSceneEntries = 8192;
        private const int MaximumNodeDepth = 64;
        private const float MaximumCoordinate = 1e12f;
        private const uint GlbMagic = 0x46546C67;
        private const uint JsonChunk = 0x4E4F534A;
        private const uint BinaryChunk = 0x004E4942;
        private const int UnsignedByte = 5121;
        private const int UnsignedShort = 5123;
        private const int UnsignedInt = 5125;
        private const int Float = 5126;

        internal static float[] Read(string modelPath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Path.IsPathFullyQualified(modelPath) == false)
            {
                throw new InvalidDataException("The GLB viewer needs an absolute file path.");
            }
            string fullPath = Path.GetFullPath(modelPath);
            if (Path.GetExtension(fullPath).Equals(".glb", StringComparison.OrdinalIgnoreCase) == false)
            {
                throw new InvalidDataException("The model file must be a GLB.");
            }
            if (OperatingSystem.IsWindows() == true)
            {
                if (fullPath.StartsWith(@"\\", StringComparison.Ordinal) == true)
                {
                    throw new NotSupportedException("The GLB viewer needs a local file.");
                }
            }
            FileInfo file = new FileInfo(fullPath);
            if (file.Exists == false)
            {
                throw new FileNotFoundException("The GLB model file does not exist.");
            }
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new NotSupportedException("The GLB viewer does not open linked model files.");
            }
            long fileLength = file.Length;
            if (fileLength < 28)
            {
                throw new InvalidDataException("The GLB header or chunks are incomplete.");
            }
            if (fileLength > MaximumFileBytes)
            {
                throw new InvalidDataException("The GLB exceeds the viewer's file size limit.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            using FileStream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
            if (stream.Length != fileLength)
            {
                throw new InvalidDataException("The GLB size changed before reading.");
            }
            Span<byte> header = stackalloc byte[12];
            stream.ReadExactly(header);
            if (BinaryPrimitives.ReadUInt32LittleEndian(header) != GlbMagic)
            {
                throw new InvalidDataException("The GLB signature is invalid.");
            }
            if (BinaryPrimitives.ReadUInt32LittleEndian(header[4..]) != 2)
            {
                throw new NotSupportedException("Only GLB 2.0 is supported.");
            }
            if (BinaryPrimitives.ReadUInt32LittleEndian(header[8..]) != fileLength)
            {
                throw new InvalidDataException("The declared GLB length does not match the file.");
            }
            int jsonLength = ReadChunkHeader(stream, fileLength, out uint chunkType);
            if (chunkType != JsonChunk)
            {
                throw new InvalidDataException("The first GLB chunk must contain JSON.");
            }
            if (jsonLength > MaximumJsonBytes)
            {
                throw new InvalidDataException("The GLB JSON exceeds the viewer's size limit.");
            }
            byte[] json = ReadBytes(stream, jsonLength, cancellationToken);
            int binaryLength = ReadChunkHeader(stream, fileLength, out chunkType);
            if (chunkType != BinaryChunk)
            {
                throw new InvalidDataException("The GLB has no embedded binary buffer.");
            }
            byte[] binary = ReadBytes(stream, binaryLength, cancellationToken);
            while (stream.Position < fileLength)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int chunkLength = ReadChunkHeader(stream, fileLength, out chunkType);
                if (chunkType == JsonChunk)
                {
                    throw new InvalidDataException("The GLB contains more than one JSON chunk.");
                }
                if (chunkType == BinaryChunk)
                {
                    throw new InvalidDataException("The GLB contains more than one binary chunk.");
                }
                stream.Position += chunkLength;
            }
            cancellationToken.ThrowIfCancellationRequested();
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
            return ReadScene(document.RootElement, binary, cancellationToken);
        }

        private static int ReadChunkHeader(FileStream stream, long fileLength, out uint chunkType)
        {
            Span<byte> chunkHeader = stackalloc byte[8];
            stream.ReadExactly(chunkHeader);
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader);
            chunkType = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader[4..]);
            if (length % 4 != 0)
            {
                throw new InvalidDataException("The GLB chunk is not four byte aligned.");
            }
            if (length > fileLength - stream.Position)
            {
                throw new InvalidDataException("The GLB chunk extends beyond the file.");
            }
            return (int)length;
        }

        private static byte[] ReadBytes(FileStream stream, int length, CancellationToken cancellationToken)
        {
            byte[] data = new byte[length];
            for (int offset = 0; offset < length;)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int readLength = Math.Min(length - offset, 65536);
                stream.ReadExactly(data.AsSpan(offset, readLength));
                offset += readLength;
            }
            return data;
        }

        private static float[] ReadScene(JsonElement root, byte[] binary, CancellationToken cancellationToken)
        {
            if (root.GetProperty("asset").GetProperty("version").GetString() != "2.0")
            {
                throw new NotSupportedException("Only glTF 2.0 assets are supported.");
            }
            if (root.TryGetProperty("extensionsRequired", out JsonElement requiredExtensions) == true)
            {
                if (requiredExtensions.GetArrayLength() != 0)
                {
                    throw new NotSupportedException("The GLB requires unsupported extensions.");
                }
            }
            JsonElement buffers = ReadArray(root, "buffers", 1);
            JsonElement buffer = ReadArrayItem(buffers, 0);
            if (buffer.TryGetProperty("uri", out _) == true)
            {
                throw new NotSupportedException("External GLB buffers are not supported.");
            }
            int bufferLength = buffer.GetProperty("byteLength").GetInt32();
            if (bufferLength <= 0)
            {
                throw new InvalidDataException("The GLB binary buffer is empty.");
            }
            if (bufferLength > binary.Length)
            {
                throw new InvalidDataException("The GLB binary buffer is truncated.");
            }
            if (binary.Length - bufferLength > 3)
            {
                throw new InvalidDataException("The GLB binary buffer has invalid padding.");
            }
            JsonElement scenes = ReadArray(root, "scenes", MaximumSceneEntries);
            JsonElement nodes = ReadArray(root, "nodes", MaximumSceneEntries);
            JsonElement meshes = ReadArray(root, "meshes", MaximumSceneEntries);
            JsonElement accessors = ReadArray(root, "accessors", MaximumSceneEntries);
            JsonElement bufferViews = ReadArray(root, "bufferViews", MaximumSceneEntries);
            JsonElement materials = default;
            if (root.TryGetProperty("materials", out _) == true)
            {
                materials = ReadArray(root, "materials", MaximumSceneEntries);
            }
            JsonElement scene = ReadArrayItem(scenes, ReadOptionalInt(root, "scene", 0));
            bool[] visited = new bool[nodes.GetArrayLength()];
            List<(JsonElement Primitive, Matrix4x4 Transform, int VertexCount)> primitives = new List<(JsonElement Primitive, Matrix4x4 Transform, int VertexCount)>();
            int drawVertexCount = 0;
            foreach (JsonElement sceneNode in ReadArray(scene, "nodes", MaximumSceneEntries).EnumerateArray())
            {
                VisitNode(sceneNode.GetInt32(), Matrix4x4.Identity, 0);
            }
            if (drawVertexCount == 0)
            {
                throw new InvalidDataException("The active GLB scene contains no triangles.");
            }
            float[] vertices = new float[drawVertexCount * VertexComponentCount];
            Vector3 minimum = new Vector3(float.PositiveInfinity);
            Vector3 maximum = new Vector3(float.NegativeInfinity);
            int outputOffset = 0;
            foreach (var entry in primitives)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CopyPrimitive(entry.Primitive, entry.Transform, entry.VertexCount, materials, accessors, bufferViews, binary, bufferLength, vertices, ref outputOffset, ref minimum, ref maximum, cancellationToken);
            }
            FitBounds(vertices, minimum, maximum, cancellationToken);
            return vertices;

            void VisitNode(int nodeIndex, Matrix4x4 parentTransform, int depth)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (depth > MaximumNodeDepth)
                {
                    throw new InvalidDataException("The GLB node hierarchy is too deep.");
                }
                JsonElement node = ReadArrayItem(nodes, nodeIndex);
                if (visited[nodeIndex] == true)
                {
                    throw new InvalidDataException("The active GLB scene repeats a node.");
                }
                visited[nodeIndex] = true;
                if (node.TryGetProperty("skin", out _) == true)
                {
                    throw new NotSupportedException("Skinned GLB meshes are not supported.");
                }
                Matrix4x4 worldTransform = ReadTransform(node) * parentTransform;
                if (node.TryGetProperty("mesh", out JsonElement meshIndex) == true)
                {
                    JsonElement mesh = ReadArrayItem(meshes, meshIndex.GetInt32());
                    foreach (JsonElement primitive in ReadArray(mesh, "primitives", MaximumSceneEntries).EnumerateArray())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (primitives.Count >= MaximumSceneEntries)
                        {
                            throw new InvalidDataException("The GLB has too many visible primitives.");
                        }
                        if (ReadOptionalInt(primitive, "mode", 4) != 4)
                        {
                            throw new NotSupportedException("Only GLB triangle primitives are supported.");
                        }
                        if (primitive.TryGetProperty("targets", out _) == true)
                        {
                            throw new NotSupportedException("GLB morph targets are not supported.");
                        }
                        RejectExtension(primitive, "KHR_draco_mesh_compression");
                        int accessorIndex = primitive.GetProperty("attributes").GetProperty("POSITION").GetInt32();
                        if (primitive.TryGetProperty("indices", out JsonElement indices) == true)
                        {
                            accessorIndex = indices.GetInt32();
                        }
                        int vertexCount = ReadArrayItem(accessors, accessorIndex).GetProperty("count").GetInt32();
                        if (vertexCount <= 0)
                        {
                            throw new InvalidDataException("A GLB triangle primitive is empty.");
                        }
                        if (vertexCount % 3 != 0)
                        {
                            throw new InvalidDataException("A GLB triangle primitive has an incomplete triangle.");
                        }
                        if (vertexCount > MaximumDrawVertices - drawVertexCount)
                        {
                            throw new InvalidDataException("The GLB exceeds the viewer's triangle limit.");
                        }
                        drawVertexCount += vertexCount;
                        primitives.Add((primitive, worldTransform, vertexCount));
                    }
                }
                if (node.TryGetProperty("children", out _) == true)
                {
                    foreach (JsonElement child in ReadArray(node, "children", MaximumSceneEntries).EnumerateArray())
                    {
                        VisitNode(child.GetInt32(), worldTransform, depth + 1);
                    }
                }
            }
        }

        private static Matrix4x4 ReadTransform(JsonElement node)
        {
            if (node.TryGetProperty("matrix", out JsonElement matrix) == true)
            {
                Span<float> values = stackalloc float[16];
                ReadComponents(matrix, values);
                if (values[3] != 0)
                {
                    throw new InvalidDataException("The GLB node matrix is not affine.");
                }
                if (values[7] != 0)
                {
                    throw new InvalidDataException("The GLB node matrix is not affine.");
                }
                if (values[11] != 0)
                {
                    throw new InvalidDataException("The GLB node matrix is not affine.");
                }
                if (values[15] != 1)
                {
                    throw new InvalidDataException("The GLB node matrix is not affine.");
                }
                // glTF columns are the rows of the equivalent System.Numerics transform.
                return new Matrix4x4(values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7], values[8], values[9], values[10], values[11], values[12], values[13], values[14], values[15]);
            }
            Vector3 translation = ReadOptionalVector(node, "translation", Vector3.Zero);
            Vector3 scale = ReadOptionalVector(node, "scale", Vector3.One);
            Quaternion rotation = Quaternion.Identity;
            if (node.TryGetProperty("rotation", out JsonElement rotationElement) == true)
            {
                Span<float> values = stackalloc float[4];
                ReadComponents(rotationElement, values);
                rotation = new Quaternion(values[0], values[1], values[2], values[3]);
                if (MathF.Abs(rotation.LengthSquared() - 1) > 0.01f)
                {
                    throw new InvalidDataException("The GLB node rotation is not a unit quaternion.");
                }
                rotation = Quaternion.Normalize(rotation);
            }
            return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(translation);
        }

        private static Vector3 ReadOptionalVector(JsonElement parent, string name, Vector3 fallback)
        {
            if (parent.TryGetProperty(name, out JsonElement element) == false)
            {
                return fallback;
            }
            Span<float> values = stackalloc float[3];
            ReadComponents(element, values);
            return new Vector3(values[0], values[1], values[2]);
        }

        private static void CopyPrimitive(JsonElement primitive, Matrix4x4 transform, int vertexCount, JsonElement materials, JsonElement accessors, JsonElement bufferViews, byte[] binary, int bufferLength, float[] vertices, ref int outputOffset, ref Vector3 minimum, ref Vector3 maximum, CancellationToken cancellationToken)
        {
            JsonElement attributes = primitive.GetProperty("attributes");
            var positions = ReadAccessor(accessors, bufferViews, attributes.GetProperty("POSITION").GetInt32(), "VEC3", 3, bufferLength);
            if (positions.ComponentType != Float)
            {
                throw new NotSupportedException("GLB positions must contain floats.");
            }
            (int Offset, int Stride, int Count, int ComponentType) normals = default;
            if (attributes.TryGetProperty("NORMAL", out JsonElement normalIndex) == true)
            {
                normals = ReadAccessor(accessors, bufferViews, normalIndex.GetInt32(), "VEC3", 3, bufferLength);
                if (normals.ComponentType != Float)
                {
                    throw new NotSupportedException("GLB normals must contain floats.");
                }
                if (normals.Count != positions.Count)
                {
                    throw new InvalidDataException("GLB position and normal counts differ.");
                }
            }
            (int Offset, int Stride, int Count, int ComponentType) indices = default;
            if (primitive.TryGetProperty("indices", out JsonElement indexAccessor) == true)
            {
                indices = ReadAccessor(accessors, bufferViews, indexAccessor.GetInt32(), "SCALAR", 1, bufferLength);
                if (indices.ComponentType == Float)
                {
                    throw new NotSupportedException("GLB indices must contain unsigned integers.");
                }
                if (indices.Stride != ComponentSize(indices.ComponentType))
                {
                    throw new InvalidDataException("GLB indices must be tightly packed.");
                }
            }
            bool hasNormalTransform = Matrix4x4.Invert(transform, out Matrix4x4 inverse);
            Matrix4x4 normalTransform = Matrix4x4.Transpose(inverse);
            bool mirrored = transform.GetDeterminant() < 0;
            Vector3 color = ReadMaterialColor(primitive, materials);
            Span<int> triangleIndices = stackalloc int[3];
            Span<Vector3> trianglePositions = stackalloc Vector3[3];
            for (int triangle = 0; triangle < vertexCount; triangle += 3)
            {
                if (triangle % 3072 == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
                for (int corner = 0; corner < 3; corner++)
                {
                    int vertexIndex = triangle + corner;
                    if (indices.Count > 0)
                    {
                        int indexOffset = indices.Offset + vertexIndex * indices.Stride;
                        uint index = ReadIndex(binary, indexOffset, indices.ComponentType);
                        if (index >= (uint)positions.Count)
                        {
                            throw new InvalidDataException("A GLB index is outside its position accessor.");
                        }
                        vertexIndex = (int)index;
                    }
                    triangleIndices[corner] = vertexIndex;
                    Vector3 position = ReadVector(binary, positions.Offset + vertexIndex * positions.Stride);
                    Vector3 transformed = Vector3.Transform(position, transform);
                    RequireFiniteCoordinate(transformed.X);
                    RequireFiniteCoordinate(transformed.Y);
                    RequireFiniteCoordinate(transformed.Z);
                    trianglePositions[corner] = transformed;
                }
                if (mirrored == true)
                {
                    int secondIndex = triangleIndices[1];
                    triangleIndices[1] = triangleIndices[2];
                    triangleIndices[2] = secondIndex;
                    Vector3 secondPosition = trianglePositions[1];
                    trianglePositions[1] = trianglePositions[2];
                    trianglePositions[2] = secondPosition;
                }
                Vector3 faceNormal = Normalize(Vector3.Cross(trianglePositions[1] - trianglePositions[0], trianglePositions[2] - trianglePositions[0]), Vector3.UnitY);
                for (int corner = 0; corner < 3; corner++)
                {
                    Vector3 position = trianglePositions[corner];
                    Vector3 normal = faceNormal;
                    if (normals.Count > 0)
                    {
                        if (hasNormalTransform == true)
                        {
                            Vector3 sourceNormal = ReadVector(binary, normals.Offset + triangleIndices[corner] * normals.Stride);
                            normal = Normalize(Vector3.TransformNormal(sourceNormal, normalTransform), faceNormal);
                        }
                    }
                    minimum = Vector3.Min(minimum, position);
                    maximum = Vector3.Max(maximum, position);
                    vertices[outputOffset++] = position.X;
                    vertices[outputOffset++] = position.Y;
                    vertices[outputOffset++] = position.Z;
                    vertices[outputOffset++] = normal.X;
                    vertices[outputOffset++] = normal.Y;
                    vertices[outputOffset++] = normal.Z;
                    vertices[outputOffset++] = color.X;
                    vertices[outputOffset++] = color.Y;
                    vertices[outputOffset++] = color.Z;
                }
            }
        }

        private static (int Offset, int Stride, int Count, int ComponentType) ReadAccessor(JsonElement accessors, JsonElement bufferViews, int index, string elementType, int components, int bufferLength)
        {
            JsonElement accessor = ReadArrayItem(accessors, index);
            if (accessor.GetProperty("type").GetString() != elementType)
            {
                throw new InvalidDataException("A GLB accessor has the wrong element type.");
            }
            if (accessor.TryGetProperty("sparse", out _) == true)
            {
                throw new NotSupportedException("Sparse GLB accessors are not supported.");
            }
            if (accessor.TryGetProperty("normalized", out JsonElement normalized) == true)
            {
                if (normalized.GetBoolean() == true)
                {
                    throw new NotSupportedException("Normalized GLB geometry accessors are not supported.");
                }
            }
            int count = accessor.GetProperty("count").GetInt32();
            if (count <= 0)
            {
                throw new InvalidDataException("A GLB accessor has no elements.");
            }
            if (count > MaximumDrawVertices)
            {
                throw new InvalidDataException("A GLB accessor exceeds the viewer's element limit.");
            }
            int componentType = accessor.GetProperty("componentType").GetInt32();
            int componentSize = ComponentSize(componentType);
            int elementSize = componentSize * components;
            JsonElement view = ReadArrayItem(bufferViews, accessor.GetProperty("bufferView").GetInt32());
            RejectExtension(view, "EXT_meshopt_compression");
            if (view.GetProperty("buffer").GetInt32() != 0)
            {
                throw new NotSupportedException("Only the embedded GLB buffer is supported.");
            }
            int viewOffset = ReadOptionalInt(view, "byteOffset", 0);
            int viewLength = view.GetProperty("byteLength").GetInt32();
            if (viewOffset < 0)
            {
                throw new InvalidDataException("A GLB buffer view has a negative offset.");
            }
            if (viewOffset > bufferLength)
            {
                throw new InvalidDataException("A GLB buffer view starts beyond the embedded buffer.");
            }
            if (viewLength <= 0)
            {
                throw new InvalidDataException("A GLB buffer view has an invalid length.");
            }
            if (viewLength > bufferLength - viewOffset)
            {
                throw new InvalidDataException("A GLB buffer view extends beyond the embedded buffer.");
            }
            int offset = ReadOptionalInt(accessor, "byteOffset", 0);
            if (offset < 0)
            {
                throw new InvalidDataException("A GLB accessor has a negative offset.");
            }
            if (offset > viewLength)
            {
                throw new InvalidDataException("A GLB accessor starts beyond its buffer view.");
            }
            if (offset % componentSize != 0)
            {
                throw new InvalidDataException("A GLB accessor has an unaligned offset.");
            }
            int absoluteOffset = viewOffset + offset;
            if (absoluteOffset % componentSize != 0)
            {
                throw new InvalidDataException("A GLB buffer view has an unaligned accessor.");
            }
            int stride = elementSize;
            if (view.TryGetProperty("byteStride", out JsonElement strideElement) == true)
            {
                stride = strideElement.GetInt32();
                if (stride % 4 != 0)
                {
                    throw new InvalidDataException("A GLB accessor has an unaligned stride.");
                }
            }
            if (stride < elementSize)
            {
                throw new InvalidDataException("A GLB accessor stride is smaller than its element.");
            }
            if (stride > 252)
            {
                throw new InvalidDataException("A GLB accessor stride is too large.");
            }
            if ((long)(count - 1) * stride + elementSize > viewLength - offset)
            {
                throw new InvalidDataException("A GLB accessor extends beyond its buffer view.");
            }
            return (absoluteOffset, stride, count, componentType);
        }

        private static int ComponentSize(int componentType)
        {
            switch (componentType)
            {
                case UnsignedByte:
                    return 1;
                case UnsignedShort:
                    return 2;
                case UnsignedInt:
                case Float:
                    return 4;
                default:
                    throw new NotSupportedException("The GLB geometry uses an unsupported component type.");
            }
        }

        private static uint ReadIndex(byte[] binary, int offset, int componentType)
        {
            switch (componentType)
            {
                case UnsignedByte:
                    return binary[offset];
                case UnsignedShort:
                    return BinaryPrimitives.ReadUInt16LittleEndian(binary.AsSpan(offset, 2));
                case UnsignedInt:
                    return BinaryPrimitives.ReadUInt32LittleEndian(binary.AsSpan(offset, 4));
                default:
                    throw new NotSupportedException("The GLB index type is not supported.");
            }
        }

        private static Vector3 ReadVector(byte[] binary, int offset)
        {
            float first = BinaryPrimitives.ReadSingleLittleEndian(binary.AsSpan(offset, 4));
            float second = BinaryPrimitives.ReadSingleLittleEndian(binary.AsSpan(offset + 4, 4));
            float third = BinaryPrimitives.ReadSingleLittleEndian(binary.AsSpan(offset + 8, 4));
            RequireFiniteCoordinate(first);
            RequireFiniteCoordinate(second);
            RequireFiniteCoordinate(third);
            return new Vector3(first, second, third);
        }

        private static Vector3 ReadMaterialColor(JsonElement primitive, JsonElement materials)
        {
            if (primitive.TryGetProperty("material", out JsonElement materialIndex) == false)
            {
                return Vector3.One;
            }
            JsonElement material = ReadArrayItem(materials, materialIndex.GetInt32());
            if (material.TryGetProperty("pbrMetallicRoughness", out JsonElement properties) == false)
            {
                return Vector3.One;
            }
            if (properties.TryGetProperty("baseColorFactor", out JsonElement color) == false)
            {
                return Vector3.One;
            }
            Span<float> channels = stackalloc float[4];
            ReadComponents(color, channels);
            foreach (float channel in channels)
            {
                if (channel < 0)
                {
                    throw new InvalidDataException("A GLB base color component is below zero.");
                }
                if (channel > 1)
                {
                    throw new InvalidDataException("A GLB base color component exceeds one.");
                }
            }
            return new Vector3(channels[0], channels[1], channels[2]);
        }

        private static Vector3 Normalize(Vector3 vector, Vector3 fallback)
        {
            float magnitude = MathF.Max(MathF.Abs(vector.X), MathF.Max(MathF.Abs(vector.Y), MathF.Abs(vector.Z)));
            if (float.IsFinite(magnitude) == false)
            {
                return fallback;
            }
            if (magnitude == 0)
            {
                return fallback;
            }
            return Vector3.Normalize(vector / magnitude);
        }

        private static void FitBounds(float[] vertices, Vector3 minimum, Vector3 maximum, CancellationToken cancellationToken)
        {
            double centerX = ((double)minimum.X + maximum.X) / 2;
            double centerY = ((double)minimum.Y + maximum.Y) / 2;
            double centerZ = ((double)minimum.Z + maximum.Z) / 2;
            double extentX = maximum.X - centerX;
            double extentY = maximum.Y - centerY;
            double extentZ = maximum.Z - centerZ;
            double radius = Math.Sqrt(extentX * extentX + extentY * extentY + extentZ * extentZ);
            if (radius <= 0)
            {
                throw new InvalidDataException("The GLB model has no spatial extent.");
            }
            for (int offset = 0; offset < vertices.Length; offset += VertexComponentCount)
            {
                if (offset % (1024 * VertexComponentCount) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
                vertices[offset] = (float)((vertices[offset] - centerX) / radius);
                vertices[offset + 1] = (float)((vertices[offset + 1] - centerY) / radius);
                vertices[offset + 2] = (float)((vertices[offset + 2] - centerZ) / radius);
            }
        }

        private static void ReadComponents(JsonElement element, Span<float> values)
        {
            if (element.GetArrayLength() != values.Length)
            {
                throw new InvalidDataException("A GLB value has the wrong number of components.");
            }
            int index = 0;
            foreach (JsonElement component in element.EnumerateArray())
            {
                float value = component.GetSingle();
                RequireFiniteCoordinate(value);
                values[index++] = value;
            }
        }

        private static void RequireFiniteCoordinate(float value)
        {
            if (float.IsFinite(value) == false)
            {
                throw new InvalidDataException("The GLB contains a nonfinite number.");
            }
            if (MathF.Abs(value) > MaximumCoordinate)
            {
                throw new InvalidDataException("The GLB contains a number outside the viewer's range.");
            }
        }

        private static JsonElement ReadArray(JsonElement parent, string name, int maximumCount)
        {
            JsonElement array = parent.GetProperty(name);
            if (array.GetArrayLength() > maximumCount)
            {
                throw new InvalidDataException("A GLB collection exceeds the viewer's size limit.");
            }
            return array;
        }

        private static JsonElement ReadArrayItem(JsonElement array, int index)
        {
            if (array.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("A GLB collection is missing.");
            }
            if (index < 0)
            {
                throw new InvalidDataException("A GLB reference has a negative index.");
            }
            if (index >= array.GetArrayLength())
            {
                throw new InvalidDataException("A GLB reference is outside its collection.");
            }
            return array[index];
        }

        private static int ReadOptionalInt(JsonElement parent, string name, int fallback)
        {
            if (parent.TryGetProperty(name, out JsonElement value) == true)
            {
                return value.GetInt32();
            }
            return fallback;
        }

        private static void RejectExtension(JsonElement parent, string name)
        {
            if (parent.TryGetProperty("extensions", out JsonElement extensions) == true)
            {
                if (extensions.TryGetProperty(name, out _) == true)
                {
                    throw new NotSupportedException("The GLB uses an unsupported geometry compression extension.");
                }
            }
        }
    }
}
