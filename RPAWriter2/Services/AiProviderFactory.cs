using RPAWriter.Core.Contracts;
using RPAWriter.Core.Services;

namespace RPAWriter2.Services
{
    /// <summary>Creates an AI provider from settings: no API key → stub, otherwise OpenAI-compatible.</summary>
    public static class AiProviderFactory
    {
        public static IAiProvider Create(SettingsData settings)
        {
            var apiKey = (settings?.ApiKey ?? "").Trim();
            if (string.IsNullOrWhiteSpace(apiKey))
                return new StubAiProvider();

            return new OpenAiCompatibleProvider(new OpenAiOptions
            {
                BaseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl) ? "https://api.openai.com/v1" : settings.BaseUrl.Trim(),
                ApiKey = apiKey,
                Model = string.IsNullOrWhiteSpace(settings.Model) ? "gpt-4o-mini" : settings.Model.Trim(),
                RulesPath = (settings?.RulesPath ?? "").Trim(),
                ContextWindowSize = settings.ContextWindowSize > 0 ? settings.ContextWindowSize : 256000
            });
        }
    }
}
