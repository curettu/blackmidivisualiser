using System.Runtime.InteropServices;
using BlackMidiVisualizer.Midi;
using SharpGen.Runtime;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using static Vortice.Direct3D11.D3D11;
using static Vortice.DXGI.DXGI;
using Color4 = Vortice.Mathematics.Color4;

namespace BlackMidiVisualizer.Render;

public sealed class D3DRenderer : IDisposable
{
    public const int MaxGpuNotes = 500_000;
    public const int MaxQuadVerts = 48_000;

    private readonly nint _hwnd;
    private IDXGIFactory2 _factory = null!;
    private ID3D11Device _device = null!;
    private ID3D11DeviceContext _ctx = null!;
    private IDXGISwapChain1 _swap = null!;
    private ID3D11Texture2D _backBuf = null!;
    private ID3D11RenderTargetView _rtv = null!;
    private ID3D11VertexShader _noteVS = null!;
    private ID3D11PixelShader _notePS = null!;
    private ID3D11VertexShader _quadVS = null!;
    private ID3D11PixelShader _quadPS = null!;
    private ID3D11PixelShader _solidPS = null!;
    private ID3D11InputLayout _quadLayout = null!;
    private ID3D11Buffer _noteBuffer = null!;
    private ID3D11ShaderResourceView _noteSrv = null!;
    private ID3D11Buffer _frameCbuf = null!;
    private ID3D11Buffer _keyCbuf = null!;
    private ID3D11Buffer _quadVb = null!;
    private ID3D11Texture2D _hudTex = null!;
    private ID3D11ShaderResourceView _hudSrv = null!;
    private ID3D11SamplerState _sampler = null!;
    private ID3D11BlendState _blend = null!;
    private ID3D11RasterizerState _raster = null!;
    private ID3D11DepthStencilState _noDepth = null!;
    private readonly GpuNote[] _noteScratch = new GpuNote[MaxGpuNotes];
    private readonly QuadVertex[] _quadScratch = new QuadVertex[MaxQuadVerts];
    private readonly float[] _keyFloats = new float[128 * 4];
    private ID3D11Texture2D _chromeTex = null!;
    private ID3D11ShaderResourceView _chromeSrv = null!;
    private readonly HudSurface _hud = new();
    private readonly ChromeSurface _chrome = new();
    private readonly VisibleNoteCollector _collector = new();
    private int _width, _height;
    private bool _tearing;
    private int _quadCount;
    private KeyGeom[] _keys = KeyboardLayout.Build(21, 108);
    private int _firstKey = 21, _lastKey = 108;
    private readonly int[] _pressed = new int[128];

    public int VisibleNotes { get; private set; }
    public IReadOnlyList<HitRect> Hits { get; private set; } = Array.Empty<HitRect>();
    public string HoverId { get; set; } = "";
    public ChromeLayout Chrome => _chrome.Layout;

    public D3DRenderer(nint hwnd, int width, int height)
    {
        _hwnd = hwnd;
        _width = Math.Max(width, 16);
        _height = Math.Max(height, 16);
        CreateDevice();
        CreateSizeResources();
        CreatePipeline();
    }

    public void Resize(int width, int height)
    {
        if (width < 16 || height < 16) return;
        if (width == _width && height == _height) return;
        _width = width;
        _height = height;
        _ctx.OMSetRenderTargets((ID3D11RenderTargetView)null!, null);
        _rtv.Dispose();
        _backBuf.Dispose();
        _swap.ResizeBuffers(2, (uint)width, (uint)height, Format.B8G8R8A8_UNorm, _tearing ? SwapChainFlags.AllowTearing : SwapChainFlags.None);
        CreateSizeResources();
    }

    private int _kbMode = -1;

    public void SetKeyboard(int mode)
    {
        if (mode == _kbMode) return;
        _kbMode = mode;
        (_firstKey, _lastKey) = KeyboardLayout.Range(mode);
        _keys = KeyboardLayout.Build(_firstKey, _lastKey);
        for (int i = 0; i < 128; i++)
        {
            _keyFloats[i * 4 + 0] = _keys[i].X;
            _keyFloats[i * 4 + 1] = _keys[i].Width;
            _keyFloats[i * 4 + 2] = _keys[i].IsBlack ? 1f : 0f;
            _keyFloats[i * 4 + 3] = 0;
        }
        UpdateBuffer(_keyCbuf, _keyFloats);
    }

