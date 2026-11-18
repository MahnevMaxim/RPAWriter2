using System;
using Agent.Core.Models;
using Agent.Core.Services;
using Agent.Rpa.Contracts;

namespace Agent.Rpa.Services
{
    /// <summary>
    /// Creates an RPA-domain AI provider from the shared <see cref="SettingsData"/> plus the
    /// RPA-domain <see cref="RpaSettingsData"/>: no API key → deterministic stub, otherwise an
    /// OpenAI-compatible <see cref="RpaAiProvider"/>.
    /// </summary>
    public static class RpaAiProviderFactory
    {
        public static IRpaAiProvider Create(SettingsData settings, RpaSettingsData rpaSettings)
        {
            var apiKey = (settings?.ApiKey ?? "").Trim();
            if (string.IsNullOrWhiteSpace(apiKey))
                return new StubRpaAiProvider();

            var rulesPath = (rpaSettings?.RulesPath ?? "").Trim();
            if (string.IsNullOrWhiteSpace(rulesPath))
                rulesPath = PromptPaths.ResolveRulesDirectory();

            var toolCallingMode = string.Equals(settings?.ToolCallingMode, "Native", StringComparison.OrdinalIgnoreCase)
                ? ToolCallingMode.Native
                : ToolCallingMode.JsonChat;

            return new RpaAiProvider(new OpenAiOptions
            {
                BaseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl) ? "https://api.openai.com/v1" : settings.BaseUrl.Trim(),
                ApiKey = apiKey,
                Model = string.IsNullOrWhiteSpace(settings.Model) ? "gpt-4o-mini" : settings.Model.Trim(),
                RulesPath = rulesPath,
                ToolCallingMode = toolCallingMode,
                ContextWindowSize = settings.ContextWindowSize > 0 ? settings.ContextWindowSize : 409600,
                TimeoutSeconds = settings.TimeoutSeconds > 0 ? settings.TimeoutSeconds : 300,
                RetryTimeout = TimeSpan.FromMinutes(settings.RetryTimeoutMinutes > 0 ? settings.RetryTimeoutMinutes : 30),
                RetryBaseDelay = TimeSpan.FromSeconds(settings.RetryBaseDelaySeconds > 0 ? settings.RetryBaseDelaySeconds : 1),
                RetryMaxDelay = TimeSpan.FromSeconds(settings.RetryMaxDelaySeconds > 0 ? settings.RetryMaxDelaySeconds : 30),
                NoRetryOn4xx = settings.NoRetryOn4xx,
                InputPricePerMillion = settings.InputPricePerMillion >= 0 ? settings.InputPricePerMillion : 0.27,
                CacheHitPricePerMillion = settings.CacheHitPricePerMillion >= 0 ? settings.CacheHitPricePerMillion : 0.07,
                OutputPricePerMillion = settings.OutputPricePerMillion >= 0 ? settings.OutputPricePerMillion : 1.10,
                Temperature = Math.Max(0.0, Math.Min(1.0, settings.Temperature)),
                ReasoningEffort = ReasoningEffortMap.Normalize(settings.ReasoningEffort)
            });
        }
    }
}
