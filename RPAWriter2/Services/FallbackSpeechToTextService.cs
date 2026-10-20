using System;
using System.Threading;
using System.Threading.Tasks;
using RPAWriter.Core.Contracts;

namespace RPAWriter2.Services
{
    /// <summary>
    /// Tries the primary service first and, when it fails, falls back to the
    /// secondary service (e.g. offline Vosk when the Whisper endpoint is unreachable).
    /// </summary>
    public sealed class FallbackSpeechToTextService : ISpeechToTextService
    {
        private readonly ISpeechToTextService _primary;
        private readonly ISpeechToTextService _fallback;

        public FallbackSpeechToTextService(ISpeechToTextService primary, ISpeechToTextService fallback)
        {
            _primary = primary ?? throw new ArgumentNullException(nameof(primary));
            _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
        }

        public async Task<string> TranscribeAsync(byte[] pcmAudio, string language, CancellationToken cancellationToken = default)
        {
            Exception primaryError = null;
            try
            {
                return await _primary.TranscribeAsync(pcmAudio, language, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                primaryError = ex;
            }

            try
            {
                return await _fallback.TranscribeAsync(pcmAudio, language, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Speech recognition failed with both providers. Primary error: " + primaryError?.Message +
                    " Fallback error: " + ex.Message, ex);
            }
        }
    }
}
