namespace CanvasManagement.Extension.TurboKart;

/// <summary>Reads a 16-bit PCM WAV into a mono float buffer. Peak-normalized so mixed sources sit together.</summary>
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
        var peak = 0f;
        for (var i = 0; i < frames; i++)
        {
            var sum = 0f;
            for (var c = 0; c < channels; c++)
            {
                var o = (i * channels + c) * 2;
                short s = (short)(data[o] | (data[o + 1] << 8));
                sum += s / 32768f;
            }
            var v = sum / channels;
            mono[i] = v;
            var a = MathF.Abs(v);
            if (a > peak) peak = a;
        }

        if (peak > 0.01f)
        {
            var scale = 0.9f / peak;
            for (var i = 0; i < mono.Length; i++) mono[i] *= scale;
        }

        return (mono, rate);
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
