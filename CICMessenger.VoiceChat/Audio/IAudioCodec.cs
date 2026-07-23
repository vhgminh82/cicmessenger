using System;

namespace CICMessenger.VoiceChat.Audio;

public interface IAudioCodec : IDisposable
{
    int SampleRate { get; }
    int Channels { get; }
    int BitsPerSample { get; }
    byte[] Encode(byte[] data, int offset, int length);
    byte[] Decode(byte[] data, int offset, int length);
}
