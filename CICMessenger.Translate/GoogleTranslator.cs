using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;

namespace CICMessenger.Translate
{
    class GoogleTranslator
    {
        static readonly HttpClient httpClient = new();

        string apiKey;

        public GoogleTranslator(string apiKey)
        {
            this.apiKey = apiKey;
        }

        public string Translate(string fromLanguage, string toLanguage, string text)
        {
            string? detectedLanguage;
            return Translate(fromLanguage, toLanguage, text, out detectedLanguage);
        }

        public string Translate(string toLanguage, string text, out string? detectedLanguage)
        {
            return Translate(null, toLanguage, text, out detectedLanguage);
        }

        string Translate(string? fromLanguage, string toLanguage, string text, out string? detectedLanguage)
        {
            detectedLanguage = null;
            text = HttpUtility.UrlEncode(text);

            String apiUrl;
            if (String.IsNullOrEmpty(fromLanguage))
                apiUrl = "https://www.googleapis.com/language/translate/v2?key={0}&target={1}&q={2}";
            else
                apiUrl = "https://www.googleapis.com/language/translate/v2?key={0}&target={1}&q={2}&source={3}";

            string url = String.Format(apiUrl, apiKey, toLanguage, text, fromLanguage);

            text = httpClient.GetStringAsync(url).GetAwaiter().GetResult();

            string translatedText = String.Empty;

            var result = JsonSerializer.Deserialize(text, TranslateJsonContext.Default.TranslateResult)!;
            if (result.Data.Translations.Any())
            {
                var translation = result.Data.Translations.First();
                detectedLanguage = translation.DetectedSourceLanguage;
                translatedText = translation.TranslatedText;
            }

            return translatedText;
        }
    }

    class TranslateResult
    {
        public TranslateData Data { get; set; } = null!;
    }

    class TranslateData
    {
        public List<TranslateTranslation> Translations { get; set; } = null!;
    }

    class TranslateTranslation
    {
        public string TranslatedText { get; set; } = null!;
        public string? DetectedSourceLanguage { get; set; }
    }

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(TranslateResult))]
    partial class TranslateJsonContext : JsonSerializerContext
    {
    }
}
