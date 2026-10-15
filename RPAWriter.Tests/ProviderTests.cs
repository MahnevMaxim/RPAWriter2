using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    public class FakeHttpHandler : HttpMessageHandler
    {
        public HttpRequestMessage LastRequest { get; private set; }
        public string LastBody { get; private set; }
        public Func<HttpRequestMessage, string> Responder { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content != null ? await request.Content.ReadAsStringAsync() : "";
            var content = Responder?.Invoke(request) ?? "";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            };
        }
    }

    /// <summary>Simulates a "hung" provider: the request never completes until cancelled (by the user or a timeout).</summary>
    public class BlockingHttpHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    [TestFixture]
    public class ProviderTests
    {
        private static string Completion(string content)
        {
            var payload = new
            {
                choices = new[] { new { message = new { content = content } } }
            };
            return JsonSerializer.Serialize(payload);
        }

        [Test]
        public async Task AnalyzeSpecAsync_SendsRequestToChatCompletionsAndParses()
        {
            var handler = new FakeHttpHandler
            {
                Responder = req => Completion(
                    "{\"summary\":\"s\",\"tasks\":[{\"action\":\"open\",\"target\":\"site\",\"activityType\":\"OpenBrowser\"}]}")
            };
            var provider = new OpenAiCompatibleProvider(new OpenAiOptions
            {
                BaseUrl = "https://example.com/v1",
                ApiKey = "sk-test",
                Model = "test-model"
            }, new HttpClient(handler));

            var analysis = await provider.AnalyzeSpecAsync(new SpecModel { RawText = "Open site" });

            Assert.That(handler.LastRequest.RequestUri.ToString(), Is.EqualTo("https://example.com/v1/chat/completions"));
            Assert.That(handler.LastRequest.Method, Is.EqualTo(HttpMethod.Post));
            Assert.That(handler.LastRequest.Headers.Authorization.Scheme, Is.EqualTo("Bearer"));
            Assert.That(handler.LastRequest.Headers.Authorization.Parameter, Is.EqualTo("sk-test"));
            Assert.That(handler.LastBody, Does.Contain("test-model"));

            Assert.That(analysis.Summary, Is.EqualTo("s"));
            Assert.That(analysis.Tasks.Count, Is.EqualTo(1));
            Assert.That(analysis.Tasks[0].ActivityType, Is.EqualTo("OpenBrowser"));
        }

        [Test]
        public async Task GenerateRobotAsync_ParsesSteps()
        {
            var handler = new FakeHttpHandler
            {
                Responder = req => Completion(
                    "{\"name\":\"Robot\",\"steps\":[" +
                    "{\"kind\":\"InvokeMethod\",\"className\":\"RpaActions\",\"methodName\":\"OpenBrowser\"}," +
                    "{\"kind\":\"Assign\",\"variable\":\"url\",\"expression\":\"http://x\"}]}")
            };
            var provider = new OpenAiCompatibleProvider(new OpenAiOptions(), new HttpClient(handler));

            var plan = await provider.GenerateRobotAsync(new SpecAnalysis());

            Assert.That(plan.Name, Is.EqualTo("Robot"));
            Assert.That(plan.Root.Children.Count, Is.EqualTo(2));
            Assert.That(plan.Root.Children[0].MethodName, Is.EqualTo("OpenBrowser"));
            Assert.That(plan.Root.Children[1].Kind, Is.EqualTo(ActivityNodeKind.Assign));
        }

        [Test]
        public async Task StubAiProvider_DecomposeTasks_CreatesTasks()
        {
            var provider = new StubAiProvider();
            var tasks = await provider.DecomposeTasksAsync("Open browser. Read table from file.");

            Assert.That(tasks.Count, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public async Task StubAiProvider_ChatAsync_ReturnsText()
        {
            var reply = await new StubAiProvider().ChatAsync("hello");
            Assert.That(reply.Answer, Does.Contain("hello"));
            Assert.That(reply.Reasoning, Is.Empty);
        }

        [Test]
        public async Task OpenAiProvider_ChatAsync_ReturnsContent()
        {
            var handler = new FakeHttpHandler { Responder = req => Completion("Hello, I'm the agent!") };
            var provider = new OpenAiCompatibleProvider(new OpenAiOptions(), new HttpClient(handler));

            var reply = await provider.ChatAsync("hello");

            Assert.That(reply.Answer, Is.EqualTo("Hello, I'm the agent!"));
            Assert.That(reply.Reasoning, Is.Null.Or.Empty);
        }

        [Test]
        public async Task OpenAiProvider_ChatAsync_ParsesReasoning()
        {
            var payload = new
            {
                choices = new[]
                {
                    new
                    {
                        message = new
                        {
                            content = "Answer",
                            reasoning_content = "Reasoning"
                        }
                    }
                }
            };
            var handler = new FakeHttpHandler { Responder = req => JsonSerializer.Serialize(payload) };
            var provider = new OpenAiCompatibleProvider(new OpenAiOptions(), new HttpClient(handler));

            var reply = await provider.ChatAsync("query");

            Assert.That(reply.Answer, Is.EqualTo("Answer"));
            Assert.That(reply.Reasoning, Is.EqualTo("Reasoning"));
        }

        [Test]
        public void OpenAiProvider_ChatAsync_CancelsWhenTokenIsCancelled()
        {
            var handler = new BlockingHttpHandler();
            var provider = new OpenAiCompatibleProvider(new OpenAiOptions(), new HttpClient(handler));
            using var cts = new CancellationTokenSource();

            var pending = provider.ChatAsync("hello", cts.Token);
            cts.Cancel();

            Assert.ThrowsAsync(Is.InstanceOf<OperationCanceledException>(), async () => await pending);
        }

        [Test]
        public void OpenAiProvider_ChatAsync_ThrowsTimeoutWhenProviderHangs()
        {
            var handler = new BlockingHttpHandler();
            var provider = new OpenAiCompatibleProvider(new OpenAiOptions { TimeoutSeconds = 1 }, new HttpClient(handler));

            Assert.ThrowsAsync<TimeoutException>(async () => await provider.ChatAsync("hello"));
        }

        [Test]
        public async Task AnalyzeSpecAsync_IncludesRulesFromFolder()
        {
            var dir = Path.Combine(Path.GetTempPath(), "rpawriter_rules_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "rules.md"), "Rule: use InvokeMethod for all actions.");
                var handler = new FakeHttpHandler
                {
                    Responder = req => Completion("{\"summary\":\"s\",\"tasks\":[]}")
                };
                var provider = new OpenAiCompatibleProvider(new OpenAiOptions
                {
                    BaseUrl = "https://example.com/v1",
                    ApiKey = "sk-test",
                    Model = "test-model",
                    RulesPath = dir
                }, new HttpClient(handler));

                await provider.AnalyzeSpecAsync(new SpecModel { RawText = "Open site" });

                Assert.That(handler.LastBody, Does.Contain("Rule: use InvokeMethod"));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Test]
        public async Task DecomposeTasksAsync_IncludesRulesFromFolder()
        {
            var dir = Path.Combine(Path.GetTempPath(), "rpawriter_rules_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "rules.md"), "Rule: one task — one action.");
                var handler = new FakeHttpHandler { Responder = req => Completion("[{\"action\":\"open\"}]") };
                var provider = new OpenAiCompatibleProvider(new OpenAiOptions { RulesPath = dir }, new HttpClient(handler));

                await provider.DecomposeTasksAsync("Open browser");

                Assert.That(handler.LastBody, Does.Contain("Rule: one task — one action."));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
