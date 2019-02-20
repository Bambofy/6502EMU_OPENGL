using System;
using System.IO;
using System.Numerics;
using System.Text;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.SPIRV;
using Veldrid.StartupUtilities;

namespace _6502EMU
{
    public enum Registers
    {
        ProgramCounter,
        ProcessorStatus
    }


    // http://www.6502.org/tutorials/6502opcodes.html
    // https://en.wikipedia.org/wiki/MOS_Technology_6502#Technical_description

    public class EMU_6502
    {
        // 16 bit address bus.
        // 8 bit processor.
        // 8 bit data bus.

        // Memory
        private sbyte[] _MEMORY = new sbyte[65536];

        private UInt32[] _COLOUR_PALETTE = new UInt32[16] {
            0x000000, 0xffffff, 0x880000, 0xaaffee,
            0xcc44cc, 0x00cc55, 0x0000aa, 0xeeee77,
            0xdd8855, 0x664400, 0xff7777, 0x333333,
            0x777777, 0xaaff66, 0x0088ff, 0xbbbbbb
        };
#region CPU Registers

        /// <summary>
        /// 8 Bit Accumulator.
        /// </summary>
        private sbyte _A = 0;

        /// <summary>
        /// 8 Bit Index Registers.
        /// </summary>
        private sbyte _X, _Y = 0;

        /// <summary>
        /// 6x8 bit (1 or 0 vavlues) Processor Status Flags.
        /// </summary>
        private sbyte[] _P = new sbyte[7]; // processor status flags.

        /// <summary>
        /// 8 Bit stack pointer
        /// </summary>
        private byte _sp = 0;

        /// <summary>
        /// 16 Bit program counter.
        /// </summary>
        private ushort _pc = 0;

#endregion

#region Memory Layout Descriptions (Pages)

        private ushort _pageSize = 0x00FF; // 255 address range for a page.

        /// <summary>
        /// Where in the main memory the beginning of the stack starts.
        /// </summary>
        private ushort _stackStartAddress = 0x0100; // 256 to 511.


        private ushort _gfxStartAddress = 0x0200; // start at 0x0200 to 0x05FF
        /// <summary>
        /// Where the program is loaded into the memory.
        /// </summary>
        private ushort _programStartAddress = 0x0600; // start at 1536 0x0600.

        /// <summary>
        /// NMI Vector, 4 bits wide.
        /// </summary>
        private ushort _nmiStartAddress = 0xFFFA;

        /// <summary>
        /// RESET vector. 4 bits wide.
        /// </summary>
        private ushort _resetStartAddress = 0xFFFC;

        /// <summary>
        /// IRQ/BRK vector. 4 bits wide.
        /// </summary>
        private ushort _irqStartAddress = 0xFFFE;



        private byte _carryFlagAddress = 0;

        private byte _zeroFlagAddress = 1;

        private byte _interruptDisableAddress = 2;

        private byte _decimalModeFlagAddress = 3;

        private byte _breakCommandFlagAddress = 4;

        private byte _overflowFlagAddress = 5;

        private byte _negativeFlagAddress = 6;
        #endregion

        #region Stack

        public void PopProgramCounter()
        {
            _pc = PopUShort();
        }

        public void PushProgramCounter()
        {
            PushUShort(_pc);
        }

        public void PopProcessorFlags()
        {
            for (int i = 0; i < 7; i++)
            {
                _P[i] = PopByte();
            }
        }
        public void PushProcessorFlags()
        {
            for (int i = 0; i < 7; i++)
            {
                PushByte(_P[i]);
            }
        }


        
        private void PushUShort(ushort pValue)
        {
            // Stack pointer initially sits point at a free address space.
            _MEMORY[_stackStartAddress + _sp] = (sbyte)((pValue & 0xFF00) >> 8); // Push our address to it.
            _MEMORY[_stackStartAddress + _sp + 1] = (sbyte)(pValue & 0x00FF);

            _sp += 2;
        }

        private void PushByte(sbyte pValue)
        {
            _MEMORY[_stackStartAddress + _sp] = pValue;

            _sp += 1;
        }

