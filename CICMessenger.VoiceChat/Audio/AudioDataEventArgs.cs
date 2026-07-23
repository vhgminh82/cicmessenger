using System;

namespace CICMessenger.VoiceChat.Audio;

public class AudioDataEventArgs : EventArgs
{
    public byte[] Buffer { get; }
    public int BytesRecorded { get; }

    public AudioDataEventArgs(byte[] buffer, int bytesRecorded)
    {
        Buffer = buffer;
        BytesRecorded = bytesRecorded;
    }
}
