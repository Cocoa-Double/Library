using System;
using System.Collections.Generic;

namespace Cocoa.Lib.Editor.MasterTable
{
    using FieldMeta = TableHeaderParser.FieldMeta;

    /// <summary>
    /// 헤더 필드 목록에서 필드 클러스터를 인식합니다.
    /// 클러스터는 접두사가 같은 여러 컬럼(RewardID / RewardCount / RewardType)을 서브 클래스 하나(Reward)로 묶는 구조입니다.
    /// </summary>
    /// <remarks>
    /// 시트는 평평한 표인데 게임 코드는 구조체를 원하는 차이를 여기서 흡수합니다.
    /// 서브 클래스가 프로젝트에 이미 있으면 그것을 재사용하고, 없을 때만 헤더에서 추론해 새로 만듭니다.
    /// </remarks>
    public static class TableClusterMap
    {
        #region Nested Types

        /// <summary>인식된 클러스터 하나입니다. 필드 선언과 데이터 주입에 함께 쓰입니다.</summary>
        public sealed class ClusterMeta
        {
            /// <summary>Table 클래스에 선언될 필드명. 클러스터 접두사와 같습니다.</summary>
            public string FieldName;

            /// <summary>서브 클래스의 전체 이름.</summary>
            public string SubClassFullName;

            /// <summary>서브 클래스의 단순 이름.</summary>
            public string SubClassSimpleName;

            /// <summary>List 로 담을지 여부. 첫 하위 필드의 접두사로 결정합니다.</summary>
            public bool IsList;

            /// <summary>List 원소 구분자. 설정에서 복사합니다.</summary>
            public string ListSeparator;

            /// <summary>이 클러스터에 속한 원본 필드들. 컬럼 순서를 유지합니다.</summary>
            public List<FieldMeta> Members = new List<FieldMeta>();

            /// <summary>서브 클래스가 이미 프로젝트에 있으면 그 Type, 없으면 null.</summary>
            public Type ExistingSubClassType;

            /// <summary>생성기가 서브 클래스 .cs 를 만들어야 하는지 여부.</summary>
            public bool NeedsGeneration
            {
                get { return ExistingSubClassType == null; }
            }
        }

        /// <summary>Analyze 의 결과입니다.</summary>
        public sealed class AnalyzeResult
        {
            /// <summary>인식된 클러스터들.</summary>
            public List<ClusterMeta> Clusters = new List<ClusterMeta>();

            /// <summary>어느 클러스터에도 흡수되지 않고 남은 필드들.</summary>
            public List<FieldMeta> LooseFields = new List<FieldMeta>();
        }

        #endregion

        #region Public API

        /// <summary>헤더 필드 목록을 클러스터와 일반 필드로 분류합니다.</summary>
        public static AnalyzeResult Analyze(IReadOnlyList<FieldMeta> fields, TableGenSettings settings)
        {
            AnalyzeResult result = new AnalyzeResult();
            if (fields == null || fields.Count == 0)
            {
                return result;
            }

            List<TableGenSettings.FieldCluster> active = CollectActiveClusters(settings);

            //== 각 필드가 몇 번째 클러스터에 속하는지 먼저 정합니다. -1 은 클러스터에 속하지 않는 필드입니다.
            int[] clusterIndexByField = new int[fields.Count];
            for (int i = 0; i < clusterIndexByField.Length; i++)
            {
                clusterIndexByField[i] = -1;
            }

            for (int fi = 0; fi < fields.Count; fi++)
            {
                for (int ci = 0; ci < active.Count; ci++)
                {
                    if (StartsWithClusterPrefix(fields[fi].Name, active[ci].FieldPrefix))
                    {
                        //== 접두사 길이 내림차순이라 가장 구체적인 클러스터가 먼저 걸립니다.
                        clusterIndexByField[fi] = ci;
                        break;
                    }
                }
            }

            //== 멤버 순서가 곧 서브 클래스의 필드 순서라 컬럼 순서대로 조립합니다.
            Dictionary<int, ClusterMeta> clusterByIndex = new Dictionary<int, ClusterMeta>();
            for (int fi = 0; fi < fields.Count; fi++)
            {
                int ci = clusterIndexByField[fi];
                if (ci < 0)
                {
                    result.LooseFields.Add(fields[fi]);
                    continue;
                }

                if (!clusterByIndex.TryGetValue(ci, out ClusterMeta meta))
                {
                    meta = new ClusterMeta
                    {
                        FieldName = active[ci].FieldPrefix,
                        SubClassFullName = active[ci].SubClassTypeName,
                        ListSeparator = settings != null ? settings.ListSeparator : null
                    };
                    clusterByIndex.Add(ci, meta);
                    result.Clusters.Add(meta);
                }

                meta.Members.Add(fields[fi]);
            }

            for (int i = 0; i < result.Clusters.Count; i++)
            {
                FinalizeCluster(result.Clusters[i], settings);
            }

            return result;
        }

