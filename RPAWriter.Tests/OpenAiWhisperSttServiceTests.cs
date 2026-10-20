using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class OpenAiWhisperSttServiceTests
    {
        [Test]
        public async Task TranscribeAsync_SendsMultipartAndParsesText()
        {
            HttpMethod method = null;
            string uri = null;
            string authScheme = null;
            string authParam = null;
            string contentType = null;
            string body = null;

            var handler = new FakeHandler(async request =>
            {
                method = request.Method;
                uri = request.RequestUri.ToString();
                authScheme = request.Headers.Authorization?.Scheme;
                authParam = request.Headers.Authorization?.Parameter;
                contentType = request.Content?.Headers?.ContentType?.MediaType;
                body = request.Content != null ? await request.Content.ReadAsStringAsync() : "";
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"text\":\"привет мир\"}", Encoding.UTF8, "application/json")
                };
            });

            var service = new OpenAiWhisperSttService("https://api.openai.com/v1", "key", "whisper-1", new HttpClient(handler));
            var text = await service.TranscribeAsync(new byte[] { 1, 2, 3, 4 }, "ru", CancellationToken.None);

            Assert.That(text, Is.EqualTo("привет мир"));
            Assert.That(method, Is.EqualTo(HttpMethod.Post));
            Assert.That(uri, Is.EqualTo("https://api.openai.com/v1/audio/transcriptions"));
            Assert.That(authScheme, Is.EqualTo("Bearer"));
            Assert.That(authParam, Is.EqualTo("key"));
            Assert.That(contentType, Is.EqualTo("multipart/form-data"));
            Assert.That(body, Does.Contain("whisper-1"));
            Assert.That(body, Does.Contain("ru"));
            Assert.That(body, Does.Contain("json"));
            Assert.That(body, Does.Contain("speech.wav"));
        }

        [Test]
        public void TranscribeAsync_NonSuccess_Throws()
        {
            var handler = new FakeHandler(async request =>
                new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("{\"error\":\"bad request\"}", Encoding.UTF8, "application/json")
                });

            var service = new OpenAiWhisperSttService("https://api.openai.com/v1", "key", "whisper-1", new HttpClient(handler));

            var ex = Assert.ThrowsAsync<InvalidOperationException>(
                () => service.TranscribeAsync(new byte[] { 1, 2, 3, 4 }, "ru", CancellationToken.None));

            Assert.That(ex.Message, Does.Contain("400"));
        }

        private sealed class FakeHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _respond;

            public FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
            {
                _respond = respond;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return _respond(request);
            }
        }
    }
}