    public void ResetCollector() => _collector.Reset();

    public void Render(MidiSong? song, AppSettings settings, double time, HudInfo hud, bool vsync)
    {
        SetKeyboard(settings.KeyboardMode);
        float chromePx = ChromeLayout.TotalHeight;
        float viewTopNdc = 1f - chromePx / Math.Max(_height, 1) * 2f;
        float pianoTopNdc = -1f + 0.40f;
        int cap = Math.Min(settings.MaxVisibleNotes, MaxGpuNotes);
        int count = 0;
        if (song is not null)
            count = _collector.Collect(song, (float)time, settings.ViewSeconds, _firstKey, _lastKey, cap, _noteScratch, _pressed);
        VisibleNotes = count;
        UploadNotes(count);

        var frame = new FrameConstants
        {
            Time = (float)time,
            TimeSpan = settings.ViewSeconds,
            PianoTopNdc = pianoTopNdc,
            ViewTopNdc = viewTopNdc,
            NoteCount = (uint)count,
            ScreenW = _width,
            ScreenH = _height,
            NoteGapPx = 1.35f
        };
        UpdateBuffer(_frameCbuf, frame);

        _ctx.OMSetRenderTargets(_rtv);
        _ctx.RSSetViewport(new Viewport(0, 0, _width, _height));
        _ctx.RSSetState(_raster);
        _ctx.OMSetBlendState(_blend, new Color4(0, 0, 0, 0), 0xFFFFFFFF);
        _ctx.OMSetDepthStencilState(_noDepth);
        _ctx.ClearRenderTargetView(_rtv, new Color4(0, 0, 0, 1));

        if (count > 0)
        {
            _ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            _ctx.VSSetShader(_noteVS);
            _ctx.PSSetShader(_notePS);
            _ctx.VSSetConstantBuffer(0, _frameCbuf);
            _ctx.VSSetConstantBuffer(1, _keyCbuf);
            _ctx.PSSetConstantBuffer(0, _frameCbuf);
            _ctx.VSSetShaderResource(0, _noteSrv);
            _ctx.Draw((uint)count * 6, 0);
        }

        _quadCount = 0;
        BuildPiano(pianoTopNdc);

        _chrome.Draw(_width, settings, hud.Playing, hud.Time, hud.Duration, HoverId);
        Hits = _chrome.Layout.Hits().ToArray();
        UploadChrome();
        AddChromeQuad();

        bool hudOn = settings.ShowHud;
        if (hudOn)
        {
            _hud.Draw(hud.FileName, hud.Time, hud.Duration, hud.NoteCount, VisibleNotes, hud.Nps, hud.Fps, hud.Polyphony, hud.Passed, hud.Playing);
            UploadHud();
            AddHudQuad();
        }
        DrawQuads(hudOn);

        _swap.Present(vsync || settings.VSync ? 1u : 0u, (!vsync && !settings.VSync && _tearing) ? PresentFlags.AllowTearing : PresentFlags.None);
    }