        private ushort PopUShort()
        {
            // Stack pointer initially sits pointing at a free address space.
            _sp -= 2; // So we reduce it to the last address space (which is occupied)

            sbyte highByte = (_MEMORY[_stackStartAddress + _sp]);
            sbyte lowByte = (_MEMORY[_stackStartAddress + _sp + 1]);

            return (ushort)((ushort)(highByte << 8) | lowByte);
        }

        private sbyte PopByte()
        {
            _sp--;

            return _MEMORY[_stackStartAddress + _sp];
        }
        #endregion

        #region Pin Functions
        /// <summary>
        /// Reset request.
        /// </summary>
        public void RESET()
        {

        }

        /// <summary>
        /// Interrupt request.
        /// </summary>
        public void IRQ()
        {

        }

        /// <summary>
        /// Non-Maskable interrupt.
        /// </summary>
        public void NMI()
        {

        }
        #endregion

        public void PrintMemory()
        {
            Console.WriteLine("---------------");
            Console.WriteLine("PC={0}", _pc);
            Console.WriteLine("A={0}", _A);
            Console.WriteLine("X={0}", _X);
            Console.WriteLine("Y={0}", _Y);
            Console.WriteLine("SP={0}", _sp);
            Console.Write("NV-BDIZC=");
            for (int i = 0; i < 6; i++)
            {
                Console.Write(_P[i] + ",");
            }
            Console.Write("\n");
            Console.WriteLine("---------------");
        }

        public UInt32 GetPixel(int pX, int pY)
        {
            ushort memoryAddress = (ushort)(_gfxStartAddress + (pX + (pY * 32)));
            sbyte memoryValue = _MEMORY[memoryAddress];
            UInt32 colour = _COLOUR_PALETTE[memoryValue];

            return colour;
        }

        public ushort ReadAbsoluteAddress(sbyte pFirstByte, sbyte pSecondByte)
        {
            byte firstByte = (byte)(pSecondByte);
            byte secondByte = (byte)(pFirstByte);

            return (ushort)((ushort)(firstByte << 8) | secondByte);
        }

        public EMU_6502(string pGameFileName)
        {
            _sp = 0;
            _pc = 0;

            Console.WriteLine("Loading Game: {0}", pGameFileName);

            long totalFileSizeInBytes;
            using (FileStream fs = File.Open(pGameFileName, FileMode.Open))
            {
                totalFileSizeInBytes = fs.Length;
            }

            int byteCount = 0;
            using (BinaryReader br = new BinaryReader(new FileStream(pGameFileName, FileMode.Open)))
            {
                while (byteCount < totalFileSizeInBytes)
                {
                    _MEMORY[_programStartAddress + byteCount] = br.ReadSByte();
                    byteCount++;
                }
            }
            Console.WriteLine("Total size: {0}", byteCount);
        }

