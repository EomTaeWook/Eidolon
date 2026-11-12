using System.Runtime.InteropServices;
using Avalonia.OpenGL;
using static Avalonia.OpenGL.GlConsts;

namespace Eidolon.App.Services.Modeling
{
    internal class ModelingGlbRenderer
    {
        private const int PositionAttribute = 0;
        private const int NormalAttribute = 1;
        private const int ColorAttribute = 2;
        private const int Blend = 0x0BE2;
        private const int Triangles = 0x0004;
        private const float FitRadius = 1.12f;
        private readonly GlInterface _graphics;
        private readonly int _yawUniform;
        private readonly int _pitchUniform;
        private readonly int _distanceUniform;
        private readonly int _horizontalScaleUniform;
        private readonly int _verticalScaleUniform;
        private readonly int _depthScaleUniform;
        private readonly int _depthOffsetUniform;
        private int _program;
        private int _vertexArray;
        private int _vertexBuffer;
        private int _vertexCount;

        internal ModelingGlbRenderer(GlInterface graphics, GlVersion version)
        {
            _graphics = graphics;
            if (version.Major < 2)
            {
                throw new NotSupportedException("The GLB viewer requires OpenGL or OpenGL ES 2.0.");
            }
            try
            {
                _program = CreateProgram(version);
                _yawUniform = RequireUniform("cameraYaw");
                _pitchUniform = RequireUniform("cameraPitch");
                _distanceUniform = RequireUniform("cameraDistance");
                _horizontalScaleUniform = RequireUniform("horizontalScale");
                _verticalScaleUniform = RequireUniform("verticalScale");
                _depthScaleUniform = RequireUniform("depthScale");
                _depthOffsetUniform = RequireUniform("depthOffset");
                if (version.Major >= 3)
                {
                    _vertexArray = _graphics.GenVertexArray();
                    if (_vertexArray == 0)
                    {
                        throw new InvalidOperationException("OpenGL could not allocate the GLB vertex array.");
                    }
                }
                _vertexBuffer = _graphics.GenBuffer();
                if (_vertexBuffer == 0)
                {
                    throw new InvalidOperationException("OpenGL could not allocate the GLB vertex buffer.");
                }
                ThrowOnGraphicsError();
            }
            catch
            {
                Release();
                throw;
            }
        }

        internal void Upload(float[] vertices)
        {
            _graphics.BindBuffer(GL_ARRAY_BUFFER, _vertexBuffer);
            GCHandle pinnedVertices = GCHandle.Alloc(vertices, GCHandleType.Pinned);
            try
            {
                _graphics.BufferData(GL_ARRAY_BUFFER, new IntPtr((long)vertices.Length * sizeof(float)), pinnedVertices.AddrOfPinnedObject(), GL_STATIC_DRAW);
                ThrowOnGraphicsError();
                _vertexCount = vertices.Length / ModelingGlbReader.VertexComponentCount;
            }
            finally
            {
                pinnedVertices.Free();
                _graphics.BindBuffer(GL_ARRAY_BUFFER, 0);
            }
        }

        internal static void ClearFrame(GlInterface graphics, int framebuffer, int width, int height)
        {
            graphics.BindFramebuffer(GL_FRAMEBUFFER, framebuffer);
            graphics.Viewport(0, 0, width, height);
            graphics.Disable(GL_SCISSOR_TEST);
            graphics.DepthMask(1);
            graphics.ClearDepth(1);
            graphics.ClearColor(0, 0, 0, 0);
            graphics.Clear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT);
        }

