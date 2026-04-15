using System.Runtime.InteropServices;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.Windows;
using Vortice.DXGI;
using Vortice.Direct3D11;
using Vortice.Mathematics;

namespace RemoteHost;

/// <summary>DXGI Desktop Duplication feeding VP8 encoder via WindowsVideoEndPoint.</summary>
public sealed class DesktopVideoSession : IDisposable
{
    private readonly WindowsVideoEndPoint _videoEndPoint;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public DesktopVideoSession(WindowsVideoEndPoint videoEndPoint)
    {
        _videoEndPoint = videoEndPoint;
    }

    public void Start()
    {
        Stop();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loop = Task.Run(() => RunCaptureLoop(token), token);
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { /* ignore */ }
        try { _loop?.Wait(TimeSpan.FromSeconds(3)); } catch { /* ignore */ }
        _cts = null;
        _loop = null;
    }

    private void RunCaptureLoop(CancellationToken ct)
    {
        IDXGIOutputDuplication? duplication = null;
        ID3D11Device? device = null;
        ID3D11Texture2D? staging = null;
        int width = 0;
        int height = 0;

        try
        {
            if (!TryInitDxgi(out device, out duplication))
            {
                Console.WriteLine("[desktop] DXGI init failed; no frames sent.");
                return;
            }

            var frameMs = 1000 / 15;
            while (!ct.IsCancellationRequested)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                DXGI_OUTDUPL_FRAME_INFO frameInfo;
                IDXGIResource? desktopResource = null;
                try
                {
                    var hr = duplication!.AcquireNextFrame(100, out frameInfo, out desktopResource);
                    const uint DXGI_ERROR_WAIT_TIMEOUT = 0x887A0027;
                    if (!hr.Success && (uint)hr.Code == DXGI_ERROR_WAIT_TIMEOUT)
                    {
                        Thread.Sleep(5);
                        continue;
                    }
                    if (hr.Failure)
                        continue;

                    using var tex = desktopResource!.QueryInterface<ID3D11Texture2D>();
                    var desc = tex.Description;
                    if (staging is null || desc.Width != width || desc.Height != height)
                    {
                        staging?.Dispose();
                        width = (int)desc.Width;
                        height = (int)desc.Height;
                        staging = device!.CreateTexture2D(new Texture2DDescription
                        {
                            Width = desc.Width,
                            Height = desc.Height,
                            MipLevels = 1,
                            ArraySize = 1,
                            Format = desc.Format,
                            SampleDescription = new SampleDescription(1, 0),
                            Usage = ResourceUsage.Staging,
                            CPUAccessFlags = CpuAccessFlags.Read,
                            BindFlags = BindFlags.None,
                        });
                    }

                    device!.ImmediateContext.CopyResource(staging!, tex);

                    var mapped = device.ImmediateContext.Map(staging!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                    try
                    {
                        var rowPitch = (int)mapped.RowPitch;
                        var copy = new byte[width * 4 * height];
                        for (int row = 0; row < height; row++)
                        {
                            Marshal.Copy(
                                IntPtr.Add(mapped.DataPointer, row * rowPitch),
                                copy,
                                row * width * 4,
                                width * 4);
                        }
                        _videoEndPoint.ExternalVideoSourceRawSample((uint)frameMs, width, height, copy, VideoPixelFormatsEnum.Bgra);
                    }
                    finally
                    {
                        device.ImmediateContext.Unmap(staging!, 0);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[desktop] frame: " + ex.Message);
                }
                finally
                {
                    try
                    {
                        if (desktopResource != null)
                            duplication?.ReleaseFrame();
                    }
                    catch { /* ignore */ }
                    desktopResource?.Dispose();
                }

                var elapsed = (int)sw.ElapsedMilliseconds;
                if (elapsed < frameMs)
                    Thread.Sleep(frameMs - elapsed);
            }
        }
        finally
        {
            staging?.Dispose();
            duplication?.Dispose();
            device?.Dispose();
        }
    }

    private static bool TryInitDxgi(out ID3D11Device? device, out IDXGIOutputDuplication? duplication)
    {
        device = null;
        duplication = null;
        try
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            factory.EnumAdapters1(0, out var adapter).CheckError();
            adapter.EnumOutputs(0, out var output).CheckError();
            using var output1 = output.QueryInterface<IDXGIOutput1>();
            var flags = DeviceCreationFlags.BgraSupport;
            D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, flags, null!, out device).CheckError();
            output1.DuplicateOutput(device, out duplication).CheckError();
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[desktop] " + ex.Message);
            return false;
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
