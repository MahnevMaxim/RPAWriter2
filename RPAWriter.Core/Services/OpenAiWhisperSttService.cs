using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RPAWriter.Core.Contracts;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// OpenAI Whisper-compatible speech-to-text via <c>POST {BaseUrl}/audio/transcriptions</c>.
    /// Reuses the same BaseUrl/API key as the chat provider, so no extra configuration is
    /// required when the backend exposes the audio transcriptions endpoint.
    /// </summary>
    public sealed class OpenAiWhisperSttService : ISpeechToTextService
    {
        private const int SampleRate = 16000;
        private const int BitsPerSample = 16;
        private const int Channels = 1;

        private readonly HttpClient _http;
        private readonly string _baseUrl;
        private readonly string _apiKey;
        private readonly string _model;
        private readonly Action<string> _log;

        public OpenAiWhisperSttService(string baseUrl, string apiKey, string model)
            : this(baseUrl, apiKey, model, new HttpClient(), null)
        {
        }

        public OpenAiWhisperSttService(string baseUrl, string apiKey, string model, HttpClient http)
            : this(baseUrl, apiKey, model, http, null)
        {
        }

        public OpenAiWhisperSttService(string baseUrl, string apiKey, string model, Action<string> log)
            : this(baseUrl, apiKey, model, new HttpClient(), log)
        {
        }

        public OpenAiWhisperSttService(string baseUrl, string apiKey, string model, HttpClient http, Action<string> log)
        {
            _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? "https://api.openai.com/v1" : baseUrl.Trim();
            _apiKey = apiKey ?? "";
            _model = string.IsNullOrWhiteSpace(model) ? "whisper-1" : model.Trim();
            _log = log;
            _http = http ?? new HttpClient();
            _http.Timeout = TimeSpan.FromSeconds(300);
        }

        private void Log(string message)
        {
            try
            {
                _log?.Invoke("[WhisperSTT] " + message);
            }
            catch
            {
                // Logging must never break speech recognition.
            }
        }

        public async Task<string> TranscribeAsync(byte[] pcmAudio, string language, CancellationToken cancellationToken = default)
        {
            if (pcmAudio == null || pcmAudio.Length == 0)
            {
                Log("No audio data supplied.");
                throw new ArgumentException("No audio data to transcribe.", nameof(pcmAudio));
            }

            var wav = PcmToWav(pcmAudio);
            var url = _baseUrl.TrimEnd('/') + "/audio/transcriptions";

            Log("Request: POST " + url + " | model=" + _model + " | language=" + (language ?? "") +
                " | pcmBytes=" + pcmAudio.Length + " | wavBytes=" + wav.Length +
                " | hasApiKey=" + !string.IsNullOrWhiteSpace(_apiKey));

            using (var content = new MultipartFormDataContent())
            {
                var fileContent = new ByteArrayContent(wav);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
                content.Add(fileContent, "file", "speech.wav");
                content.Add(new StringContent(_model), "model");
                if (!string.IsNullOrWhiteSpace(language))
                    content.Add(new StringContent(language), "language");
                content.Add(new StringContent("json"), "response_format");

                using (var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content })
                {
                    if (!string.IsNullOrWhiteSpace(_apiKey))
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                    else
                        Log("No API key configured — the request will be sent without Authorization.");

                    HttpResponseMessage response = null;
                    try
                    {
                        response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Log("SendAsync failed: " + ex);
                        throw;
                    }

                    using (response)
                    {
                        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        Log("Response: status=" + (int)response.StatusCode + " " + response.ReasonPhrase +
                            " | body=" + Truncate(body, 2000));

                        if (!response.IsSuccessStatusCode)
                            throw new InvalidOperationException(
                                "Speech recognition failed (" + (int)response.StatusCode + " " + response.ReasonPhrase + "): " + Truncate(body, 500));

                        var obj = JObject.Parse(body);
                        var text = obj["text"]?.Value<string>()?.Trim();
                        if (string.IsNullOrWhiteSpace(text))
                        {
                            Log("Response JSON contained no 'text' field: " + Truncate(body, 2000));
                            throw new InvalidOperationException("Speech recognition returned no text.");
                        }

                        Log("Recognized text: " + Truncate(text, 500));
                        return text;
                    }
                }
            }
        }

        /// <summary>Wraps raw 16-bit mono PCM into a minimal 44-byte WAV container.</summary>
        internal static byte[] PcmToWav(byte[] pcm)
        {
            var byteRate = SampleRate * Channels * BitsPerSample / 8;
            var blockAlign = Channels * BitsPerSample / 8;

            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + pcm.Length);
                writer.Write(Encoding.ASCII.GetBytes("WAVE"));
                writer.Write(Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16);
                writer.Write((short)1); // PCM
                writer.Write((short)Channels);
                writer.Write(SampleRate);
                writer.Write(byteRate);
                writer.Write((short)blockAlign);
                writer.Write((short)BitsPerSample);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(pcm.Length);
                writer.Write(pcm);
                writer.Flush();
                return ms.ToArray();
            }
        }

        private static string Truncate(string s, int max)
        {
            if (s == null) return "";
            return s.Length <= max ? s : s.Substring(0, max);
        }
    }
}
