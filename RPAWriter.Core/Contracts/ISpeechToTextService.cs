using System.Threading;
using System.Threading.Tasks;

namespace RPAWriter.Core.Contracts
{
    /// <summary>
    /// Transcribes raw speech audio (16 kHz, 16-bit, mono PCM) into text.
    /// Implementations may be cloud-based (OpenAI Whisper) or offline (Vosk).
    /// </summary>
    public interface ISpeechToTextService
    {
        /// <summary>
        /// Transcribes the supplied PCM audio and returns the recognized text.
        /// Throws on failure; callers are responsible for error handling and fallback.
        /// </summary>
        Task<string> TranscribeAsync(byte[] pcmAudio, string language, CancellationToken cancellationToken = default);
    }
}
