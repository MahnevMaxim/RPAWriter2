using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RPAWriter.Core.Contracts;
using Vosk;

namespace RPAWriter2.Services
{
    /// <summary>
    /// Offline speech recognition using Vosk. Requires a Vosk model directory
    /// (e.g. vosk-model-small-ru-0.22) and the native libvosk binaries that ship
    /// with the Vosk NuGet package.
    /// </summary>
    public sealed class VoskSttService : ISpeechToTextService
    {
        private readonly string _modelPath;

        public VoskSttService(string modelPath)
        {
            _modelPath = modelPath ?? "";
        }

        public Task<string> TranscribeAsync(byte[] pcmAudio, string language, CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                if (string.IsNullOrWhiteSpace(_modelPath) || !Directory.Exists(_modelPath))
                    throw new InvalidOperationException("Vosk model directory is not configured or does not exist: " + _modelPath);

                if (pcmAudio == null || pcmAudio.Length == 0)
                    throw new ArgumentException("No audio data to transcribe.", nameof(pcmAudio));

                using (var model = new Model(_modelPath))
                using (var recognizer = new VoskRecognizer(model, 16000.0f))
                {
                    recognizer.SetMaxAlternatives(0);
                    recognizer.SetWords(false);

                    string partial;
                    if (recognizer.AcceptWaveform(pcmAudio, pcmAudio.Length))
                        partial = ExtractText(recognizer.Result());
                    else
                        partial = ExtractText(recognizer.PartialResult());

                    var final = ExtractText(recognizer.FinalResult());
                    var text = !string.IsNullOrWhiteSpace(final) ? final : partial;
                    if (string.IsNullOrWhiteSpace(text))
                        throw new InvalidOperationException("Vosk returned no recognized text.");

                    return text.Trim();
                }
            }, cancellationToken);
        }

        private static string ExtractText(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return "";
            var obj = JObject.Parse(json);
            return obj["text"]?.Value<string>() ?? "";
        }
    }
}