    private void BuildPiano(float pianoTopNdc)
    {
        float bottom = -1f;
        float redH = 9f / _height * 2f;
        float keyTop = pianoTopNdc - redH;
        float lip = 0.062f;

        AddSolid(-1, bottom, 1, pianoTopNdc + 0.01f, 0.02f, 0.02f, 0.025f, 1);

        for (int k = _firstKey; k <= _lastKey; k++)
        {
            if (_keys[k].IsBlack) continue;
            float x0 = _keys[k].X * 2f - 1f;
            float x1 = (_keys[k].X + _keys[k].Width) * 2f - 1f;
            bool pressed = _pressed[k] >= 0;
            float r = 0.96f, g = 0.96f, b = 0.97f;
            if (pressed)
            {
                ColorPack.UnpackRgb(_pressed[k], out r, out g, out b);
                r = lerp(r, 1, 0.22f); g = lerp(g, 1, 0.22f); b = lerp(b, 1, 0.22f);
            }

            float gap = 0.0022f;
            // key bed / gap
            AddSolid(x0, bottom, x1, keyTop, 0.08f, 0.08f, 0.09f, 1);
            // top face
            AddSolid(x0 + gap, bottom + lip, x1 - gap, keyTop, r, g, b, 1);
            // left highlight
            AddSolid(x0 + gap, bottom + lip, x0 + gap + 0.0035f, keyTop, r * 1.05f, g * 1.05f, b * 1.05f, 1);
            // right shade
            AddSolid(x1 - gap - 0.0045f, bottom + lip, x1 - gap, keyTop, r * 0.72f, g * 0.72f, b * 0.72f, 1);
            // front lip (Synthesia)
            float fr = pressed ? r * 0.7f : 0.78f, fg = pressed ? g * 0.7f : 0.78f, fb = pressed ? b * 0.7f : 0.79f;
            AddSolid(x0 + gap, bottom, x1 - gap, bottom + lip, fr, fg, fb, 1);
            AddSolid(x0 + gap, bottom + lip - 0.008f, x1 - gap, bottom + lip, fr * 1.12f, fg * 1.12f, fb * 1.12f, 1);
        }

        // Yamaha / Synthesia red felt
        AddSolid(-1, keyTop, 1, pianoTopNdc, 0.78f, 0.08f, 0.12f, 1);
        AddSolid(-1, pianoTopNdc - redH * 0.38f, 1, pianoTopNdc, 0.94f, 0.20f, 0.22f, 1);
        AddSolid(-1, keyTop, 1, keyTop + redH * 0.28f, 0.42f, 0.04f, 0.06f, 1);

        for (int k = _firstKey; k <= _lastKey; k++)
        {
            if (!_keys[k].IsBlack) continue;
            float x0 = _keys[k].X * 2f - 1f;
            float x1 = (_keys[k].X + _keys[k].Width) * 2f - 1f;
            bool pressed = _pressed[k] >= 0;
            float r = 0.06f, g = 0.06f, b = 0.07f;
            if (pressed)
            {
                ColorPack.UnpackRgb(_pressed[k], out r, out g, out b);
                r *= 0.42f; g *= 0.42f; b *= 0.42f;
            }
            float y1 = lerp(bottom, keyTop, 0.64f);
            float y0 = bottom + lip * 0.55f;
            AddSolid(x0, y0, x1, y1, r, g, b, 1);
            AddSolid(x0, y0, x1, y0 + 0.028f, r * 0.55f, g * 0.55f, b * 0.55f, 1);
            AddSolid(x0, y1 - 0.01f, x1, y1, r * 1.55f, g * 1.55f, b * 1.55f, 1);
            AddSolid(x0, y0, x0 + 0.003f, y1, r * 1.35f, g * 1.35f, b * 1.35f, 1);
            AddSolid(x1 - 0.0035f, y0, x1, y1, r * 0.45f, g * 0.45f, b * 0.45f, 1);
        }
    }

    private void AddChromeQuad()
    {
        float x1 = PixelToNdcX(Math.Min(_width, ChromeSurface.TexWidth));
        float y0 = PixelToNdcY(ChromeLayout.TotalHeight);
        float y1 = PixelToNdcY(0);
        float u1 = Math.Min(_width, ChromeSurface.TexWidth) / (float)ChromeSurface.TexWidth;
        AddQuad(-1, y0, x1, y1, 0, 1, u1, 0, 1, 1, 1, 1);
    }

    private void AddHudQuad()
    {
        float x0 = PixelToNdcX(8);
        float x1 = PixelToNdcX(8 + HudSurface.Width);
        float y0 = PixelToNdcY(ChromeLayout.TotalHeight + 8 + HudSurface.Height);
        float y1 = PixelToNdcY(ChromeLayout.TotalHeight + 8);
        AddTextured(x0, y0, x1, y1, 1, 1, 1, 1);
    }

    private void DrawQuads(bool hudOn)
    {
        if (_quadCount == 0) return;
        UpdateSpan<QuadVertex>(_quadVb, _quadScratch.AsSpan(0, _quadCount));
        _ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _ctx.IASetInputLayout(_quadLayout);
        _ctx.IASetVertexBuffer(0, _quadVb, (uint)Marshal.SizeOf<QuadVertex>());
        _ctx.VSSetShader(_quadVS);
        _ctx.PSSetSampler(0, _sampler);

        int overlayVerts = hudOn ? 12 : 6;
        int solid = Math.Max(0, _quadCount - overlayVerts);
        if (solid > 0)
        {
            _ctx.PSSetShader(_solidPS);
            _ctx.Draw((uint)solid, 0);
        }

        _ctx.PSSetShader(_quadPS);
        _ctx.PSSetShaderResource(0, _chromeSrv);
        _ctx.Draw(6, (uint)solid);
        if (hudOn)
        {
            _ctx.PSSetShaderResource(0, _hudSrv);
            _ctx.Draw(6, (uint)(solid + 6));
        }
        _ctx.IASetInputLayout(null!);
    }