        public void EmulateCycle()
        {
            if (_P[_breakCommandFlagAddress] != 0)
            {
                return;
            }

            // the opcode is 16 bits wide.
            // some opcodes only use 8 bits though. (they only access the first 256 shorts).

            // http://www.6502.org/tutorials/6502opcodes.html#PC
            sbyte opcode = _MEMORY[_programStartAddress + _pc];
            byte opcodeHN = (byte)(((byte)(opcode >> 4)) & 0x0F);
            byte opcodeLN = ((byte)(_MEMORY[_programStartAddress + _pc] & 0x0F));

            Console.WriteLine("{0:X}, {1:X}", opcodeHN, opcodeLN);

            switch (opcodeHN)
            {
                case 0x0:
                    switch (opcodeLN)
                    {
                        case 0x0:
                            // 0x00
                            // BRK
                            // http://www.obelisk.me.uk/6502/reference.html#BRK

                            PushProgramCounter();
                            PushProcessorFlags();

                            // IRQ vector address is loaded into PC.
                            sbyte hb = _MEMORY[_irqStartAddress];
                            sbyte lb = _MEMORY[_irqStartAddress + 1];
                            _pc = (ushort)((ushort)(hb << 8) | (ushort)(lb));

                            // Break flag set to 1
                            _P[_breakCommandFlagAddress] = 1;

                            break;
                    }
                    break;
                case 0x1:
                    break;
                case 0x2:
                    break;
                case 0x3:
                    break;
                case 0x4:
                    break;
                case 0x5:
                    break;
                case 0x6:
                    break;
                case 0x7:
                    break;
                case 0x8:
                    switch (opcodeLN)
                    {
                        case 0xE:
                            // 0x8E - Absolute - STX - Store X Register

                            _pc++; // move to the operand location.

                            // read ushort address value.
                            // 00 02 -> $0200
                            ushort addr = ReadAbsoluteAddress(_MEMORY[_programStartAddress + _pc], _MEMORY[_programStartAddress + _pc + 1]); // 02 00

                            _MEMORY[addr] = _X;

                            _pc += 2; // next instruction location.
                            break;
                        case 0xD:
                            // Ox8D - Absolute - STA - Store Accumulator.

                            _pc++;

                            // read ushort address value.
                            // 00 02 -> $0200
                            addr = ReadAbsoluteAddress(_MEMORY[_programStartAddress + _pc], _MEMORY[_programStartAddress + _pc + 1]); // 02 00

                            _MEMORY[addr] = _A;

                            Console.WriteLine("_MEMORY[{0:X}] = _A({1:X})", addr, _A);

                            _pc +=2;

                            break;

                    }
                    break;
                case 0x9:
                    break;
                case 0xA:
                    switch (opcodeLN)
                    {
                        case 0x2:
                            // 0xA2 - LDX - Immediate addressing - Load X Register.
                            _pc++; // move to operand.
                            
                            _X = _MEMORY[_programStartAddress + _pc]; // load value at address defined.
                            
                            if (_X == 0) _P[_zeroFlagAddress] = 1;
                            if (_X < 0) _P[_negativeFlagAddress] = 1;

                            _pc++;

                            break;
                        case 0x9:
                            // 0xA9 - LDA - Immediate Mode - Load Accumulator.
                            _pc++;

                            _A = _MEMORY[_programStartAddress + _pc];

                            _pc++;

                            break;
                    }
                    break;
                case 0xB:
                    break;
                case 0xC:
                    switch (opcodeLN)
                    {
                        case 0xA:
                            // 0xCA - DEX - Decrement X Register.
                            _X--;

                            if (_X == 0) _P[_zeroFlagAddress] = 1;
                            if (_X < 0) _P[_negativeFlagAddress] = 1;

                            _pc++;

                            break;
                    }
                    break;
                case 0xD:
                    switch (opcodeLN)
                    {
                        case 0x0:
                            // 0xD0 - BNE - Branch if not equal - relative addressing
                            if (_P[_zeroFlagAddress] == 0)
                            {
                                // if the zero flag is clear, add relative displacement to the pc
                                _pc++;

                                sbyte disp = _MEMORY[_programStartAddress + _pc];

                                _pc++;

                                _pc += (ushort)disp;
                            }
                            break;
                    }
                    break;
                case 0xE:
                    switch (opcodeLN)
                    {
                        case 0x00:
                            // 0xE0 - CPX - Compare X Register with given value
                            _pc++;

                            sbyte v = _MEMORY[_programStartAddress + _pc]; // load the operand value from memory.
                            

                            if (_X >= v) _P[_carryFlagAddress] = 1;
                            if (_X == v) _P[_zeroFlagAddress] = 1;
                            if (_X < v) _P[_negativeFlagAddress] = 1;

                            _pc++;

                            break;
                    }
                    break;
                case 0xF:
                    break;
                default:
                    break;
            }
        }
    }

    struct VertexPositionColor
    {
        public Vector2 Position; // This is the position, in normalized device coordinates.
        public RgbaFloat Color; // This is the color of the vertex.
        public VertexPositionColor(Vector2 position, RgbaFloat color)
        {
            Position = position;
            Color = color;
        }
        public const uint SizeInBytes = 24;
    }

