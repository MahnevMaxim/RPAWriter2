using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    public class FakeHttpHandler : HttpMessageHandler
    {
        public HttpRequestMessage LastRequest { get; private set; }
        public string LastBody { get; private set; }
        public Func<HttpRequestMessage, string> Responder { get; set; }
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content != null ? await request.Content.ReadAsStringAsync() : "";
            var content = Responder?.Invoke(request) ?? "";
            return new HttpResponseMessage(StatusCode)
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

    /// <summary>Returns a response per call count, used to simulate transient provider failures.</summary>
    public class RetryingHttpHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public Func<int, HttpResponseMessage> Responder { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(Responder(CallCount));
        }
    }

    /// <summary>Collects progress reports synchronously, so tests can assert on them deterministically.</summary>
    public class SyncProgress<T> : IProgress<T>
    {
        public List<T> Reported { get; } = new List<T>();

        public void Report(T value) => Reported.Add(value);
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
            var provider = new OpenAiCompatibleProvider(new OpenAiOptions
            {
                TimeoutSeconds = 1,
                RetryTimeout = TimeSpan.FromMilliseconds(200),
                RetryBaseDelay = TimeSpan.FromMilliseconds(10),
                RetryMaxDelay = TimeSpan.FromMilliseconds(10)
            }, new HttpClient(handler));

            var ex = Assert.ThrowsAsync<ProviderErrorException>(async () => await provider.ChatAsync("hello"));
            Assert.That(ex.Message, Does.Contain("no response"));
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

        [Test]
        public async Task DecomposeTasksAsync_ParsesSapTransactionFields()
        {
            var handler = new FakeHttpHandler
            {
                Responder = req => Completion(
                    "[{\"action\":\"настроить\",\"target\":\"VA01\",\"sapTransactionCode\":\"VA01\",\"sapActionKind\":\"setup\"}," +
                    "{\"action\":\"получить таблицу\",\"target\":\"SE16N\",\"sapTransactionCode\":\"SE16N\",\"sapActionKind\":\"gettable\"}]")
            };
            var provider = new OpenAiCompatibleProvider(new OpenAiOptions(), new HttpClient(handler));

            var tasks = await provider.DecomposeTasksAsync("SAP VA01");

            Assert.That(tasks.Count, Is.EqualTo(2));
            Assert.That(tasks[0].SapTransactionCode, Is.EqualTo("VA01"));
            Assert.That(tasks[0].SapActionKind, Is.EqualTo(SapActionKind.Setup));
            Assert.That(tasks[1].SapTransactionCode, Is.EqualTo("SE16N"));
            Assert.That(tasks[1].SapActionKind, Is.EqualTo(SapActionKind.GetTable));
        }

        [Test]
        public async Task AnalyzeSpecAsync_ParsesSapTransactionFields()
        {
            var handler = new FakeHttpHandler
            {
                Responder = req => Completion(
                    "{\"summary\":\"s\",\"tasks\":[{\"action\":\"настроить\",\"target\":\"VA01\",\"sapTransactionCode\":\"VA01\",\"sapActionKind\":\"setup\"}]}")
            };
            var provider = new OpenAiCompatibleProvider(new OpenAiOptions(), new HttpClient(handler));

            var analysis = await provider.AnalyzeSpecAsync(new SpecModel { RawText = "SAP VA01" });

            Assert.That(analysis.Tasks.Count, Is.EqualTo(1));
            Assert.That(analysis.Tasks[0].SapTransactionCode, Is.EqualTo("VA01"));
            Assert.That(analysis.Tasks[0].SapActionKind, Is.EqualTo(SapActionKind.Setup));
        }

        [Test]
        public async Task ReconstructSpecAsync_IncludesPlatformRulesFromSubfolder()
        {
            var dir = Path.Combine(Path.GetTempPath(), "rpawriter_rules_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(dir, "mwf"));
            try
            {
                File.WriteAllText(Path.Combine(dir, "mwf", "rules.md"), "Platform rule: analyze main.xaml first.");
                var handler = new FakeHttpHandler
                {
                    Responder = req => Completion("{\"name\":\"R\",\"description\":\"d\",\"steps\":[],\"inputs\":[],\"outputs\":[]}")
                };
                var provider = new OpenAiCompatibleProvider(new OpenAiOptions { RulesPath = dir, ApiKey = "sk-test" }, new HttpClient(handler));

                await provider.ReconstructSpecAsync("RPA-0001", Array.Empty<RobotSourceFile>(), platform: RobotPlatform.Mwf);

                Assert.That(handler.LastBody, Does.Contain("Platform rule: analyze main.xaml first."));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Test]
        public void ChatAsync_IncludesResponseBodyWhenProviderReturnsError()
        {
            var handler = new FakeHttpHandler
            {
                StatusCode = HttpStatusCode.BadRequest,
                Responder = req => "{\"error\":{\"message\":\"context length exceeded\"}}"
            };
            var provider = new OpenAiCompatibleProvider(new OpenAiOptions
            {
                ApiKey = "sk-test",
                RetryTimeout = TimeSpan.FromMilliseconds(200),
                RetryBaseDelay = TimeSpan.FromMilliseconds(10),
                RetryMaxDelay = TimeSpan.FromMilliseconds(10)
            }, new HttpClient(handler));

            var ex = Assert.ThrowsAsync<ProviderErrorException>(async () => await provider.ChatAsync("hi"));

            Assert.That(ex.Kind, Is.EqualTo(ProviderErrorKind.ContextWindowExceeded));
            Assert.That(ex.Message, Does.Contain("400"));
            Assert.That(ex.Message, Does.Contain("context length exceeded"));
        }

        [Test]
        public void OpenAiOptions_DefaultContextWindowIs409600()
        {
            Assert.That(new OpenAiOptions().ContextWindowSize, Is.EqualTo(409600));
            var provider = new OpenAiCompatibleProvider(new OpenAiOptions(), new HttpClient(new FakeHttpHandler()));
            Assert.That(provider.ContextWindowSize, Is.EqualTo(409600));
        }

        [Test]
        public async Task SendAsync_RetriesNonSuccessThenSucceeds()
        {
            var handler = new RetryingHttpHandler
            {
                Responder = n => n == 1
                    ? new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("<html>502</html>") }
                    : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Completion("ok")) }
            };
            var provider = new OpenAiCompatibleProvider(new OpenAiOptions
            {
                ApiKey = "sk-test",
                RetryBaseDelay = TimeSpan.Zero,
                RetryMaxDelay = TimeSpan.Zero
            }, new HttpClient(handler));

            var reply = await provider.ChatAsync("hi");

            Assert.That(reply.Answer, Is.EqualTo("ok"));
            Assert.That(handler.CallCount, Is.EqualTo(2));
        }

        [Test]
        public void SendAsync_ExhaustsBudgetAndThrowsWithStatusAndBody()
        {
            var handler = new RetryingHttpHandler
            {
                Responder = n => new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("{\"error\":{\"message\":\"bad key\"}}")
                }
            };
            var provider = new OpenAiCompatibleProvider(new OpenAiOptions
            {
                ApiKey = "sk-test",
                RetryTimeout = TimeSpan.FromMilliseconds(150),
                RetryBaseDelay = TimeSpan.FromMilliseconds(10),
                RetryMaxDelay = TimeSpan.FromMilliseconds(10)
            }, new HttpClient(handler));

            var ex = Assert.ThrowsAsync<ProviderErrorException>(async () => await provider.ChatAsync("hi"));

            Assert.That(ex.Message, Does.Contain("400"));
            Assert.That(ex.Message, Does.Contain("bad key"));
            Assert.That(ex.Message, Does.Contain("attempt(s)"));
            Assert.That(handler.CallCount, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public async Task SendAsync_RaisesRetryAttemptWithAttemptCount()
        {
            var handler = new RetryingHttpHandler
            {
                Responder = n => n <= 2
                    ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("busy") }
                    : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Completion("done")) }
            };
            var provider = new OpenAiCompatibleProvider(new OpenAiOptions
            {
                ApiKey = "sk-test",
                RetryBaseDelay = TimeSpan.Zero,
                RetryMaxDelay = TimeSpan.Zero
            }, new HttpClient(handler));

            var attempts = new List<RetryAttemptEventArgs>();
            provider.RetryAttempt += (s, e) => attempts.Add(e);

            await provider.ChatAsync("hi");

            Assert.That(attempts.Count, Is.EqualTo(2));
            Assert.That(attempts[0].Attempt, Is.EqualTo(1));
            Assert.That(attempts[0].StatusCode, Is.EqualTo(503));
            Assert.That(attempts[0].Body, Does.Contain("busy"));
            Assert.That(attempts[1].Attempt, Is.EqualTo(2));
        }

        [Test]
        public void SendAsync_CancelsDuringBackoff()
        {
            var handler = new RetryingHttpHandler
            {
                Responder = n => new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("down") }
            };
            var provider = new OpenAiCompatibleProvider(new OpenAiOptions
            {
                ApiKey = "sk-test",
                RetryTimeout = TimeSpan.FromMinutes(30),
                RetryBaseDelay = TimeSpan.FromHours(1),
                RetryMaxDelay = TimeSpan.FromHours(1)
            }, new HttpClient(handler));

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

            Assert.ThrowsAsync(Is.InstanceOf<OperationCanceledException>(), async () => await provider.ChatAsync("hi", cts.Token));
        }

        [Test]
        public async Task AnalyzeSpecAsync_LoadsPromptFromPromptsPath()
        {
            var dir = Path.Combine(Path.GetTempPath(), "rpawriter_prompts_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(dir, "Tasks"));
            try
            {
                File.WriteAllText(Path.Combine(dir, "Tasks", "analyze-spec.txt"), "CUSTOM PROMPT with spec: {spec}");
                var handler = new FakeHttpHandler
                {
                    Responder = req => Completion("{\"summary\":\"s\",\"tasks\":[]}")
                };
                var provider = new OpenAiCompatibleProvider(new OpenAiOptions
                {
                    PromptsPath = dir,
                    ApiKey = "sk-test"
                }, new HttpClient(handler));

                await provider.AnalyzeSpecAsync(new SpecModel { RawText = "Open site" });

                Assert.That(handler.LastBody, Does.Contain("CUSTOM PROMPT"));
                Assert.That(handler.LastBody, Does.Contain("Open site"));
                Assert.That(handler.LastBody, Does.Not.Contain("{spec}"));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Test]
        public async Task ReloadPromptsFromDisk_PicksUpEditedChatSystemPrompt()
        {
            var dir = Path.Combine(Path.GetTempPath(), "rpawriter_prompts_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(dir, "System"));
            try
            {
                var chatFile = Path.Combine(dir, "System", "chat-system.txt");
                File.WriteAllText(chatFile, "CHAT V1");
                var handler = new FakeHttpHandler { Responder = req => Completion("ok") };
                var provider = new OpenAiCompatibleProvider(new OpenAiOptions { PromptsPath = dir, ApiKey = "sk-test" }, new HttpClient(handler));

                await provider.ChatAsync("hi");
                Assert.That(handler.LastBody, Does.Contain("CHAT V1"));

                File.WriteAllText(chatFile, "CHAT V2");
                provider.ReloadPromptsFromDisk();

                await provider.ChatAsync("hi");
                Assert.That(handler.LastBody, Does.Contain("CHAT V2"));
                Assert.That(handler.LastBody, Does.Not.Contain("CHAT V1"));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Test]
        public async Task MapTaskToSolutionAsync_ToleratesInvalidEscapeInCSharpCode()
        {
            // The model may emit a raw "\s" (from a C# verbatim string or regex pattern)
            // without escaping the backslash, which is invalid JSON.
            var content = "{\"xamlFragment\":\"<Activity/>\",\"csharpCode\":\"var p = Regex.Match(s, @\\\"\\s+\\\");\",\"solutionPaths\":[\"Main.xaml\"]}";
            var handler = new FakeHttpHandler { Responder = req => Completion(content) };
            var provider = new OpenAiCompatibleProvider(new OpenAiOptions(), new HttpClient(handler));

            var record = await provider.MapTaskToSolutionAsync(
                new TaskDescriptor { Action = "match", Description = "Match whitespace" },
                new List<RobotSourceFile>());

            Assert.That(record.CSharpCode, Does.Contain(@"\s"));
            Assert.That(record.SolutionPaths, Does.Contain("Main.xaml"));
        }

        [Test]
        public async Task ChatStreamingAsync_StreamsReasoningAndContentDeltas()
        {
            var sse =
                "data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"step \"}}]}\n\n" +
                "data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"two\"}}]}\n\n" +
                "data: {\"choices\":[{\"delta\":{\"content\":\"{\\\"action\\\":\\\"final\\\",\\\"text\\\":\\\"done\\\"}\"}}]}\n\n" +
                "data: {\"choices\":[],\"usage\":{\"prompt_tokens\":100,\"completion_tokens\":50,\"total_tokens\":150}}\n\n" +
                "data: [DONE]\n\n";

            var handler = new FakeHttpHandler { Responder = req => sse };
            var provider = new OpenAiCompatibleProvider(new OpenAiOptions { ApiKey = "sk-test", Model = "test-model" }, new HttpClient(handler));

            var progress = new SyncProgress<ChatStreamDelta>();

            var response = await provider.ChatStreamingAsync(
                new List<ChatMessageData> { new ChatMessageData { Role = "user", Content = "hi" } },
                "system",
                progress);

            var deltas = progress.Reported.Select(d => d.ReasoningDelta ?? d.ContentDelta).ToList();

            Assert.That(response.Reasoning, Is.EqualTo("step two"));
            Assert.That(response.Answer, Is.EqualTo("{\"action\":\"final\",\"text\":\"done\"}"));
            Assert.That(deltas, Does.Contain("step "));
            Assert.That(deltas, Does.Contain("two"));
            Assert.That(handler.LastBody, Does.Contain("\"stream\":true"));
            Assert.That(handler.LastBody, Does.Contain("\"stream_options\":{\"include_usage\":true}"));
            Assert.That(provider.TotalTokenUsage.TotalTokens, Is.EqualTo(150));
        }

        [Test]
        public async Task ChatAsync_NonStreaming_DoesNotSetStreamOptions_AndRecordsUsage()
        {
            var handler = new FakeHttpHandler
            {
                Responder = req => "{\"choices\":[{\"message\":{\"content\":\"hello\"}}],\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":5,\"total_tokens\":15}}"
            };
            var provider = new OpenAiCompatibleProvider(new OpenAiOptions { ApiKey = "sk-test", Model = "test-model" }, new HttpClient(handler));

            var response = await provider.ChatAsync(
                new List<ChatMessageData> { new ChatMessageData { Role = "user", Content = "hi" } },
                "system");

            Assert.That(response.Answer, Is.EqualTo("hello"));
            Assert.That(handler.LastBody, Does.Not.Contain("\"stream\""));
            Assert.That(handler.LastBody, Does.Not.Contain("stream_options"));
            Assert.That(provider.TotalTokenUsage.TotalTokens, Is.EqualTo(15));
        }

        [Test]
        public async Task ChatStreamingAsync_EmptyChoicesChunk_DoesNotThrow()
        {
            var sse =
                "data: {\"choices\":[{\"delta\":{\"content\":\"hi\"}}]}\n\n" +
                "data: {\"choices\":[]}\n\n" +
                "data: [DONE]\n\n";

            var handler = new FakeHttpHandler { Responder = req => sse };
            var provider = new OpenAiCompatibleProvider(new OpenAiOptions { ApiKey = "sk-test", Model = "test-model" }, new HttpClient(handler));

            var response = await provider.ChatStreamingAsync(
                new List<ChatMessageData> { new ChatMessageData { Role = "user", Content = "hi" } },
                "system",
                new SyncProgress<ChatStreamDelta>());

            Assert.That(response.Answer, Is.EqualTo("hi"));
            Assert.That(provider.TotalTokenUsage.TotalTokens, Is.EqualTo(0));
        }

        [Test]
        public async Task ChatStreamingAsync_AccumulatesUsageAcrossRequests()
        {
            string Stream(int total) =>
                "data: {\"choices\":[{\"delta\":{\"content\":\"a\"}}]}\n\n" +
                "data: {\"choices\":[],\"usage\":{\"prompt_tokens\":" + total + ",\"completion_tokens\":0,\"total_tokens\":" + total + "}}\n\n" +
                "data: [DONE]\n\n";

            var handler = new FakeHttpHandler { Responder = req => Stream(100) };
            var provider = new OpenAiCompatibleProvider(new OpenAiOptions { ApiKey = "sk-test", Model = "test-model" }, new HttpClient(handler));

            await provider.ChatStreamingAsync(
                new List<ChatMessageData> { new ChatMessageData { Role = "user", Content = "hi" } },
                "system",
                new SyncProgress<ChatStreamDelta>());

            handler.Responder = req => Stream(50);
            await provider.ChatStreamingAsync(
                new List<ChatMessageData> { new ChatMessageData { Role = "user", Content = "hi" } },
                "system",
                new SyncProgress<ChatStreamDelta>());

            Assert.That(provider.TotalTokenUsage.TotalTokens, Is.EqualTo(150));
        }

        [Test]
        public async Task ChatAsync_RecordsDeepSeekCacheTokens_AndEstimatesCost()
        {
            // DeepSeek reports prompt_cache_hit_tokens / prompt_cache_miss_tokens.
            var handler = new FakeHttpHandler
            {
                Responder = req =>
                    "{\"choices\":[{\"message\":{\"content\":\"hi\"}}]," +
                    "\"usage\":{\"prompt_tokens\":11,\"completion_tokens\":4,\"total_tokens\":15," +
                    "\"prompt_cache_hit_tokens\":3,\"prompt_cache_miss_tokens\":8}}"
            };
            var provider = new OpenAiCompatibleProvider(
                new OpenAiOptions
                {
                    ApiKey = "sk-test",
                    Model = "test-model",
                    InputPricePerMillion = 0.27,
                    CacheHitPricePerMillion = 0.07,
                    OutputPricePerMillion = 1.10
                },
                new HttpClient(handler));

            await provider.ChatAsync(
                new List<ChatMessageData> { new ChatMessageData { Role = "user", Content = "hi" } },
                "system");

            Assert.That(provider.TotalTokenUsage.CacheHitTokens, Is.EqualTo(3));
            Assert.That(provider.TotalTokenUsage.CacheMissTokens, Is.EqualTo(8));

            // 8 miss * 0.27/M + 3 hit * 0.07/M + 4 out * 1.10/M
            var expected = 8 / 1000000.0 * 0.27 + 3 / 1000000.0 * 0.07 + 4 / 1000000.0 * 1.10;
            Assert.That(provider.TotalTokenUsage.EstimatedCostUsd, Is.EqualTo(expected).Within(1e-12));
        }

        [Test]
        public async Task ChatAsync_RecordsOpenAICachedTokens_AsCacheHit()
        {
            // OpenAI reports prompt_tokens_details.cached_tokens instead of DeepSeek fields.
            var handler = new FakeHttpHandler
            {
                Responder = req =>
                    "{\"choices\":[{\"message\":{\"content\":\"hi\"}}]," +
                    "\"usage\":{\"prompt_tokens\":100,\"completion_tokens\":10,\"total_tokens\":110," +
                    "\"prompt_tokens_details\":{\"cached_tokens\":90}}}"
            };
            var provider = new OpenAiCompatibleProvider(
                new OpenAiOptions { ApiKey = "sk-test", Model = "test-model" },
                new HttpClient(handler));

            await provider.ChatAsync(
                new List<ChatMessageData> { new ChatMessageData { Role = "user", Content = "hi" } },
                "system");

            Assert.That(provider.TotalTokenUsage.CacheHitTokens, Is.EqualTo(90));
            Assert.That(provider.TotalTokenUsage.CacheMissTokens, Is.EqualTo(10));
        }

        [Test]
        public async Task ChatAsync_NoCacheInfo_TreatsAllInputAsCacheMiss()
        {
            var handler = new FakeHttpHandler
            {
                Responder = req =>
                    "{\"choices\":[{\"message\":{\"content\":\"hi\"}}]," +
                    "\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":5,\"total_tokens\":15}}"
            };
            var provider = new OpenAiCompatibleProvider(
                new OpenAiOptions { ApiKey = "sk-test", Model = "test-model" },
                new HttpClient(handler));

            await provider.ChatAsync(
                new List<ChatMessageData> { new ChatMessageData { Role = "user", Content = "hi" } },
                "system");

            Assert.That(provider.TotalTokenUsage.CacheHitTokens, Is.EqualTo(0));
            Assert.That(provider.TotalTokenUsage.CacheMissTokens, Is.EqualTo(10));
        }

        [Test]
        public async Task ChatAsync_RaisesPromptAssembled_WithSystemPromptAndMessages()
        {
            var handler = new FakeHttpHandler
            {
                Responder = req => Completion("ok")
            };
            var provider = new OpenAiCompatibleProvider(new OpenAiOptions { ApiKey = "sk-test", Model = "test-model" }, new HttpClient(handler));

            PromptAssembledEventArgs captured = null;
            provider.PromptAssembled += (s, e) => { captured = e; };

            await provider.ChatAsync(
                new List<ChatMessageData>
                {
                    new ChatMessageData { Role = "user", Content = "hello" },
                    new ChatMessageData { Role = "assistant", Content = "hi" }
                },
                "sys-prompt");

            Assert.That(captured, Is.Not.Null);
            Assert.That(captured.SystemPrompt, Is.EqualTo("sys-prompt"));
            Assert.That(captured.Messages.Count, Is.EqualTo(2));
            Assert.That(captured.Messages[0].Content, Is.EqualTo("hello"));
            Assert.That(captured.EstimatedInputTokens, Is.GreaterThan(0));
        }

        [Test]
        public async Task GenerateAlgorithmDraftAsync_SubstitutesSpecAndReturnsContent()
        {
            var handler = new FakeHttpHandler
            {
                Responder = req => Completion("ALGORITHM")
            };
            var provider = new OpenAiCompatibleProvider(new OpenAiOptions { ApiKey = "sk-test", Model = "test-model" }, new HttpClient(handler));

            var result = await provider.GenerateAlgorithmDraftAsync("SPEC-MD");

            Assert.That(result, Is.EqualTo("ALGORITHM"));
            Assert.That(handler.LastBody, Does.Contain("SPEC-MD"));
        }

        [Test]
        public async Task ConsolidateAlgorithmAsync_SubstitutesDraftsAndParsesJson()
        {
            var handler = new FakeHttpHandler
            {
                Responder = req => Completion("{\"algorithm\":\"ALG\",\"questions\":\"Q\"}")
            };
            var provider = new OpenAiCompatibleProvider(new OpenAiOptions { ApiKey = "sk-test", Model = "test-model" }, new HttpClient(handler));

            var result = await provider.ConsolidateAlgorithmAsync("SPEC-MD", new[] { "draft-one", "draft-two" });

            Assert.That(result.AlgorithmMarkdown, Is.EqualTo("ALG"));
            Assert.That(result.QuestionsMarkdown, Is.EqualTo("Q"));
            Assert.That(handler.LastBody, Does.Contain("SPEC-MD"));
            Assert.That(handler.LastBody, Does.Contain("Черновик 1"));
            Assert.That(handler.LastBody, Does.Contain("draft-two"));
        }

        [Test]
        public async Task StubAiProvider_GeneratesAlgorithmAndQuestions()
        {
            var stub = new StubAiProvider();

            var draft = await stub.GenerateAlgorithmDraftAsync("spec");
            Assert.That(draft, Does.Contain("mermaid"));

            var consolidated = await stub.ConsolidateAlgorithmAsync("spec", new[] { "d1" });
            Assert.That(consolidated.AlgorithmMarkdown, Does.Contain("mermaid"));
            Assert.That(consolidated.QuestionsMarkdown, Does.Contain("Вопросы"));
        }
    }
}