    private void AddTextured(float x0, float y0, float x1, float y1, float r, float g, float b, float a)
    {
        AddQuad(x0, y0, x1, y1, 0, 1, 1, 0, r, g, b, a);
    }

    private void AddSolid(float x0, float y0, float x1, float y1, float r, float g, float b, float a)
    {
        AddQuad(x0, y0, x1, y1, 0, 0, 0, 0, r, g, b, a);
    }

    private void AddTriangle((float x, float y) p0, (float x, float y) p1, (float x, float y) p2, float r, float g, float b, float al)
    {
        if (_quadCount + 3 > MaxQuadVerts) return;
        PutVert(_quadCount++, p0.x, p0.y, 0, 0, r, g, b, al);
        PutVert(_quadCount++, p1.x, p1.y, 0, 0, r, g, b, al);
        PutVert(_quadCount++, p2.x, p2.y, 0, 0, r, g, b, al);
    }

    private void AddQuad(float x0, float y0, float x1, float y1, float u0, float v0, float u1, float v1, float r, float g, float b, float a)
    {
        if (_quadCount + 6 > MaxQuadVerts) return;
        PutVert(_quadCount++, x0, y0, u0, v0, r, g, b, a);
        PutVert(_quadCount++, x1, y0, u1, v0, r, g, b, a);
        PutVert(_quadCount++, x0, y1, u0, v1, r, g, b, a);
        PutVert(_quadCount++, x0, y1, u0, v1, r, g, b, a);
        PutVert(_quadCount++, x1, y0, u1, v0, r, g, b, a);
        PutVert(_quadCount++, x1, y1, u1, v1, r, g, b, a);
    }

    private void PutVert(int i, float x, float y, float u, float v, float r, float g, float b, float a)
    {
        _quadScratch[i] = new QuadVertex { X = x, Y = y, U = u, V = v, R = r, G = g, B = b, A = a };
    }

    private float PixelToNdcX(float x) => x / _width * 2f - 1f;
    private float PixelToNdcY(float y) => 1f - y / _height * 2f;
    private (float x, float y) Pixel(float x, float y) => (PixelToNdcX(x), PixelToNdcY(y));
    private static float lerp(float a, float b, float t) => a + (b - a) * t;

    private unsafe void UploadNotes(int count)
    {
        MappedSubresource map = _ctx.Map(_noteBuffer, 0, MapMode.WriteDiscard);
        if (count > 0)
        {
            fixed (GpuNote* src = _noteScratch)
                Buffer.MemoryCopy(src, (void*)map.DataPointer, (long)count * sizeof(GpuNote), (long)count * sizeof(GpuNote));
        }
        _ctx.Unmap(_noteBuffer, 0);
    }

    private unsafe void UploadChrome()
    {
        var box = new Box(0, 0, 0, ChromeSurface.TexWidth, ChromeSurface.TexHeight, 1);
        fixed (byte* p = _chrome.Pixels)
        {
            _ctx.UpdateSubresource(_chromeTex, 0, box, (nint)p, (uint)_chrome.Stride, 0);
        }
    }

    private unsafe void UploadHud()
    {
        var box = new Box(0, 0, 0, HudSurface.Width, HudSurface.Height, 1);
        fixed (byte* p = _hud.Pixels)
        {
            _ctx.UpdateSubresource(_hudTex, 0, box, (nint)p, (uint)_hud.Stride, 0);
        }
    }

    private unsafe void UpdateBuffer<T>(ID3D11Buffer buffer, T value) where T : unmanaged
    {
        MappedSubresource map = _ctx.Map(buffer, 0, MapMode.WriteDiscard);
        UnsafeWrite(map.DataPointer, value);
        _ctx.Unmap(buffer, 0);
    }

