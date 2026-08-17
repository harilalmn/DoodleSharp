using System;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace DoodleSharp.Export;

/// <summary>
/// Exports animation frames to MP4 video using Windows Media Foundation.
/// </summary>
public class VideoExporter : IDisposable
{
    private IMFSinkWriter? _sinkWriter;
    private int _videoStreamIndex;
    private long _frameCount;
    private readonly int _width;
    private readonly int _height;
    private readonly int _fps;
    private readonly uint _bitrate;
    private bool _disposed;

    // MF GUIDs
    private static readonly Guid MFMediaType_Video = new("73646976-0000-0010-8000-00AA00389B71");
    private static readonly Guid MFVideoFormat_H264 = new("34363248-0000-0010-8000-00AA00389B71");
    private static readonly Guid MFVideoFormat_RGB32 = new("00000016-0000-0010-8000-00AA00389B71");

    // MF Attributes
    private static readonly Guid MF_MT_MAJOR_TYPE = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    private static readonly Guid MF_MT_SUBTYPE = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    private static readonly Guid MF_MT_AVG_BITRATE = new("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
    private static readonly Guid MF_MT_INTERLACE_MODE = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
    private static readonly Guid MF_MT_FRAME_SIZE = new("1652c33d-d6b2-4012-b834-72030849a37d");
    private static readonly Guid MF_MT_FRAME_RATE = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
    private static readonly Guid MF_MT_PIXEL_ASPECT_RATIO = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
    private static readonly Guid MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS = new("a634a91c-822b-41b9-a494-4de4643612b0");
    private static readonly Guid MF_SINK_WRITER_DISABLE_THROTTLING = new("08b845d8-2b74-4afe-9d53-be16d2d5ae4f");

    public VideoExporter(string filePath, int width, int height, int fps = 30, uint bitrateMbps = 5)
    {
        _width = width;
        _height = height;
        _fps = fps;
        _bitrate = bitrateMbps * 1_000_000;
        _frameCount = 0;

        InitializeMediaFoundation();
        CreateSinkWriter(filePath);
    }

    private void InitializeMediaFoundation()
    {
        int hr = MFStartup(MF_VERSION, MFSTARTUP_FULL);
        if (hr < 0)
            Marshal.ThrowExceptionForHR(hr);
    }

    private void CreateSinkWriter(string filePath)
    {
        // Create sink writer
        int hr = MFCreateSinkWriterFromURL(filePath, IntPtr.Zero, null, out _sinkWriter);
        if (hr < 0)
            Marshal.ThrowExceptionForHR(hr);

        // Create output media type (H.264)
        hr = MFCreateMediaType(out IMFMediaType outputType);
        if (hr < 0)
            Marshal.ThrowExceptionForHR(hr);

        outputType.SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video);
        outputType.SetGUID(MF_MT_SUBTYPE, MFVideoFormat_H264);
        outputType.SetUINT32(MF_MT_AVG_BITRATE, _bitrate);
        outputType.SetUINT32(MF_MT_INTERLACE_MODE, 2); // Progressive
        outputType.SetUINT64(MF_MT_FRAME_SIZE, ((ulong)_width << 32) | (uint)_height);
        outputType.SetUINT64(MF_MT_FRAME_RATE, ((ulong)_fps << 32) | 1);
        outputType.SetUINT64(MF_MT_PIXEL_ASPECT_RATIO, (1UL << 32) | 1);

        hr = _sinkWriter.AddStream(outputType, out _videoStreamIndex);
        if (hr < 0)
            Marshal.ThrowExceptionForHR(hr);

        Marshal.ReleaseComObject(outputType);

        // Create input media type (RGB32)
        hr = MFCreateMediaType(out IMFMediaType inputType);
        if (hr < 0)
            Marshal.ThrowExceptionForHR(hr);

        inputType.SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video);
        inputType.SetGUID(MF_MT_SUBTYPE, MFVideoFormat_RGB32);
        inputType.SetUINT32(MF_MT_INTERLACE_MODE, 2); // Progressive
        inputType.SetUINT64(MF_MT_FRAME_SIZE, ((ulong)_width << 32) | (uint)_height);
        inputType.SetUINT64(MF_MT_FRAME_RATE, ((ulong)_fps << 32) | 1);
        inputType.SetUINT64(MF_MT_PIXEL_ASPECT_RATIO, (1UL << 32) | 1);