    class Program
    {
        private static GraphicsDevice _graphicsDevice;
        private static CommandList _commandList;
        private static DeviceBuffer _vertexBuffer;
        private static DeviceBuffer _indexBuffer;
        private static Shader[] _shaders;
        private static Pipeline _pipeline;

        private static DeviceBuffer _positionOffsetBuffer;
        private static DeviceBuffer _colourValueBuffer;
        private static ResourceSet _positionOffsetResourceSet;
        private static ResourceSet _colourValueResourceSet;

        private const string VertexCode = @"
#version 450

layout(location = 0) in vec2 Position;
layout(location = 1) in vec4 Color;
layout(set = 0, binding = 0) uniform PositionOffset
{
    vec2 OffsetVal;
};

layout(set = 0, binding = 1) uniform ColorValue
{
    vec4 ColorVal;
};

layout(location = 0) out vec4 fsin_Color;

void main()
{
    gl_Position = vec4(OffsetVal + Position, 0, 1);
    fsin_Color = ColorVal;
}";

        private const string FragmentCode = @"
#version 450

layout(location = 0) in vec4 fsin_Color;
layout(location = 0) out vec4 fsout_Color;

void main()
{
    fsout_Color = fsin_Color;
}";

        static void CreateResources()
        {
            ResourceFactory factory = _graphicsDevice.ResourceFactory;

            float scale = 16f;
            VertexPositionColor[] quadVertices =
            {
                new VertexPositionColor(new Vector2(0f, 0f), RgbaFloat.Red), // tl
                new VertexPositionColor(new Vector2(1f/scale, 0f), RgbaFloat.Green), // tr
                new VertexPositionColor(new Vector2(0f, -1f/scale), RgbaFloat.Blue), // bl
                new VertexPositionColor(new Vector2(1f/scale, -1f/scale), RgbaFloat.Yellow) // br
            };

            ushort[] quadIndices = { 0, 1, 2, 3 };

            _positionOffsetBuffer = factory.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer));
            _colourValueBuffer = factory.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer));

            _vertexBuffer = factory.CreateBuffer(new BufferDescription(4 * VertexPositionColor.SizeInBytes, BufferUsage.VertexBuffer));
            _indexBuffer = factory.CreateBuffer(new BufferDescription(4 * sizeof(ushort), BufferUsage.IndexBuffer));

            _graphicsDevice.UpdateBuffer(_vertexBuffer, 0, quadVertices);
            _graphicsDevice.UpdateBuffer(_indexBuffer, 0, quadIndices);

            VertexLayoutDescription vertexLayout = new VertexLayoutDescription(
                new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
                new VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4));

            ResourceLayout positionOffset = factory.CreateResourceLayout(
               new ResourceLayoutDescription(
                   new ResourceLayoutElementDescription("OffsetVal", ResourceKind.UniformBuffer, ShaderStages.Vertex)
               )
            );

            ResourceLayout colourValue = factory.CreateResourceLayout(
              new ResourceLayoutDescription(
                  new ResourceLayoutElementDescription("ColorVal", ResourceKind.UniformBuffer, ShaderStages.Vertex)
              )
           );

            ShaderDescription vertexShaderDesc = new ShaderDescription(
                ShaderStages.Vertex,
                Encoding.UTF8.GetBytes(VertexCode),
                "main");
            ShaderDescription fragmentShaderDesc = new ShaderDescription(
                ShaderStages.Fragment,
                Encoding.UTF8.GetBytes(FragmentCode),
                "main");

            _shaders = factory.CreateFromSpirv(vertexShaderDesc, fragmentShaderDesc);

            GraphicsPipelineDescription pipelineDescription = new GraphicsPipelineDescription();
            pipelineDescription.BlendState = BlendStateDescription.SingleOverrideBlend;

            pipelineDescription.DepthStencilState = new DepthStencilStateDescription(
                depthTestEnabled: true,
                depthWriteEnabled: true,
                comparisonKind: ComparisonKind.LessEqual);

            pipelineDescription.RasterizerState = new RasterizerStateDescription(
                cullMode: FaceCullMode.Back,
                fillMode: PolygonFillMode.Solid,
                frontFace: FrontFace.Clockwise,
                depthClipEnabled: true,
                scissorTestEnabled: false);

            pipelineDescription.PrimitiveTopology = PrimitiveTopology.TriangleStrip;
            pipelineDescription.ResourceLayouts = new ResourceLayout[] { positionOffset, colourValue };

            pipelineDescription.ShaderSet = new ShaderSetDescription(
                vertexLayouts: new VertexLayoutDescription[] { vertexLayout },
                shaders: _shaders);

            pipelineDescription.Outputs = _graphicsDevice.SwapchainFramebuffer.OutputDescription;
            _pipeline = factory.CreateGraphicsPipeline(pipelineDescription);

            _positionOffsetResourceSet = factory.CreateResourceSet(new ResourceSetDescription(positionOffset, _positionOffsetBuffer));
            _colourValueResourceSet = factory.CreateResourceSet(new ResourceSetDescription(colourValue, _colourValueBuffer));

            _commandList = factory.CreateCommandList();
        }
        
        static void Draw()
        {
            _commandList.Begin();

            _commandList.SetFramebuffer(_graphicsDevice.SwapchainFramebuffer);

            _commandList.ClearColorTarget(0, RgbaFloat.Black);

            _commandList.SetVertexBuffer(0, _vertexBuffer);
            _commandList.SetIndexBuffer(_indexBuffer, IndexFormat.UInt16);
            _commandList.SetPipeline(_pipeline);

            _commandList.SetGraphicsResourceSet(0, _positionOffsetResourceSet);
            _commandList.SetGraphicsResourceSet(1, _colourValueResourceSet);

            for (int x = 0; x < 32; x++)
            {
                for (int y = 0; y < 32; y++)
                {
                    // (0, 0) = (-1, 1)
                    UInt32 thisPixelColor = _emu.GetPixel(x, y);

                    // 0xFF00FF
                    byte r = ((byte)(thisPixelColor >> 16));
                    byte g = ((byte)((byte)(thisPixelColor >> 8) & 0x0000FF));
                    byte b = ((byte)((byte)(thisPixelColor) & 0x0000FF));

                    _commandList.UpdateBuffer(_positionOffsetBuffer, 0, new Vector2(((x-16)/16f), (-y+16)/16f));
                    _commandList.UpdateBuffer(_colourValueBuffer, 0, new RgbaFloat((float)r/255.0f, (float)g/255.0f, (float)b/255.0f, 1.0f));

                    _commandList.DrawIndexed(
                        indexCount: 4,
                        instanceCount: 1,
                        indexStart: 0,
                        vertexOffset: 0,
                        instanceStart: 0
                    );
                }
            }


            _commandList.End();


            _graphicsDevice.SubmitCommands(_commandList);
            _graphicsDevice.SwapBuffers();
        }

        static void DisposeResources()
        {
            _pipeline.Dispose();
            for (int i = 0; i < _shaders.Length; i++) _shaders[i].Dispose();
            _commandList.Dispose();
            _vertexBuffer.Dispose();
            _indexBuffer.Dispose();
            _graphicsDevice.Dispose();
        }

        private static EMU_6502 _emu;

        static void Main(string[] args)
        {
            _emu = new EMU_6502("p2.6502asm");

            // start pixel address is 0x0200
            // rightmost pixel address is 0x021F
            // 0 = 0x0200
            // 31 = 0x021F

            // 32 by 32 pixels.

            // 0x0200 = 512
            // 0x05ff = 1535

            WindowCreateInfo windowCI = new WindowCreateInfo()
            {
                X = 100,
                Y = 100,
                WindowWidth = 320,
                WindowHeight = 320
            };
            Sdl2Window window = VeldridStartup.CreateWindow(ref windowCI);
            _graphicsDevice = VeldridStartup.CreateGraphicsDevice(window);
            
            CreateResources();
            while (window.Exists)
            {
                window.PumpEvents();

                _emu.EmulateCycle();
                Draw();
                _emu.PrintMemory();
            }
            DisposeResources();
        }
    }
}