    private unsafe void UpdateBuffer<T>(ID3D11Buffer buffer, T[] data) where T : unmanaged
        => UpdateSpan<T>(buffer, data);

    private unsafe void UpdateSpan<T>(ID3D11Buffer buffer, ReadOnlySpan<T> data) where T : unmanaged
    {
        MappedSubresource map = _ctx.Map(buffer, 0, MapMode.WriteDiscard);
        if (data.Length > 0)
        {
            fixed (T* src = data)
                Buffer.MemoryCopy(src, (void*)map.DataPointer, (long)data.Length * sizeof(T), (long)data.Length * sizeof(T));
        }
        _ctx.Unmap(buffer, 0);
    }

    private static unsafe void UnsafeWrite<T>(nint dest, T value) where T : unmanaged
        => *(T*)dest = value;

    private void CreateDevice()
    {
        _factory = CreateDXGIFactory1<IDXGIFactory2>();
        using var f5 = _factory.QueryInterfaceOrNull<IDXGIFactory5>();
        if (f5 is not null) _tearing = f5.PresentAllowTearing;

        var levels = new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 };
        var flags = DeviceCreationFlags.BgraSupport;
        Result hr = D3D11CreateDevice(null, DriverType.Hardware, flags, levels, out _device, out _, out _ctx);
        if (hr.Failure)
            D3D11CreateDevice(null, DriverType.Warp, flags, levels, out _device, out _, out _ctx).CheckError();

