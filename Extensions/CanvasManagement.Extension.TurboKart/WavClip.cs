namespace CanvasManagement.Extension.TurboKart;

/// <summary>Reads a 16-bit PCM WAV into a mono float buffer and gets it ready to mix.</summary>
internal static class WavClip
{
    public static (float[] Mono, int Rate) Load(Stream stream)
    {
        using var reader = new BinaryReader(stream);
        if (new string(reader.ReadChars(4)) != "RIFF") throw new InvalidDataException("not a wav");
        reader.ReadInt32();
        if (new string(reader.ReadChars(4)) != "WAVE") throw new InvalidDataException("not a wav");

        int channels = 1, rate = 44100, bits = 16;
        byte[]? data = null;
        while (stream.Position + 8 <= stream.Length)
        {
            var id = new string(reader.ReadChars(4));
            var size = reader.ReadInt32();
            if (size < 0) break;
            if (id == "fmt ")
            {
                var fmt = reader.ReadInt16();
                channels = reader.ReadInt16();
                rate = reader.ReadInt32();
                reader.ReadInt32();
                reader.ReadInt16();
                bits = reader.ReadInt16();
                var extra = size - 16;
                if (extra > 0) reader.ReadBytes(extra);
                if (fmt != 1 || bits != 16)
                    throw new InvalidDataException($"wav fmt {fmt} {bits}-bit");
            }
            else if (id == "data")
            {
                data = reader.ReadBytes(size);
            }
            else
            {
                reader.ReadBytes(size);
            }
            if ((size & 1) == 1 && stream.Position < stream.Length) reader.ReadByte();
        }

        if (data == null || channels < 1 || rate < 8000)
            throw new InvalidDataException("wav has no data");

        var frames = data.Length / (2 * channels);
        var mono = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            var sum = 0f;
            for (var c = 0; c < channels; c++)
            {
                var o = (i * channels + c) * 2;
                short s = (short)(data[o] | (data[o + 1] << 8));
                sum += s / 32768f;
            }
            mono[i] = sum / channels;
        }
        return (mono, rate);
    }

    /// <summary>
    ///     Removes DC, sets the loudness by RMS (peak-normalising made quiet, hissy clips as loud as the engine)
    ///     and, for a loop, crossfades the tail into the head so the wrap does not click.
    /// </summary>
    public static float[] Prepare(float[] mono, int rate, float targetRms, bool loop)
    {
        var mean = 0.0;
        foreach (var v in mono) mean += v;
        mean /= Math.Max(1, mono.Length);
        var buf = mono.Select(v => (float)(v - mean)).ToArray();

        if (loop)
        {
            var fade = Math.Min(buf.Length / 4, (int)(0.04f * rate));
            // New loop = original without its last `fade` samples; its head is blended with that tail.
            var n = buf.Length - fade;
            var looped = new float[n];
            Array.Copy(buf, looped, n);
            for (var i = 0; i < fade; i++)
            {
                var t = (i + 0.5f) / fade;
                var a = MathF.Sqrt(1 - t);
                var b = MathF.Sqrt(t);
                looped[i] = buf[n + i] * a + buf[i] * b;
            }
            buf = looped;
        }
        else
        {
            // Short fade at both ends.
            var f = Math.Min(buf.Length / 8, rate / 400);
            for (var i = 0; i < f; i++)
            {
                buf[i] *= i / (float)f;
                buf[^(i + 1)] *= i / (float)f;
            }
        }

        double sum = 0;
        var peak = 0f;
        foreach (var v in buf)
        {
            sum += v * v;
            peak = Math.Max(peak, MathF.Abs(v));
        }
        var rms = (float)Math.Sqrt(sum / Math.Max(1, buf.Length));
        if (rms > 1e-5f)
        {
            var k = Math.Min(targetRms / rms, 0.95f / Math.Max(peak, 1e-5f));
            for (var i = 0; i < buf.Length; i++) buf[i] *= k;
        }
        return buf;
    }

    public static (float[] Mono, int Rate)? FromResource(string fileName)
    {
        var name = "CanvasManagement.Extension.TurboKart.Audio." + fileName;
        var stream = typeof(WavClip).Assembly.GetManifestResourceStream(name);
        if (stream == null)
        {
            Console.WriteLine("[TurboKart] missing sound " + fileName);
            return null;
        }
        using (stream)
            return Load(stream);
    }
}
