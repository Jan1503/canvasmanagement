using System.Runtime.InteropServices;
using System.Text;
using ManagedBass;

namespace CanvasManagement.Extension.AudioPlayer;

/// <summary>
///     BASS-based audio player for cross-platform audio playback with FFT analysis
///     BASS natively handles audio output AND provides FFT data - no manual forwarding needed!
/// </summary>
internal sealed class BassAudioPlayer(int sampleRate = 44100) : IDisposable
{
    // FFT buffer for spectrum analysis
    private readonly float[] _fftBuffer = new float[2048]; // 2048 = FFT size

    private string _audioUrl = "";
    private bool _disposed;
    private int _stream;

    // Track metadata

    public bool IsInitialized { get; private set; }
    public bool IsPlaying => Bass.ChannelIsActive(_stream) == PlaybackState.Playing;

    // Metadata properties
    public string TrackTitle { get; private set; } = "";

    public string TrackArtist { get; private set; } = "";

    public string TrackAlbum { get; private set; } = "";

    public string TrackInfo
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(TrackArtist))
                parts.Add(TrackArtist);
            if (!string.IsNullOrEmpty(TrackTitle))
                parts.Add(TrackTitle);
            if (!string.IsNullOrEmpty(TrackAlbum))
                parts.Add($"({TrackAlbum})");

            return parts.Count > 0 ? string.Join(" - ", parts) : "Unknown";
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            if (_stream != 0)
            {
                Bass.ChannelStop(_stream);
                Bass.StreamFree(_stream);
                _stream = 0;
            }

            if (IsInitialized)
            {
                Bass.Free();
                IsInitialized = false;
                Console.WriteLine("[BASS] Audio engine freed");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BASS] Exception during cleanup: {ex.Message}");
        }
    }

    public bool Initialize(int deviceId = -1)
    {
        try
        {
            Console.WriteLine("[BASS] Initializing BASS audio engine...");

            // Initialize BASS audio system
            // -1 = default device, sampleRate, 0 = no flags
            if (!Bass.Init(deviceId, sampleRate))
            {
                var error = Bass.LastError;
                Console.WriteLine($"[BASS] Failed to initialize: {error}");
                return false;
            }

            Console.WriteLine($"[BASS] Initialized successfully on device {deviceId}");
            Console.WriteLine($"[BASS] Sample rate: {sampleRate}Hz");
            Console.WriteLine($"[BASS] Version: {Bass.Version}");

            // Get device info
            if (Bass.GetDeviceInfo(Bass.CurrentDevice, out var deviceInfo))
                Console.WriteLine($"[BASS] Device: {deviceInfo.Name}");

            IsInitialized = true;
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BASS] Exception during initialization: {ex.Message}");
            return false;
        }
    }

    public bool LoadAndPlay(string url)
    {
        if (!IsInitialized)
        {
            Console.WriteLine("[BASS] Not initialized!");
            return false;
        }

        try
        {
            Console.WriteLine($"[BASS] Loading: {url}");

            // Stop and free existing stream
            if (_stream != 0)
            {
                Bass.ChannelStop(_stream);
                Bass.StreamFree(_stream);
                _stream = 0;
            }

            // Store URL for metadata
            SetAudioUrl(url);

            // Determine if URL or file
            var flags = BassFlags.Default | BassFlags.AutoFree;

            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                // Create stream from URL (internet radio, HTTP stream)
                _stream = Bass.CreateStream(url, 0, flags, null);
                Console.WriteLine($"[BASS] Created URL stream: {_stream}");
            }
            else
            {
                // Create stream from file
                _stream = Bass.CreateStream(url, 0, 0, flags);
                Console.WriteLine($"[BASS] Created file stream: {_stream}");
            }

            if (_stream == 0)
            {
                var error = Bass.LastError;
                Console.WriteLine($"[BASS] Failed to create stream: {error}");
                return false;
            }

            // Start playback - BASS handles audio output automatically!
            if (!Bass.ChannelPlay(_stream))
            {
                var error = Bass.LastError;
                Console.WriteLine($"[BASS] Failed to start playback: {error}");
                return false;
            }

            Console.WriteLine("[BASS] Playback started successfully!");
            Console.WriteLine("[BASS] Audio output and FFT analysis both active!");

            // Extract metadata tags
            ExtractMetadata();

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BASS] Exception during load/play: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    ///     Extract metadata from the current stream (title, artist, album, etc.)
    /// </summary>
    private void ExtractMetadata()
    {
        try
        {
            // Clear existing metadata
            TrackTitle = "";
            TrackArtist = "";
            TrackAlbum = "";

            if (_stream == 0)
                return;

            // Get channel info to determine stream type
            var channelInfo = Bass.ChannelGetInfo(_stream);
            Console.WriteLine($"[BASS] Stream type: {channelInfo.ChannelType}, Flags: {channelInfo.Flags}");

            // Try different tag retrieval methods based on stream type
            // Using proper TagType enum values

            // Method 1: HTTP/ICY tags for internet radio streams
            var tags = Bass.ChannelGetTags(_stream, TagType.HTTP);
            if (tags != IntPtr.Zero)
            {
                Console.WriteLine("[BASS] Found HTTP/ICY tags");
                ParseHttpTags(tags);
            }

            // Method 2: OGG Vorbis comments
            tags = Bass.ChannelGetTags(_stream, TagType.OGG);
            if (tags != IntPtr.Zero)
            {
                Console.WriteLine("[BASS] Found OGG tags");
                ParseOggTags(tags);
            }

            // Method 3: ID3v1 tags
            tags = Bass.ChannelGetTags(_stream, TagType.ID3);
            if (tags != IntPtr.Zero)
            {
                Console.WriteLine("[BASS] Found ID3v1 tags");
                ParseId3v1Tags(tags);
            }

            // Method 4: ID3v2 tags - try casting to int
            tags = Bass.ChannelGetTags(_stream, (TagType)3);
            if (tags != IntPtr.Zero)
            {
                Console.WriteLine("[BASS] Found ID3v2 tags");
                ParseId3v2Tags(tags);
            }

            // Method 5: META tags - Shoutcast metadata updates (cast to int)
            tags = Bass.ChannelGetTags(_stream, (TagType)5);
            if (tags != IntPtr.Zero)
            {
                Console.WriteLine("[BASS] Found META tags");
                var meta = Marshal.PtrToStringAnsi(tags);
                if (!string.IsNullOrEmpty(meta))
                {
                    Console.WriteLine($"[BASS] META: {meta}");
                    ParseMetaTag(meta);
                }
            }

            // Method 6: WMA tags
            tags = Bass.ChannelGetTags(_stream, TagType.WMA);
            if (tags != IntPtr.Zero)
            {
                Console.WriteLine("[BASS] Found WMA tags");
                ParseOggTags(tags); // WMA uses similar format
            }

            // Fallback: use filename for local files
            if (string.IsNullOrEmpty(TrackTitle) && !string.IsNullOrEmpty(_audioUrl) && !_audioUrl.StartsWith("http"))
                TrackTitle = Path.GetFileNameWithoutExtension(_audioUrl);

            // Log final metadata
            if (!string.IsNullOrEmpty(TrackTitle) || !string.IsNullOrEmpty(TrackArtist))
                Console.WriteLine($"[BASS] Track Info: {TrackInfo}");
            else
                Console.WriteLine("[BASS] No metadata found");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BASS] Error extracting metadata: {ex.Message}");
        }
    }

    /// <summary>
    ///     Parse HTTP/ICY headers
    /// </summary>
    private void ParseHttpTags(IntPtr tags)
    {
        try
        {
            var offset = 0;
            while (true)
            {
                var tagPtr = IntPtr.Add(tags, offset);
                var tag = Marshal.PtrToStringAnsi(tagPtr);

                if (string.IsNullOrEmpty(tag))
                    break;

                Console.WriteLine($"[BASS] HTTP tag: {tag}");

                // Parse key:value format
                if (tag.Contains(':'))
                {
                    var parts = tag.Split(new[] { ':' }, 2);
                    if (parts.Length == 2)
                    {
                        var key = parts[0].Trim().ToLowerInvariant();
                        var value = parts[1].Trim();

                        switch (key)
                        {
                            case "icy-name":
                                TrackArtist = value;
                                break;
                            case "icy-description":
                                if (string.IsNullOrEmpty(TrackTitle))
                                    TrackTitle = value;
                                break;
                            case "icy-genre":
                                TrackAlbum = value;
                                break;
                            case "icy-url":
                                // Station URL
                                break;
                        }
                    }
                }

                offset += tag.Length + 1;

                // Safety limit
                if (offset > 8192)
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BASS] Error parsing HTTP tags: {ex.Message}");
        }
    }

    /// <summary>
    ///     Parse OGG Vorbis comments
    /// </summary>
    private void ParseOggTags(IntPtr tags)
    {
        try
        {
            var offset = 0;
            while (true)
            {
                var tagPtr = IntPtr.Add(tags, offset);
                var tag = Marshal.PtrToStringAnsi(tagPtr);

                if (string.IsNullOrEmpty(tag))
                    break;

                Console.WriteLine($"[BASS] OGG tag: {tag}");

                // Parse KEY=VALUE format
                if (tag.Contains('='))
                {
                    var parts = tag.Split(new[] { '=' }, 2);
                    if (parts.Length == 2)
                    {
                        var key = parts[0].Trim().ToLowerInvariant();
                        var value = parts[1].Trim();

                        switch (key)
                        {
                            case "title":
                                TrackTitle = value;
                                break;
                            case "artist":
                                TrackArtist = value;
                                break;
                            case "album":
                                TrackAlbum = value;
                                break;
                        }
                    }
                }

                offset += tag.Length + 1;

                // Safety limit
                if (offset > 8192)
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BASS] Error parsing OGG tags: {ex.Message}");
        }
    }

    /// <summary>
    ///     Parse ID3v1 tags (128 bytes at end of file)
    /// </summary>
    private void ParseId3v1Tags(IntPtr tags)
    {
        try
        {
            // Read 128 bytes of ID3v1 tag
            var tagData = new byte[128];
            Marshal.Copy(tags, tagData, 0, 128);

            // Check for "TAG" header
            if (tagData[0] == 'T' && tagData[1] == 'A' && tagData[2] == 'G')
            {
                // Title: bytes 3-32
                TrackTitle = Encoding.ASCII.GetString(tagData, 3, 30).Trim('\0', ' ');

                // Artist: bytes 33-62
                TrackArtist = Encoding.ASCII.GetString(tagData, 33, 30).Trim('\0', ' ');

                // Album: bytes 63-92
                TrackAlbum = Encoding.ASCII.GetString(tagData, 63, 30).Trim('\0', ' ');

                Console.WriteLine($"[BASS] ID3v1: Title={TrackTitle}, Artist={TrackArtist}, Album={TrackAlbum}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BASS] Error parsing ID3v1 tags: {ex.Message}");
        }
    }

    /// <summary>
    ///     Parse ID3v2 tags
    /// </summary>
    private void ParseId3v2Tags(IntPtr tags)
    {
        try
        {
            // ID3v2 is complex, try to read basic frames
            var offset = 10; // Skip ID3v2 header (10 bytes)

            while (offset < 2048) // Safety limit
                try
                {
                    // Read frame ID (4 bytes)
                    var frameIdPtr = IntPtr.Add(tags, offset);
                    var frameId = Marshal.PtrToStringAnsi(frameIdPtr, 4);

                    if (string.IsNullOrEmpty(frameId) || frameId[0] == '\0')
                        break;

                    // Read frame size (4 bytes)
                    var sizePtr = IntPtr.Add(tags, offset + 4);
                    var sizeBytes = new byte[4];
                    Marshal.Copy(sizePtr, sizeBytes, 0, 4);
                    var frameSize = (sizeBytes[0] << 21) | (sizeBytes[1] << 14) | (sizeBytes[2] << 7) | sizeBytes[3];

                    if (frameSize <= 0 || frameSize > 10000)
                        break;

                    // Skip flags (2 bytes)
                    offset += 10;

                    // Read frame data
                    var dataPtr = IntPtr.Add(tags, offset);
                    var encoding = Marshal.ReadByte(dataPtr); // First byte is text encoding
                    var textPtr = IntPtr.Add(dataPtr, 1);
                    var text = Marshal.PtrToStringAnsi(textPtr, Math.Min(frameSize - 1, 256));

                    Console.WriteLine($"[BASS] ID3v2 frame: {frameId} = {text}");

                    // Map frame IDs to metadata
                    switch (frameId)
                    {
                        case "TIT2": // Title
                            TrackTitle = text?.Trim('\0') ?? "";
                            break;
                        case "TPE1": // Artist
                            TrackArtist = text?.Trim('\0') ?? "";
                            break;
                        case "TALB": // Album
                            TrackAlbum = text?.Trim('\0') ?? "";
                            break;
                    }

                    offset += frameSize;
                }
                catch
                {
                    break;
                }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BASS] Error parsing ID3v2 tags: {ex.Message}");
        }
    }

    /// <summary>
    ///     Parse META tag (StreamTitle)
    /// </summary>
    private void ParseMetaTag(string meta)
    {
        try
        {
            // Format: StreamTitle='Artist - Title';StreamUrl='http://...';
            if (meta.Contains("StreamTitle="))
            {
                var start = meta.IndexOf("StreamTitle='") + 13;
                var end = meta.IndexOf("';", start);

                if (end > start)
                {
                    var title = meta.Substring(start, end - start);

                    // Try to split Artist - Title
                    if (title.Contains(" - "))
                    {
                        var parts = title.Split(new[] { " - " }, 2, StringSplitOptions.None);
                        TrackArtist = parts[0].Trim();
                        TrackTitle = parts[1].Trim();
                    }
                    else
                    {
                        TrackTitle = title.Trim();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BASS] Error parsing META tag: {ex.Message}");
        }
    }

    /// <summary>
    ///     Update metadata for internet radio streams (called periodically)
    /// </summary>
    public void UpdateMetadata()
    {
        if (_stream == 0 || !IsPlaying)
            return;

        try
        {
            // Try to get updated META tags (Shoutcast updates) - cast to TagType
            var tags = Bass.ChannelGetTags(_stream, (TagType)5);

            if (tags != IntPtr.Zero)
            {
                var meta = Marshal.PtrToStringAnsi(tags);
                if (!string.IsNullOrEmpty(meta))
                {
                    Console.WriteLine($"[BASS] Metadata update: {meta}");

                    var oldTitle = TrackTitle;
                    var oldArtist = TrackArtist;

                    ParseMetaTag(meta);

                    // Only log if changed
                    if (TrackTitle != oldTitle || TrackArtist != oldArtist)
                        Console.WriteLine($"[BASS] Track changed: {TrackInfo}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BASS] Error updating metadata: {ex.Message}");
        }
    }

    /// <summary>
    ///     Store audio URL for metadata extraction
    /// </summary>
    public void SetAudioUrl(string url)
    {
        _audioUrl = url;
    }

    public void Stop()
    {
        if (_stream != 0)
        {
            Bass.ChannelStop(_stream);
            Console.WriteLine("[BASS] Playback stopped");
        }
    }

    public void Pause()
    {
        if (_stream != 0) Bass.ChannelPause(_stream);
    }

    public void Resume()
    {
        if (_stream != 0) Bass.ChannelPlay(_stream);
    }

    public bool SetVolume(int volume)
    {
        if (_stream == 0)
            return false;

        // BASS volume is 0.0 to 1.0
        var bassVolume = volume / 100.0;
        return Bass.ChannelSetAttribute(_stream, ChannelAttribute.Volume, bassVolume);
    }

    /// <summary>
    ///     Get FFT data for spectrum analysis
    ///     This is SUPER EASY with BASS - no manual processing needed!
    /// </summary>
    public bool GetFFTData(float[] leftChannel, float[] rightChannel, float[] spectrumBands)
    {
        if (_stream == 0 || !IsPlaying)
            return false;

        try
        {
            // Get FFT data from BASS (2048-point FFT)
            // BASS automatically performs FFT on the playing audio!
            var result = Bass.ChannelGetData(_stream, _fftBuffer, (int)DataFlags.FFT2048);

            if (result < 0)
                return false;

            // BASS returns FFT data as frequency bins
            // _fftBuffer[0..1023] = frequency magnitudes (1024 bins for 2048-point FFT)

            // Calculate RMS levels for left/right channels with OPTIMIZED boost
            // (BASS mixes to mono for FFT, so we'll use overall level for both)
            float totalEnergy = 0;
            for (var i = 0; i < 1024; i++) totalEnergy += _fftBuffer[i] * _fftBuffer[i];

            // OPTIMIZED BOOST: 20x for best balance
            var rmsLevel = (float)Math.Sqrt(totalEnergy / 1024) * 20f;
            rmsLevel = Math.Min(1.0f, rmsLevel); // Clamp to 0-1

            // For stereo simulation, add slight variation
            if (leftChannel.Length > 0) leftChannel[0] = rmsLevel * 0.95f; // Slightly lower for left

            if (rightChannel.Length > 0) rightChannel[0] = rmsLevel * 1.05f; // Slightly higher for right

            // Map FFT bins to spectrum bands - always fill all 16 bands
            // 16-band spectrum (detailed)
            spectrumBands[0] = CalculateBandEnergy(0, 4); // 0-100Hz
            spectrumBands[1] = CalculateBandEnergy(5, 9); // 100-200Hz
            spectrumBands[2] = CalculateBandEnergy(10, 14); // 200-300Hz
            spectrumBands[3] = CalculateBandEnergy(15, 18); // 300-400Hz
            spectrumBands[4] = CalculateBandEnergy(19, 27); // 400-600Hz
            spectrumBands[5] = CalculateBandEnergy(28, 37); // 600-800Hz
            spectrumBands[6] = CalculateBandEnergy(38, 55); // 800-1200Hz
            spectrumBands[7] = CalculateBandEnergy(56, 74); // 1200-1600Hz
            spectrumBands[8] = CalculateBandEnergy(75, 111); // 1600-2400Hz
            spectrumBands[9] = CalculateBandEnergy(112, 148); // 2400-3200Hz
            spectrumBands[10] = CalculateBandEnergy(149, 222); // 3200-4800Hz
            spectrumBands[11] = CalculateBandEnergy(223, 297); // 4800-6400Hz
            spectrumBands[12] = CalculateBandEnergy(298, 446); // 6400-9600Hz
            spectrumBands[13] = CalculateBandEnergy(447, 595); // 9600-12800Hz
            spectrumBands[14] = CalculateBandEnergy(596, 809); // 12800-17400Hz
            spectrumBands[15] = CalculateBandEnergy(810, 1023); // 17400-22050Hz

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BASS] Exception getting FFT data: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    ///     Get waveform data for oscilloscope visualization
    /// </summary>
    public bool GetWaveformData(float[] waveformBuffer)
    {
        if (_stream == 0 || !IsPlaying)
            return false;

        try
        {
            // Get PCM sample data from BASS
            // Request exactly the buffer size we need with Float flag
            var bytesToRead = waveformBuffer.Length * sizeof(float);
            var result = Bass.ChannelGetData(_stream, waveformBuffer, bytesToRead | (int)DataFlags.Float);

            if (result < 0)
                // If that fails, try without size specification
                result = Bass.ChannelGetData(_stream, waveformBuffer, (int)DataFlags.Float);

            return result >= 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BASS] Exception getting waveform data: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    ///     Detect beat/kick drum in bass frequencies
    /// </summary>
    public float DetectBeat()
    {
        if (_stream == 0 || !IsPlaying)
            return 0f;

        try
        {
            // Get FFT data
            var result = Bass.ChannelGetData(_stream, _fftBuffer, (int)DataFlags.FFT2048);
            if (result < 0)
                return 0f;

            // Focus on bass frequencies (20-200Hz) for beat detection
            // These correspond to kick drums and bass drops
            float bassEnergy = 0;
            for (var i = 0; i < 10; i++) // bins 0-9 = 0-200Hz
                bassEnergy += _fftBuffer[i] * _fftBuffer[i];

            // Normalize and boost
            var beatLevel = (float)Math.Sqrt(bassEnergy / 10) * 80f;
            return Math.Min(1.0f, beatLevel);
        }
        catch
        {
            return 0f;
        }
    }

    private float CalculateBandEnergy(int startBin, int endBin)
    {
        float energy = 0;
        var count = 0;

        for (var i = startBin; i <= endBin && i < _fftBuffer.Length; i++)
        {
            energy += _fftBuffer[i] * _fftBuffer[i];
            count++;
        }

        if (count == 0)
            return 0;

        // RMS and logarithmic scaling with OPTIMIZED boost
        var rms = (float)Math.Sqrt(energy / count);

        // OPTIMIZED BOOST: 12x for best color distribution
        var scaled = (float)Math.Log10(1 + rms * 99) * 12f;

        return Math.Min(1.0f, scaled);
    }
}