        internal void Draw(int width, int height, float yaw, float pitch, float zoom)
        {
            float verticalSlope = MathF.Tan(MathF.PI / 8);
            float horizontalSlope = verticalSlope * width / height;
            float fitAngle = MathF.Atan(MathF.Min(verticalSlope, horizontalSlope));
            float fitDistance = FitRadius / MathF.Sin(fitAngle);
            float distance = MathF.Max(FitRadius, fitDistance * zoom);
            float near = MathF.Max(0.01f, distance - FitRadius);
            float far = distance + FitRadius;

            _graphics.Disable(GL_CULL_FACE);
            _graphics.Disable(Blend);
            _graphics.Enable(GL_DEPTH_TEST);
            _graphics.DepthFunc(GL_LESS);
            if (_vertexArray != 0)
            {
                _graphics.BindVertexArray(_vertexArray);
            }
            _graphics.BindBuffer(GL_ARRAY_BUFFER, _vertexBuffer);
            _graphics.UseProgram(_program);
            try
            {
                int vertexStride = ModelingGlbReader.VertexComponentCount * sizeof(float);
                SetVertexAttribute(PositionAttribute, vertexStride, 0);
                SetVertexAttribute(NormalAttribute, vertexStride, 3 * sizeof(float));
                SetVertexAttribute(ColorAttribute, vertexStride, 6 * sizeof(float));
                _graphics.Uniform1f(_yawUniform, yaw);
                _graphics.Uniform1f(_pitchUniform, pitch);
                _graphics.Uniform1f(_distanceUniform, distance);
                _graphics.Uniform1f(_horizontalScaleUniform, 1 / horizontalSlope);
                _graphics.Uniform1f(_verticalScaleUniform, 1 / verticalSlope);
                _graphics.Uniform1f(_depthScaleUniform, (far + near) / (far - near));
                _graphics.Uniform1f(_depthOffsetUniform, -2 * far * near / (far - near));
                _graphics.DrawArrays(Triangles, 0, new IntPtr(_vertexCount));
                ThrowOnGraphicsError();
            }
            finally
            {
                _graphics.UseProgram(0);
                _graphics.BindBuffer(GL_ARRAY_BUFFER, 0);
                if (_vertexArray != 0)
                {
                    _graphics.BindVertexArray(0);
                }
                _graphics.Disable(GL_DEPTH_TEST);
            }
        }

        internal void Release()
        {
            if (_vertexArray != 0)
            {
                _graphics.DeleteVertexArray(_vertexArray);
                _vertexArray = 0;
            }
            if (_vertexBuffer != 0)
            {
                _graphics.DeleteBuffer(_vertexBuffer);
                _vertexBuffer = 0;
            }
            if (_program != 0)
            {
                _graphics.DeleteProgram(_program);
                _program = 0;
            }
            _vertexCount = 0;
        }

        private void SetVertexAttribute(int index, int stride, int offset)
        {
            _graphics.VertexAttribPointer(index, 3, GL_FLOAT, 0, stride, new IntPtr(offset));
            _graphics.EnableVertexAttribArray(index);
        }

        private int RequireUniform(string name)
        {
            int location = _graphics.GetUniformLocationString(_program, name);
            if (location < 0)
            {
                throw new InvalidOperationException("OpenGL did not link a GLB camera uniform.");
            }
            return location;
        }

        private void ThrowOnGraphicsError()
        {
            int error = _graphics.GetError();
            if (error != 0)
            {
                throw new InvalidOperationException($"OpenGL could not render the GLB model (0x{error:X}).");
            }
        }