        var desc = new SwapChainDescription1
        {
            Width = (uint)_width,
            Height = (uint)_height,
            Format = Format.B8G8R8A8_UNorm,
            Stereo = false,
            SampleDescription = new SampleDescription(1, 0),
            BufferUsage = Usage.RenderTargetOutput,
            BufferCount = 2,
            Scaling = Scaling.Stretch,
            SwapEffect = SwapEffect.FlipDiscard,
            AlphaMode = AlphaMode.Ignore,
            Flags = _tearing ? SwapChainFlags.AllowTearing : SwapChainFlags.None
        };
        _swap = _factory.CreateSwapChainForHwnd(_device, _hwnd, desc);
        _factory.MakeWindowAssociation(_hwnd, WindowAssociationFlags.IgnoreAltEnter);
    }

    private void CreateSizeResources()
    {
        _backBuf = _swap.GetBuffer<ID3D11Texture2D>(0);
        _rtv = _device.CreateRenderTargetView(_backBuf);
    }

    private void CreatePipeline()
    {
        ReadOnlyMemory<byte> noteVs = CompileHlsl("Notes.hlsl", "VSMain", "vs_5_0");
        ReadOnlyMemory<byte> notePs = CompileHlsl("Notes.hlsl", "PSMain", "ps_5_0");
        ReadOnlyMemory<byte> quadVs = CompileHlsl("Quads.hlsl", "VSMain", "vs_5_0");
        ReadOnlyMemory<byte> quadPs = CompileHlsl("Quads.hlsl", "PSMain", "ps_5_0");
        ReadOnlyMemory<byte> solidPs = CompileHlsl("Quads.hlsl", "PSSolid", "ps_5_0");

        _noteVS = _device.CreateVertexShader(noteVs.Span);
        _notePS = _device.CreatePixelShader(notePs.Span);
        _quadVS = _device.CreateVertexShader(quadVs.Span);
        _quadPS = _device.CreatePixelShader(quadPs.Span);
        _solidPS = _device.CreatePixelShader(solidPs.Span);

        var layout = new InputElementDescription[]
        {
            new("POSITION", 0, Format.R32G32_Float, 0, 0),
            new("TEXCOORD", 0, Format.R32G32_Float, 8, 0),
            new("COLOR", 0, Format.R32G32B32A32_Float, 16, 0),
        };
        _quadLayout = _device.CreateInputLayout(layout, quadVs.Span);

        uint noteStride = (uint)Marshal.SizeOf<GpuNote>();
        _noteBuffer = _device.CreateBuffer(new BufferDescription
        {
            ByteWidth = noteStride * MaxGpuNotes,
            Usage = ResourceUsage.Dynamic,
            BindFlags = BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.Write,
            MiscFlags = ResourceOptionFlags.BufferStructured,
            StructureByteStride = noteStride
        });
        _noteSrv = _device.CreateShaderResourceView(_noteBuffer, new ShaderResourceViewDescription
        {
            Format = Format.Unknown,
            ViewDimension = ShaderResourceViewDimension.Buffer,
            Buffer = { FirstElement = 0, NumElements = MaxGpuNotes }
        });

        _frameCbuf = _device.CreateBuffer(new BufferDescription((uint)Align16(Marshal.SizeOf<FrameConstants>()), BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
        _keyCbuf = _device.CreateBuffer(new BufferDescription(128 * 16, BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
        _quadVb = _device.CreateBuffer(new BufferDescription((uint)(Marshal.SizeOf<QuadVertex>() * MaxQuadVerts), BindFlags.VertexBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));

        _hudTex = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = HudSurface.Width,
            Height = HudSurface.Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.None
        });
        _hudSrv = _device.CreateShaderResourceView(_hudTex);
        _chromeTex = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = ChromeSurface.TexWidth,
            Height = ChromeSurface.TexHeight,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.None
        });
        _chromeSrv = _device.CreateShaderResourceView(_chromeTex);
        _sampler = _device.CreateSamplerState(new SamplerDescription
        {
            Filter = Filter.MinMagMipLinear,
            AddressU = TextureAddressMode.Clamp,
            AddressV = TextureAddressMode.Clamp,
            AddressW = TextureAddressMode.Clamp,
            ComparisonFunc = ComparisonFunction.Never,
            MinLOD = 0,
            MaxLOD = float.MaxValue
        });

        var blendDesc = new BlendDescription(Blend.SourceAlpha, Blend.InverseSourceAlpha, Blend.One, Blend.InverseSourceAlpha);
        _blend = _device.CreateBlendState(blendDesc);
        _raster = _device.CreateRasterizerState(new RasterizerDescription(CullMode.None, FillMode.Solid) { DepthClipEnable = false });
        _noDepth = _device.CreateDepthStencilState(new DepthStencilDescription
        {
            DepthEnable = false,
            DepthWriteMask = DepthWriteMask.Zero,
            DepthFunc = ComparisonFunction.Always
        });

        SetKeyboard(88);
    }

    private static ReadOnlyMemory<byte> CompileHlsl(string fileName, string entry, string profile)
    {
        string source = LoadHlslSource(fileName);
        return Compiler.Compile(source, entry, fileName, profile, ShaderFlags.OptimizationLevel3);
    }

    private static string LoadHlslSource(string fileName)
    {
        string resName = typeof(D3DRenderer).Assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("." + fileName, StringComparison.OrdinalIgnoreCase)
                              || n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));
        if (resName is not null)
        {
            using var s = typeof(D3DRenderer).Assembly.GetManifestResourceStream(resName);
            if (s is not null)
            {
                using var r = new StreamReader(s);
                return r.ReadToEnd();
            }
        }

        string path = Path.Combine(AppContext.BaseDirectory, "Shaders", fileName);
        if (File.Exists(path))
            return File.ReadAllText(path);

        throw new FileNotFoundException($"HLSL shader not found: {fileName}");
    }

    private static int Align16(int n) => (n + 15) & ~15;

    public void Dispose()
    {
        _hud.Dispose();
        _chrome.Dispose();
        _chromeSrv?.Dispose();
        _chromeTex?.Dispose();
        _noDepth?.Dispose();
        _raster?.Dispose();
        _blend?.Dispose();
        _sampler?.Dispose();
        _hudSrv?.Dispose();
        _hudTex?.Dispose();
        _quadVb?.Dispose();
        _keyCbuf?.Dispose();
        _frameCbuf?.Dispose();
        _noteSrv?.Dispose();
        _noteBuffer?.Dispose();
        _quadLayout?.Dispose();
        _solidPS?.Dispose();
        _quadPS?.Dispose();
        _quadVS?.Dispose();
        _notePS?.Dispose();
        _noteVS?.Dispose();
        _rtv?.Dispose();
        _backBuf?.Dispose();
        _swap?.Dispose();
        _ctx?.Dispose();
        _device?.Dispose();
        _factory?.Dispose();
    }
}

public sealed class HudInfo
{
    public string FileName = "";
    public double Time;
    public double Duration;
    public int NoteCount;
    public int Nps;
    public int Fps;
    public int Polyphony;
    public int Tracks;
    public int Passed;
    public bool Playing;
}
