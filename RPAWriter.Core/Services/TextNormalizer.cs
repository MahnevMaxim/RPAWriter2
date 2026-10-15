using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace RPAWriter.Core.Services
{
    /// <summary>Text normalization: tokenization and stop-word removal (RU/EN).</summary>
    public static class TextNormalizer
    {
        private static readonly HashSet<string> StopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "и","в","во","не","что","он","на","я","с","со","как","а","то","все","она","так","его","но","да",
            "ты","к","у","же","вы","за","бы","по","только","ее","мне","было","вот","от","меня","еще","нет",
            "о","из","ему","теперь","когда","даже","ну","вдруг","ли","если","уже","или","ни","быть","был",
            "него","до","вас","нибудь","опять","уж","вам","ведь","там","потом","себя","ничего","ей","может",
            "они","тут","где","есть","надо","ней","для","мы","тебя","их","чем","была","сам","чтоб","без",
            "будто","чего","раз","тоже","себе","под","будет","ж","тогда","кто","этот","того","потому","этого",
            "какой","совсем","ним","здесь","этом","один","почти","мой","тем","чтобы","нее","сейчас","были",
            "куда","зачем","всех","никогда","можно","при","наконец","два","об","другой","хоть","после","над",
            "больше","тот","через","эти","нас","про","всего","них","какая","много","разве","три","эту","моя",
            "впрочем","хорошо","свою","этой","перед","иногда","лучше","чуть","том","нельзя","такой","им",
            "более","всегда","конечно","всю","между",
            "the","a","an","and","or","of","to","in","on","for","with","is","are","was","were","be","been",
            "by","as","at","it","this","that","these","those","from","into","out","up","down","do","does",
            "did","not","no","yes","will","would","can","could","should","may","might","must","shall","than",
            "then","so","if","but","also","its","his","her","their","our","your","my","we","you","he","she",
            "they","them","us","about","over","under","again","further","once","here","there","all","any",
            "both","each","few","more","most","other","some","such","only","own","same","too","very","just",
            "don","now"
        };

        public static IReadOnlyList<string> Tokenize(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();
            var tokens = new List<string>();
            foreach (var raw in Regex.Split(text, @"[^a-zA-Zа-яА-ЯёЁ0-9]+"))
            {
                if (raw.Length < 2) continue;
                var t = raw.ToLowerInvariant();
                if (StopWords.Contains(t)) continue;
                tokens.Add(t);
            }
            return tokens;
        }

        public static IReadOnlyList<string> NormalizeKeywords(IEnumerable<string> words)
        {
            if (words == null) return Array.Empty<string>();
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var w in words)
                foreach (var t in Tokenize(w))
                    set.Add(t);
            return set.ToList();
        }
    }
}
