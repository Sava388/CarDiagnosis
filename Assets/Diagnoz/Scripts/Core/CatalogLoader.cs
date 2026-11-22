using System;
using System.Collections.Generic;
using UnityEngine;

namespace CarDiagnosis.Core
{
    public enum CatalogSource { Primary, Fallback, None }

    public sealed class CatalogLoadResult
    {
        public CatalogData Catalog;
        public CatalogSource Source = CatalogSource.None;
        public readonly List<string> PrimaryErrors = new List<string>();
        public readonly List<string> FallbackErrors = new List<string>();
    }

    // Сначала разбирает и проверяет основной каталог целиком. Если есть хоть одна ошибка —
    // основной каталог не применяется вовсе, берётся встроенный резервный (тоже после проверки).
    public static class CatalogLoader
    {
        public static CatalogLoadResult Load(string primaryJson, string primaryName, string fallbackJson,
            ICollection<string> sceneNodeIds = null, Func<string, string> missingGlyphs = null)
        {
            var result = new CatalogLoadResult();
            CatalogData primary = Parse(primaryJson, primaryName, result.PrimaryErrors);
            if (primary != null) result.PrimaryErrors.AddRange(CatalogValidator.Validate(primary, sceneNodeIds, missingGlyphs));
            if (primary != null && result.PrimaryErrors.Count == 0)
            { result.Catalog = primary; result.Source = CatalogSource.Primary; return result; }

            CatalogData fallback = Parse(fallbackJson, "встроенный каталог", result.FallbackErrors);
            if (fallback != null) result.FallbackErrors.AddRange(CatalogValidator.Validate(fallback, sceneNodeIds, missingGlyphs));
            if (fallback != null && result.FallbackErrors.Count == 0)
            { result.Catalog = fallback; result.Source = CatalogSource.Fallback; }
            return result;
        }

        public static CatalogData Parse(string json, string name, List<string> errors)
        {
            if (json == null) { errors.Add(name + ": файл не найден."); return null; }
            if (string.IsNullOrWhiteSpace(json)) { errors.Add(name + ": файл пуст."); return null; }
            try
            {
                CatalogData data = JsonUtility.FromJson<CatalogData>(json);
                if (data == null) errors.Add(name + ": файл не содержит каталога.");
                return data;
            }
            catch (Exception ex)
            {
                errors.Add(name + ": файл повреждён (" + ex.Message + ").");
                return null;
            }
        }
    }
}
