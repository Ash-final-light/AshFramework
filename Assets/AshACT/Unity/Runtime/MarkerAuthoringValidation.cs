using System;
using System.Collections.Generic;

namespace AshACT.Unity
{
    /// <summary>校验标记配置草稿，不创建标记运行状态或执行操作。</summary>
    public static class MarkerAuthoringValidation
    {
        private const int MaximumEntries = 1024;

        public static bool TryValidateCatalog(MarkerCatalogAsset catalog, out IReadOnlyList<ConfigurationIssue> issues)
        {
            List<ConfigurationIssue> result = new List<ConfigurationIssue>();
            ValidateCatalog(catalog, result);
            issues = result.AsReadOnly();
            return result.Count == 0;
        }

        public static bool TryValidateProfile(CharacterAttributeAsset profile, out IReadOnlyList<ConfigurationIssue> issues)
        {
            List<ConfigurationIssue> result = new List<ConfigurationIssue>();
            if (profile == null)
            {
                Add(result, null, "Profile", "MissingProfile", "必须提供角色配置。");
            }
            else if (profile.ProducedMarkerIds == null || profile.ProducedMarkerIds.Length > MaximumEntries)
            {
                Add(result, profile, "ProducedMarkerIds", "InvalidMarkerList", "可产生标记列表必须非空且不超过 1024 项。");
            }
            else if (profile.MarkerCatalog != null || profile.ProducedMarkerIds.Length > 0)
            {
                HashSet<uint> available = ValidateCatalog(profile.MarkerCatalog, result);
                CheckRelease(profile, profile.MarkerCatalog, result);
                HashSet<uint> selected = new HashSet<uint>();
                foreach (uint id in profile.ProducedMarkerIds)
                {
                    if (!available.Contains(id) || !selected.Add(id))
                    {
                        Add(result, profile, "ProducedMarkerIds", "InvalidProducedMarker", "可产生标记必须引用定义库中的有效编号，且不能重复。");
                    }
                }
            }

            issues = result.AsReadOnly();
            return result.Count == 0;
        }

        public static bool TryValidateEffect(EffectGraphAsset effect, CharacterAttributeAsset sourcePreview,
            out IReadOnlyList<ConfigurationIssue> issues)
        {
            List<ConfigurationIssue> result = new List<ConfigurationIssue>();
            if (effect == null)
            {
                Add(result, null, "Effect", "MissingEffect", "必须提供效果配置。");
            }
            else if (effect.MarkerOperations == null || effect.MarkerOperations.Length > MaximumEntries)
            {
                Add(result, effect, "MarkerOperations", "InvalidMarkerList", "标记操作列表必须非空且不超过 1024 项。");
            }
            else if (effect.MarkerCatalog != null || effect.MarkerOperations.Length > 0)
            {
                HashSet<uint> available = ValidateCatalog(effect.MarkerCatalog, result);
                CheckRelease(effect, effect.MarkerCatalog, result);
                for (int i = 0; i < effect.MarkerOperations.Length; i++)
                {
                    EffectMarkerOperation operation = effect.MarkerOperations[i];
                    string path = $"MarkerOperations[{i}]";
                    if (operation == null || !Enum.IsDefined(typeof(MarkerOperationKind), operation.Kind) ||
                        !Enum.IsDefined(typeof(MarkerOperationTiming), operation.Timing) ||
                        !Enum.IsDefined(typeof(MarkerOperationTarget), operation.Target))
                    {
                        Add(result, effect, path, "InvalidMarkerOperation", "标记操作及其类型、时机、接收对象必须有效。");
                        continue;
                    }

                    if (!available.Contains(operation.MarkerId))
                    {
                        Add(result, effect, path + ".MarkerId", "MissingMarker", "操作引用的标记必须在所选定义库中存在。");
                    }

                    bool needsCount = operation.Kind == MarkerOperationKind.Add || operation.RemovalMode == MarkerRemovalMode.Count;
                    if ((needsCount && operation.Count <= 0) ||
                        (operation.Kind == MarkerOperationKind.Remove &&
                        (!Enum.IsDefined(typeof(MarkerRemovalMode), operation.RemovalMode) ||
                        !Enum.IsDefined(typeof(MarkerRemovalScope), operation.RemovalScope))))
                    {
                        Add(result, effect, path, "InvalidMarkerOperation", "请求层数必须为正整数，移除方式和来源范围必须有效。");
                    }

                    if (operation.Timing == MarkerOperationTiming.Periodic && effect.PeriodFrames <= 0)
                    {
                        Add(result, effect, path + ".Timing", "MissingMarkerPeriod", "周期标记操作要求效果配置正整数周期。");
                    }

                    if (operation.Kind == MarkerOperationKind.Add && sourcePreview != null &&
                        (sourcePreview.MarkerCatalog != effect.MarkerCatalog || sourcePreview.ProducedMarkerIds == null ||
                        Array.IndexOf(sourcePreview.ProducedMarkerIds, operation.MarkerId) < 0))
                    {
                        Add(result, effect, path + ".MarkerId", "MarkerNotProduced", "施加者预览角色未在同一标记库中声明该标记的产生资格。");
                    }
                }
            }

            issues = result.AsReadOnly();
            return result.Count == 0;
        }

        private static HashSet<uint> ValidateCatalog(MarkerCatalogAsset catalog, List<ConfigurationIssue> issues)
        {
            HashSet<uint> ids = new HashSet<uint>();
            if (catalog == null)
            {
                Add(issues, null, "MarkerCatalog", "MissingMarkerCatalog", "请选择独立的标记定义库。");
                return ids;
            }

            if (catalog.SchemaVersion != VersionedConfigurationAsset.CurrentSchemaVersion || catalog.ReleaseVersion <= 0)
            {
                Add(issues, catalog, "SchemaVersion/ReleaseVersion", "VersionMismatch", "标记库要求当前协议版本和正整数发布版本。");
            }

            if (catalog.Markers == null || catalog.Markers.Length > MaximumEntries)
            {
                Add(issues, catalog, "Markers", "InvalidMarkerList", "标记定义列表必须非空且不超过 1024 项。");
                return ids;
            }

            for (int i = 0; i < catalog.Markers.Length; i++)
            {
                MarkerDefinitionEntry entry = catalog.Markers[i];
                if (entry == null || entry.MarkerId == 0 || !ids.Add(entry.MarkerId) || string.IsNullOrWhiteSpace(entry.DisplayName))
                {
                    Add(issues, catalog, $"Markers[{i}]", "InvalidMarkerDefinition", "标记编号必须非零且唯一，名称不能为空。");
                }
            }

            return ids;
        }

        private static void CheckRelease(VersionedConfigurationAsset owner, MarkerCatalogAsset catalog,
            List<ConfigurationIssue> issues)
        {
            if (catalog != null && owner.ReleaseVersion != catalog.ReleaseVersion)
            {
                Add(issues, owner, "MarkerCatalog", "VersionMismatch", "引用的标记库发布版本必须与当前资产一致。");
            }
        }

        private static void Add(List<ConfigurationIssue> issues, VersionedConfigurationAsset asset,
            string path, string code, string message)
        {
            issues.Add(new ConfigurationIssue(code, asset, path, message));
        }
    }
}
