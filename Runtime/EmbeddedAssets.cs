using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace TankMod.Runtime;

/// <summary>Reads files that are compiled into the plugin DLL (everything under the project's Assets folder).</summary>
internal static class EmbeddedAssets
{
    private static readonly Assembly Assembly = typeof(EmbeddedAssets).Assembly;
    private static readonly string[] Names = Assembly.GetManifestResourceNames();

    /// <param name="path">Path relative to the Assets folder, e.g. "piwo/piwo.obj".</param>
    public static byte[] ReadBytes(string path)
    {
        using var stream = Open(path);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    public static string ReadText(string path)
    {
        using var stream = Open(path);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static Texture2D LoadTexture(string path, bool linear = false)
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear) { name = Path.GetFileNameWithoutExtension(path) };
        if (!ImageConversion.LoadImage(tex, ReadBytes(path)))
        {
            throw new InvalidDataException($"Could not decode texture '{path}'.");
        }

        tex.wrapMode = TextureWrapMode.Repeat;
        return tex;
    }

    private static Stream Open(string path)
    {
        var wanted = Normalize(path);
        var name = Names.FirstOrDefault(n => Normalize(n).EndsWith(wanted, StringComparison.OrdinalIgnoreCase));
        if (name == null)
        {
            throw new FileNotFoundException($"Embedded asset '{path}' not found. Available: {string.Join(", ", Names)}");
        }

        return Assembly.GetManifestResourceStream(name)!;
    }

    
    public static AudioClip LoadWavPcm16(string path)
    {
        byte[] bytes = ReadBytes(path);

        int channels = 0;
        int sampleRate = 0;
        int bitsPerSample = 0;
        int audioFormat = 0;

        int dataOffset = -1;
        int dataLength = 0;

        using (var stream = new MemoryStream(bytes))
        using (var reader = new BinaryReader(stream))
        {
            if (reader.ReadUInt32() != 0x46464952) // RIFF
                throw new InvalidDataException("Invalid WAV file.");

            reader.ReadUInt32();

            if (reader.ReadUInt32() != 0x45564157) // WAVE
                throw new InvalidDataException("Invalid WAV file.");

            while (stream.Position + 8 <= stream.Length)
            {
                string chunk = new string(reader.ReadChars(4));
                int length = reader.ReadInt32();
                long nextChunk = stream.Position + length;

                if (chunk == "fmt ")
                {
                    audioFormat = reader.ReadUInt16();
                    channels = reader.ReadUInt16();
                    sampleRate = reader.ReadInt32();

                    reader.ReadInt32();
                    reader.ReadUInt16();

                    bitsPerSample = reader.ReadUInt16();
                }
                else if (chunk == "data")
                {
                    dataOffset = (int)stream.Position;
                    dataLength = length;
                }

                stream.Position = nextChunk + (length & 1);

                if (stream.Position > stream.Length)
                    break;
            }
        }

        if (audioFormat != 1 || bitsPerSample != 16)
            throw new InvalidDataException(
                $"Expected 16-bit PCM WAV. Format: {audioFormat}, Bit depth: {bitsPerSample}");

        if (dataOffset < 0 || channels <= 0 || sampleRate <= 0)
            throw new InvalidDataException("WAV file is missing audio data.");

        int sampleCount = dataLength / 2;
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            short sample = BitConverter.ToInt16(bytes, dataOffset + i * 2);
            samples[i] = sample / 32768f;
        }

        int frames = sampleCount / channels;

        AudioClip clip = AudioClip.Create(
            Path.GetFileNameWithoutExtension(path),
            frames,
            channels,
            sampleRate,
            false
        );

        clip.SetData(samples, 0);
        clip.hideFlags = HideFlags.DontUnloadUnusedAsset;

        return clip;
    }


    private static string Normalize(string s) => s.Replace('\\', '/');
}