        private int CreateProgram(GlVersion version)
        {
            (string header, bool modern) = GetShaderHeader(version);
            bool embedded = version.Type == GlProfileType.OpenGLES;
            string attribute = "attribute";
            string vertexOutput = "varying";
            string fragmentInput = "varying";
            string fragmentOutput = string.Empty;
            string colorOutput = "gl_FragColor";
            if (modern == true)
            {
                attribute = "in";
                vertexOutput = "out";
                fragmentInput = "in";
                fragmentOutput = "out vec4 fragmentColor;\n";
                colorOutput = "fragmentColor";
            }
            string vertexPrecision = string.Empty;
            string fragmentPrecision = string.Empty;
            string varyingPrecision = string.Empty;
            if (embedded == true)
            {
                vertexPrecision = "precision highp float;\n";
                fragmentPrecision = "precision mediump float;\n";
                varyingPrecision = "mediump ";
            }
            string vertexSource = header + vertexPrecision + $"""
                {attribute} vec3 position;
                {attribute} vec3 normal;
                {attribute} vec3 color;
                {vertexOutput} {varyingPrecision}vec3 viewNormal;
                {vertexOutput} {varyingPrecision}vec3 baseColor;
                """ + "\n" + """
                uniform float cameraYaw;
                uniform float cameraPitch;
                uniform float cameraDistance;
                uniform float horizontalScale;
                uniform float verticalScale;
                uniform float depthScale;
                uniform float depthOffset;
                void main()
                {
                    float yawCos = cos(cameraYaw);
                    float yawSin = sin(cameraYaw);
                    float pitchCos = cos(cameraPitch);
                    float pitchSin = sin(cameraPitch);
                    vec3 right = vec3(yawCos, 0.0, -yawSin);
                    vec3 up = vec3(-yawSin * pitchSin, pitchCos, -yawCos * pitchSin);
                    vec3 backward = vec3(yawSin * pitchCos, pitchSin, yawCos * pitchCos);
                    float depth = cameraDistance - dot(position, backward);
                    gl_Position = vec4(dot(position, right) * horizontalScale, dot(position, up) * verticalScale, depth * depthScale + depthOffset, depth);
                    viewNormal = vec3(dot(normal, right), dot(normal, up), dot(normal, backward));
                    baseColor = color;
                }
                """;
            string fragmentSource = header + fragmentPrecision + fragmentOutput + $"""
                {fragmentInput} {varyingPrecision}vec3 viewNormal;
                {fragmentInput} {varyingPrecision}vec3 baseColor;
                """ + "\n" + """
                void main()
                {
                    vec3 facingNormal = normalize(viewNormal + vec3(0.0, 0.0, 0.0001));
                    if (!gl_FrontFacing)
                    {
                        facingNormal = -facingNormal;
                    }
                    vec3 light = normalize(vec3(0.4, 0.65, 1.0));
                    float shade = 0.3 + 0.7 * max(dot(facingNormal, light), 0.0);
                    vec3 litColor = pow(clamp(baseColor * shade, 0.0, 1.0), vec3(1.0 / 2.2));
                """ + "\n    " + colorOutput + " = vec4(litColor, 1.0);\n}\n";
            int vertexShader = 0;
            int fragmentShader = 0;
            int program = 0;
            try
            {
                vertexShader = CompileShader(GL_VERTEX_SHADER, vertexSource);
                fragmentShader = CompileShader(GL_FRAGMENT_SHADER, fragmentSource);
                program = _graphics.CreateProgram();
                if (program == 0)
                {
                    throw new InvalidOperationException("OpenGL could not allocate the GLB shader program.");
                }
                _graphics.AttachShader(program, vertexShader);
                _graphics.AttachShader(program, fragmentShader);
                _graphics.BindAttribLocationString(program, PositionAttribute, "position");
                _graphics.BindAttribLocationString(program, NormalAttribute, "normal");
                _graphics.BindAttribLocationString(program, ColorAttribute, "color");
                string error = _graphics.LinkProgramAndGetError(program);
                if (error != null)
                {
                    throw new InvalidOperationException("OpenGL could not link the GLB shaders: " + error);
                }
                return program;
            }
            catch
            {
                if (program != 0)
                {
                    _graphics.DeleteProgram(program);
                }
                throw;
            }
            finally
            {
                if (vertexShader != 0)
                {
                    _graphics.DeleteShader(vertexShader);
                }
                if (fragmentShader != 0)
                {
                    _graphics.DeleteShader(fragmentShader);
                }
            }
        }

        private int CompileShader(int type, string source)
        {
            int shader = _graphics.CreateShader(type);
            if (shader == 0)
            {
                throw new InvalidOperationException("OpenGL could not allocate a GLB shader.");
            }
            try
            {
                string error = _graphics.CompileShaderAndGetError(shader, source);
                if (error != null)
                {
                    throw new InvalidOperationException("OpenGL could not compile a GLB shader: " + error);
                }
                return shader;
            }
            catch
            {
                _graphics.DeleteShader(shader);
                throw;
            }
        }

        private static (string Header, bool Modern) GetShaderHeader(GlVersion version)
        {
            if (version.Type == GlProfileType.OpenGLES)
            {
                if (version.Major >= 3)
                {
                    return ("#version 300 es\n", true);
                }
                return ("#version 100\n", false);
            }
            if (version.Major >= 4)
            {
                return ("#version 150\n", true);
            }
            if (version.Major == 3)
            {
                if (version.Minor >= 2)
                {
                    return ("#version 150\n", true);
                }
                return ("#version 130\n", true);
            }
            if (version.Minor >= 1)
            {
                return ("#version 120\n", false);
            }
            return ("#version 110\n", false);
        }
    }
}