        hr = _sinkWriter.SetInputMediaType(_videoStreamIndex, inputType, null);
        if (hr < 0)
            Marshal.ThrowExceptionForHR(hr);

        Marshal.ReleaseComObject(inputType);

        // Start writing
        hr = _sinkWriter.BeginWriting();
        if (hr < 0)
            Marshal.ThrowExceptionForHR(hr);
    }

    public void AddFrame(RenderTargetBitmap bitmap)
    {
        if (_disposed || _sinkWriter == null)
            throw new ObjectDisposedException(nameof(VideoExporter));

        // Convert RenderTargetBitmap to byte array (BGRA format)
        int stride = _width * 4;
        byte[] pixels = new byte[_height * stride];

        // Create a FormatConvertedBitmap to ensure BGRA32 format
        var convertedBitmap = new FormatConvertedBitmap(bitmap, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        convertedBitmap.CopyPixels(pixels, stride, 0);

        // Create MF sample
        int hr = MFCreateMemoryBuffer(pixels.Length, out IMFMediaBuffer buffer);
        if (hr < 0)
            Marshal.ThrowExceptionForHR(hr);

        try
        {
            // Lock buffer and copy pixels
            hr = buffer.Lock(out IntPtr bufferData, out _, out _);
            if (hr < 0)
                Marshal.ThrowExceptionForHR(hr);

            // MF expects bottom-up RGB, but WPF gives top-down, so we need to flip
            for (int y = 0; y < _height; y++)
            {
                int srcOffset = y * stride;
                int dstOffset = (_height - 1 - y) * stride;
                Marshal.Copy(pixels, srcOffset, bufferData + dstOffset, stride);
            }

            hr = buffer.Unlock();
            if (hr < 0)
                Marshal.ThrowExceptionForHR(hr);

            hr = buffer.SetCurrentLength(pixels.Length);
            if (hr < 0)
                Marshal.ThrowExceptionForHR(hr);

            // Create sample
            hr = MFCreateSample(out IMFSample sample);
            if (hr < 0)
                Marshal.ThrowExceptionForHR(hr);

            try
            {
                sample.AddBuffer(buffer);

                // Set timestamp (100-nanosecond units)
                long frameDuration = 10_000_000L / _fps;
                sample.SetSampleTime(_frameCount * frameDuration);
                sample.SetSampleDuration(frameDuration);

                // Write sample
                hr = _sinkWriter.WriteSample(_videoStreamIndex, sample);
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);

                _frameCount++;
            }
            finally
            {
                Marshal.ReleaseComObject(sample);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(buffer);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_sinkWriter != null)
        {
            _sinkWriter.Finalize_();
            Marshal.ReleaseComObject(_sinkWriter);
            _sinkWriter = null;
        }

        MFShutdown();
    }

    #region Media Foundation Interop

    private const uint MF_VERSION = 0x00020070;
    private const uint MFSTARTUP_FULL = 0;

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFStartup(uint version, uint flags);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFShutdown();

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateMediaType(out IMFMediaType ppMFType);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateMemoryBuffer(int cbMaxLength, out IMFMediaBuffer ppBuffer);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateSample(out IMFSample ppIMFSample);

    [DllImport("mfreadwrite.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int MFCreateSinkWriterFromURL(
        string pwszOutputURL,
        IntPtr pByteStream,
        IMFAttributes? pAttributes,
        out IMFSinkWriter ppSinkWriter);

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3")]
    private interface IMFAttributes
    {
        void GetItem([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, IntPtr pValue);
        void GetItemType([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, out uint pType);
        void CompareItem([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, IntPtr Value, out bool pbResult);
        void Compare(IMFAttributes pTheirs, uint MatchType, out bool pbResult);
        void GetUINT32([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, out uint punValue);
        void GetUINT64([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, out ulong punValue);
        void GetDouble([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, out double pfValue);
        void GetGUID([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, out Guid pguidValue);
        void GetStringLength([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, out uint pcchLength);
        void GetString([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] out string pwszValue, uint cchBufSize, out uint pcchLength);
        void GetAllocatedString([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] out string ppwszValue, out uint pcchLength);
        void GetBlobSize([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, out uint pcbBlobSize);
        void GetBlob([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, IntPtr pBuf, uint cbBufSize, out uint pcbBlobSize);
        void GetAllocatedBlob([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, out IntPtr ppBuf, out uint pcbSize);
        void GetUnknown([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
        void SetItem([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, IntPtr Value);
        void DeleteItem([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey);
        void DeleteAllItems();
        void SetUINT32([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, uint unValue);
        void SetUINT64([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, ulong unValue);
        void SetDouble([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, double fValue);
        void SetGUID([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, [MarshalAs(UnmanagedType.LPStruct)] Guid guidValue);
        void SetString([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
        void SetBlob([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, IntPtr pBuf, uint cbBufSize);
        void SetUnknown([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        void LockStore();
        void UnlockStore();
        void GetCount(out uint pcItems);
        void GetItemByIndex(uint unIndex, out Guid pguidKey, IntPtr pValue);
        void CopyAllItems(IMFAttributes pDest);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555")]
    private interface IMFMediaType : IMFAttributes
    {
        // IMFAttributes methods inherited
        new void GetItem([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, IntPtr pValue);
        new void GetItemType([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, out uint pType);
        new void CompareItem([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, IntPtr Value, out bool pbResult);
        new void Compare(IMFAttributes pTheirs, uint MatchType, out bool pbResult);
        new void GetUINT32([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, out uint punValue);
        new void GetUINT64([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, out ulong punValue);
        new void GetDouble([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, out double pfValue);
        new void GetGUID([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, out Guid pguidValue);
        new void GetStringLength([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, out uint pcchLength);
        new void GetString([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] out string pwszValue, uint cchBufSize, out uint pcchLength);
        new void GetAllocatedString([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] out string ppwszValue, out uint pcchLength);
        new void GetBlobSize([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, out uint pcbBlobSize);
        new void GetBlob([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, IntPtr pBuf, uint cbBufSize, out uint pcbBlobSize);
        new void GetAllocatedBlob([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, out IntPtr ppBuf, out uint pcbSize);
        new void GetUnknown([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
        new void SetItem([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, IntPtr Value);
        new void DeleteItem([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey);
        new void DeleteAllItems();
        new void SetUINT32([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, uint unValue);
        new void SetUINT64([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, ulong unValue);
        new void SetDouble([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, double fValue);
        new void SetGUID([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, [MarshalAs(UnmanagedType.LPStruct)] Guid guidValue);
        new void SetString([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
        new void SetBlob([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, IntPtr pBuf, uint cbBufSize);
        new void SetUnknown([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        new void LockStore();
        new void UnlockStore();
        new void GetCount(out uint pcItems);
        new void GetItemByIndex(uint unIndex, out Guid pguidKey, IntPtr pValue);
        new void CopyAllItems(IMFAttributes pDest);

        // IMFMediaType methods
        void GetMajorType(out Guid pguidMajorType);
        void IsCompressedFormat(out bool pfCompressed);
        void IsEqual(IMFMediaType pIMediaType, out uint pdwFlags);
        void GetRepresentation(Guid guidRepresentation, out IntPtr ppvRepresentation);
        void FreeRepresentation(Guid guidRepresentation, IntPtr pvRepresentation);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("045fa593-8799-42b8-bc8d-8968c6453507")]
    private interface IMFMediaBuffer
    {
        int Lock(out IntPtr ppbBuffer, out int pcbMaxLength, out int pcbCurrentLength);
        int Unlock();
        int GetCurrentLength(out int pcbCurrentLength);
        int SetCurrentLength(int cbCurrentLength);
        int GetMaxLength(out int pcbMaxLength);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4")]
    private interface IMFSample
    {
        // IMFAttributes methods (inherited but not used directly)
        void GetItem([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, IntPtr pValue);
        void GetItemType([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, out uint pType);
        void CompareItem([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, IntPtr Value, out bool pbResult);
        void Compare(IMFAttributes pTheirs, uint MatchType, out bool pbResult);
        void GetUINT32([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, out uint punValue);
        void GetUINT64([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, out ulong punValue);
        void GetDouble([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, out double pfValue);
        void GetGUID([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, out Guid pguidValue);
        void GetStringLength([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, out uint pcchLength);
        void GetString([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] out string pwszValue, uint cchBufSize, out uint pcchLength);
        void GetAllocatedString([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] out string ppwszValue, out uint pcchLength);
        void GetBlobSize([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, out uint pcbBlobSize);
        void GetBlob([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, IntPtr pBuf, uint cbBufSize, out uint pcbBlobSize);
        void GetAllocatedBlob([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, out IntPtr ppBuf, out uint pcbSize);
        void GetUnknown([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
        void SetItem([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, IntPtr Value);
        void DeleteItem([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey);
        void DeleteAllItems();
        void SetUINT32([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, uint unValue);
        void SetUINT64([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, ulong unValue);
        void SetDouble([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, double fValue);
        void SetGUID([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, [MarshalAs(UnmanagedType.LPStruct)] Guid guidValue);
        void SetString([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
        void SetBlob([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, IntPtr pBuf, uint cbBufSize);
        void SetUnknown([MarshalAs(UnmanagedType.LPStruct)] Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        void LockStore();
        void UnlockStore();
        void GetCount(out uint pcItems);
        void GetItemByIndex(uint unIndex, out Guid pguidKey, IntPtr pValue);
        void CopyAllItems(IMFAttributes pDest);

        // IMFSample methods
        void GetSampleFlags(out uint pdwSampleFlags);
        void SetSampleFlags(uint dwSampleFlags);
        void GetSampleTime(out long phnsSampleTime);
        void SetSampleTime(long hnsSampleTime);
        void GetSampleDuration(out long phnsSampleDuration);
        void SetSampleDuration(long hnsSampleDuration);
        void GetBufferCount(out uint pdwBufferCount);
        void GetBufferByIndex(uint dwIndex, out IMFMediaBuffer ppBuffer);
        void ConvertToContiguousBuffer(out IMFMediaBuffer ppBuffer);
        void AddBuffer(IMFMediaBuffer pBuffer);
        void RemoveBufferByIndex(uint dwIndex);
        void RemoveAllBuffers();
        void GetTotalLength(out uint pcbTotalLength);
        void CopyToBuffer(IMFMediaBuffer pBuffer);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("3137f1cd-fe5e-4805-a5d8-fb477448cb3d")]
    private interface IMFSinkWriter
    {
        int AddStream(IMFMediaType pTargetMediaType, out int pdwStreamIndex);
        int SetInputMediaType(int dwStreamIndex, IMFMediaType pInputMediaType, IMFAttributes? pEncodingParameters);
        int BeginWriting();
        int WriteSample(int dwStreamIndex, IMFSample pSample);
        int SendStreamTick(int dwStreamIndex, long hnsTimestamp);
        int PlaceMarker(int dwStreamIndex, IntPtr pvContext);
        int NotifyEndOfSegment(int dwStreamIndex);
        int Flush(int dwStreamIndex);
        [PreserveSig]
        int Finalize_();
        int GetServiceForStream(int dwStreamIndex, ref Guid guidService, ref Guid riid, out IntPtr ppvObject);
        int GetStatistics(int dwStreamIndex, out MF_SINK_WRITER_STATISTICS pStats);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MF_SINK_WRITER_STATISTICS
    {
        public uint cb;
        public long llLastTimestampReceived;
        public long llLastTimestampEncoded;
        public long llLastTimestampProcessed;
        public long llLastStreamTickReceived;
        public long llLastSinkSampleRequest;
        public ulong qwNumSamplesReceived;
        public ulong qwNumSamplesEncoded;
        public ulong qwNumSamplesProcessed;
        public ulong qwNumStreamTicksReceived;
        public uint dwByteCountQueued;
        public ulong qwByteCountProcessed;
        public uint dwNumOutstandingSinkSampleRequests;
        public uint dwAverageSampleRateReceived;
        public uint dwAverageSampleRateEncoded;
        public uint dwAverageSampleRateProcessed;
    }

    #endregion
}