        /// <summary>
        /// 클러스터 하위 필드명에서 서브 클래스의 필드명을 뽑습니다.
        /// FieldName 이 "Reward" 이고 memberFieldName 이 "RewardID" 면 "ID" 가 됩니다.
        /// </summary>
        public static string ExtractSubFieldName(string clusterFieldName, string memberFieldName)
        {
            if (string.IsNullOrEmpty(memberFieldName) || string.IsNullOrEmpty(clusterFieldName))
            {
                return memberFieldName;
            }

            if (memberFieldName.Length <= clusterFieldName.Length)
            {
                return memberFieldName;
            }

            if (!memberFieldName.StartsWith(clusterFieldName, StringComparison.Ordinal))
            {
                return memberFieldName;
            }

            return memberFieldName.Substring(clusterFieldName.Length);
        }

        #endregion

        #region Private Helpers

        //== 접두사 길이 내림차순으로 돌려줍니다. 길이가 같으면 이름 순으로 정해 설정 목록의 순서가 결과를 바꾸지 않게 합니다.
        private static List<TableGenSettings.FieldCluster> CollectActiveClusters(TableGenSettings settings)
        {
            List<TableGenSettings.FieldCluster> list = new List<TableGenSettings.FieldCluster>();
            if (settings == null || settings.FieldClusters == null)
            {
                return list;
            }

            for (int i = 0; i < settings.FieldClusters.Count; i++)
            {
                TableGenSettings.FieldCluster cluster = settings.FieldClusters[i];
                if (cluster == null || !cluster.Enabled || string.IsNullOrEmpty(cluster.FieldPrefix))
                {
                    continue;
                }

                list.Add(cluster);
            }

            list.Sort(CompareByPrefix);
            return list;
        }

        private static int CompareByPrefix(TableGenSettings.FieldCluster a, TableGenSettings.FieldCluster b)
        {
            int byLength = b.FieldPrefix.Length.CompareTo(a.FieldPrefix.Length);
            if (byLength != 0)
            {
                return byLength;
            }

            return string.Compare(a.FieldPrefix, b.FieldPrefix, StringComparison.Ordinal);
        }

        //== 다음 글자가 대문자여야 클러스터로 봅니다. 이 조건이 없으면 'Reward' 에 'Rewarded' 가 걸립니다.
        private static bool StartsWithClusterPrefix(string fieldName, string prefix)
        {
            if (string.IsNullOrEmpty(fieldName) || string.IsNullOrEmpty(prefix))
            {
                return false;
            }

            if (fieldName.Length <= prefix.Length)
            {
                return false;
            }

            if (!fieldName.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }

            return char.IsUpper(fieldName[prefix.Length]);
        }

        private static void FinalizeCluster(ClusterMeta meta, TableGenSettings settings)
        {
            if (meta.Members.Count > 0)
            {
                meta.IsList = meta.Members[0].Type.IsList;
            }

            //== 서브 클래스 이름을 비워 두면 Table 네임스페이스 아래에 클러스터명으로 새로 만듭니다.
            if (string.IsNullOrEmpty(meta.SubClassFullName))
            {
                string tableNamespace = settings != null ? settings.TableNamespace : null;
                meta.SubClassFullName = string.IsNullOrEmpty(tableNamespace)
                    ? meta.FieldName
                    : $"{tableNamespace}.{meta.FieldName}";
            }

            int lastDot = meta.SubClassFullName.LastIndexOf('.');
            meta.SubClassSimpleName = lastDot >= 0
                ? meta.SubClassFullName.Substring(lastDot + 1)
                : meta.SubClassFullName;

            meta.ExistingSubClassType = TableReflection.FindType(meta.SubClassFullName);
        }

        #endregion
    }
}
