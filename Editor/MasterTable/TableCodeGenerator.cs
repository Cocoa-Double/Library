using System.Collections.Generic;
using System.Text;

using Cocoa.Lib.Util;

namespace Cocoa.Lib.Editor.MasterTable
{
    using FieldMeta = TableHeaderParser.FieldMeta;
    using ClusterMeta = TableClusterMap.ClusterMeta;

    /// <summary>
    /// 파싱된 헤더와 클러스터 정보로 C# 소스 문자열을 만듭니다(Table / enum / 서브 클래스 / MasterTable).
    /// </summary>
    /// <remarks>
    /// 파일 I/O 를 전혀 하지 않는 순수 문자열 생성기라, 무엇이 나오는지 확인할 때 결과 문자열만 보면 됩니다.
    /// 파일을 쓸지 판단하는 것은 파이프라인 쪽 몫입니다.
    /// </remarks>
    public static class TableCodeGenerator
    {
        #region Constants

        //== 손으로 고쳐도 다음 생성 때 덮어써지므로 파일 첫 줄에 그 사실을 남깁니다.
        private const string GeneratedHeader =
            "//== 이 파일은 Table Generator 가 만들었습니다. 직접 수정하면 다음 생성 때 사라집니다.";

        #endregion

        #region Public API - Table 클래스

        /// <summary>
        /// Table 데이터 클래스 소스를 만듭니다.
        /// looseFields 는 그대로 선언되는 컬럼, clusters 는 서브 클래스로 묶인 컬럼들입니다.
        /// </summary>
        /// <param name="baseClassFullName">이 시트가 상속할 베이스 클래스. 없으면 null 입니다.</param>
        /// <param name="inheritedFieldNames">베이스가 이미 가진 필드명. 재선언하면 필드를 가리므로 건너뜁니다.</param>
        public static string GenerateTableClass(
            string sheetName,
            IReadOnlyList<FieldMeta> looseFields,
            IReadOnlyList<ClusterMeta> clusters,
            FieldMeta keyField,
            string tableNamespace,
            string runtimeNamespace,
            IReadOnlyList<TableGenSettings.EnumExternalMap> enumExternalMaps,
            string baseClassFullName,
            HashSet<string> inheritedFieldNames)
        {
            string keyType = ResolveKeyType(keyField, enumExternalMaps);
            SplitFullName(baseClassFullName, out string baseNamespace, out string baseSimpleName);

            StringBuilder builder = new StringBuilder();
            builder.AppendLine(GeneratedHeader);
            builder.AppendLine();
            builder.AppendLine("using System;");
            builder.AppendLine("using System.Collections.Generic;");

            List<string> usings = CollectClusterUsings(clusters, tableNamespace);
            AddUsing(usings, runtimeNamespace, tableNamespace);
            AddUsing(usings, baseNamespace, tableNamespace);
            for (int i = 0; i < usings.Count; i++)
            {
                builder.AppendLine($"using {usings[i]};");
            }

            builder.AppendLine();
            builder.AppendLine($"namespace {tableNamespace}");
            builder.AppendLine("{");
            builder.AppendLine("    [Serializable]");
            builder.AppendLine(string.IsNullOrEmpty(baseSimpleName)
                ? $"    public class {sheetName} : ITableData<{keyType}>"
                : $"    public class {sheetName} : {baseSimpleName}, ITableData<{keyType}>");
            builder.AppendLine("    {");

            AppendLooseFields(builder, looseFields, enumExternalMaps, inheritedFieldNames);
            AppendClusterFields(builder, clusters, inheritedFieldNames);
            AppendKeyProperty(builder, keyField, keyType);

            builder.AppendLine("    }");
            builder.AppendLine("}");
            return builder.ToString();
        }

        #endregion

        #region Public API - 클러스터 서브 클래스

        /// <summary>
        /// 클러스터의 서브 클래스 소스를 만듭니다. 프로젝트에 없을 때만 호출합니다.
        /// 시트 접두사가 List 여도 서브 클래스의 필드는 항상 스칼라이고, List 는 Table 쪽 필드가 담습니다.
        /// </summary>
        public static string GenerateClusterSubClass(
            ClusterMeta cluster,
            string clusterNamespace,
            IReadOnlyList<TableGenSettings.EnumExternalMap> enumExternalMaps)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine(GeneratedHeader);
            builder.AppendLine();
            builder.AppendLine("using System;");
            builder.AppendLine();
            builder.AppendLine($"namespace {clusterNamespace}");
            builder.AppendLine("{");
            builder.AppendLine("    [Serializable]");
            builder.AppendLine($"    public class {cluster.SubClassSimpleName}");
            builder.AppendLine("    {");

            for (int i = 0; i < cluster.Members.Count; i++)
            {
                FieldMeta member = cluster.Members[i];
                TableTypeMap.TypeInfo scalarType = new TableTypeMap.TypeInfo(
                    member.Type.ElementType, false, member.Type.IsEnum);

                string declaration = TableTypeMap.ToDeclaration(scalarType, ResolveEnumName(member, enumExternalMaps));
                string subFieldName = TableClusterMap.ExtractSubFieldName(cluster.FieldName, member.Name);
                builder.AppendLine($"        public {declaration} {subFieldName};");
            }

            builder.AppendLine("    }");
            builder.AppendLine("}");
            return builder.ToString();
        }

        #endregion

        #region Public API - enum

        /// <summary>
        /// enum 소스를 만듭니다. 멤버는 컬럼에 등장한 문자열을 식별자로 정제한 것이며, 넘겨받은 순서를 그대로 씁니다.
        /// </summary>
        /// <remarks>
        /// 멤버 순서가 곧 번호이고 그 번호가 .asset 에 직렬화되므로 여기서 정렬하면 기존 데이터의 의미가 바뀝니다.
        /// </remarks>
        public static string GenerateEnum(string enumTypeName, IEnumerable<string> orderedValues, string tableNamespace)
        {
            List<string> members = CollectEnumMembers(enumTypeName, orderedValues);

            StringBuilder builder = new StringBuilder();
            builder.AppendLine(GeneratedHeader);
            builder.AppendLine();
            builder.AppendLine($"namespace {tableNamespace}");
            builder.AppendLine("{");
            builder.AppendLine($"    public enum {enumTypeName}");
            builder.AppendLine("    {");

            for (int i = 0; i < members.Count; i++)
            {
                builder.AppendLine($"        {members[i]},");
            }

            builder.AppendLine("    }");
            builder.AppendLine("}");
            return builder.ToString();
        }

        #endregion

        #region Public API - MasterTable 클래스

        /// <summary>시트 하나에 대응하는 MasterTable ScriptableObject 소스를 만듭니다.</summary>
        public static string GenerateMasterTableClass(
            string sheetName,
            FieldMeta keyField,
            string tableNamespace,
            string masterTableNamespace,
            string runtimeNamespace,
            IReadOnlyList<TableGenSettings.EnumExternalMap> enumExternalMaps)
        {
            string keyType = ResolveKeyType(keyField, enumExternalMaps);
            string className = $"MasterTable_{sheetName}";

            StringBuilder builder = new StringBuilder();
            builder.AppendLine(GeneratedHeader);
            builder.AppendLine();
            builder.AppendLine("using UnityEngine;");

            List<string> usings = new List<string>();
            AddUsing(usings, tableNamespace, masterTableNamespace);
            AddUsing(usings, runtimeNamespace, masterTableNamespace);
            for (int i = 0; i < usings.Count; i++)
            {
                builder.AppendLine($"using {usings[i]};");
            }

            builder.AppendLine();
            builder.AppendLine($"namespace {masterTableNamespace}");
            builder.AppendLine("{");
            builder.AppendLine($"    [CreateAssetMenu(fileName = \"{className}\", menuName = \"Cocoa/Lib/MasterTable/{sheetName}\")]");
            builder.AppendLine($"    public class {className} : MasterTableSO<{keyType}, {sheetName}> {{ }}");
            builder.AppendLine("}");
            return builder.ToString();
        }

        #endregion

        #region Private Helpers - 필드 선언

        private static void AppendLooseFields(
            StringBuilder builder,
            IReadOnlyList<FieldMeta> looseFields,
            IReadOnlyList<TableGenSettings.EnumExternalMap> enumExternalMaps,
            HashSet<string> inheritedFieldNames)
        {
            if (looseFields == null)
            {
                return;
            }

            for (int i = 0; i < looseFields.Count; i++)
            {
                FieldMeta field = looseFields[i];
                if (inheritedFieldNames != null && inheritedFieldNames.Contains(field.Name))
                {
                    continue;
                }

                string declaration = TableTypeMap.ToDeclaration(field.Type, ResolveEnumName(field, enumExternalMaps));
                builder.AppendLine($"        public {declaration} {field.Name};");
            }
        }

        private static void AppendClusterFields(
            StringBuilder builder, IReadOnlyList<ClusterMeta> clusters, HashSet<string> inheritedFieldNames)
        {
            if (clusters == null)
            {
                return;
            }

            for (int i = 0; i < clusters.Count; i++)
            {
                ClusterMeta cluster = clusters[i];
                if (inheritedFieldNames != null && inheritedFieldNames.Contains(cluster.FieldName))
                {
                    continue;
                }

                string declaration = cluster.IsList
                    ? $"List<{cluster.SubClassSimpleName}>"
                    : cluster.SubClassSimpleName;
                builder.AppendLine($"        public {declaration} {cluster.FieldName};");
            }
        }

        private static void AppendKeyProperty(StringBuilder builder, FieldMeta keyField, string keyType)
        {
            builder.AppendLine();

            //== 키를 못 정한 경우입니다. 호출자가 이미 로그를 남겼으므로 컴파일만 되게 둡니다.
            if (keyField == null)
            {
                builder.AppendLine("        //== 키로 쓸 컬럼을 찾지 못해 고정값을 반환합니다.");
                builder.AppendLine("        public int Key { get { return 0; } }");
                return;
            }

            builder.AppendLine($"        public {keyType} Key {{ get {{ return {keyField.Name}; }} }}");
        }

        #endregion

        #region Private Helpers - 이름 해석

        private static string ResolveKeyType(
            FieldMeta keyField, IReadOnlyList<TableGenSettings.EnumExternalMap> enumExternalMaps)
        {
            if (keyField == null)
            {
                return "int";
            }

            return TableTypeMap.ToDeclaration(keyField.Type, ResolveEnumName(keyField, enumExternalMaps));
        }

        //== 외부 매핑에 걸리면 전체 이름을, 아니면 생성될 enum 타입명을 그대로 씁니다.
        private static string ResolveEnumName(
            FieldMeta field, IReadOnlyList<TableGenSettings.EnumExternalMap> enumExternalMaps)
        {
            if (field == null || !field.Type.IsEnum || string.IsNullOrEmpty(field.EnumTypeName))
            {
                return field != null ? field.EnumTypeName : null;
            }

            if (enumExternalMaps == null)
            {
                return field.EnumTypeName;
            }

            string fullName = FindExternalFullName(enumExternalMaps, field.EnumTypeName);
            return !string.IsNullOrEmpty(fullName) ? fullName : field.EnumTypeName;
        }

        private static string FindExternalFullName(
            IReadOnlyList<TableGenSettings.EnumExternalMap> maps, string typeName)
        {
            for (int i = 0; i < maps.Count; i++)
            {
                TableGenSettings.EnumExternalMap map = maps[i];
                if (map == null || !map.Enabled)
                {
                    continue;
                }

                if (map.TypeName == typeName)
                {
                    return map.FullTypeName;
                }
            }

            return null;
        }

        //== 외부 enum 은 전체 이름으로 박히므로 using 이 필요 없고, 클러스터 서브 클래스만 챙기면 됩니다.
        private static List<string> CollectClusterUsings(IReadOnlyList<ClusterMeta> clusters, string tableNamespace)
        {
            List<string> list = new List<string>();
            if (clusters == null)
            {
                return list;
            }

            for (int i = 0; i < clusters.Count; i++)
            {
                SplitFullName(clusters[i].SubClassFullName, out string clusterNamespace, out _);
                AddUsing(list, clusterNamespace, tableNamespace);
            }

            return list;
        }

        private static void AddUsing(List<string> list, string candidate, string ownNamespace)
        {
            if (string.IsNullOrEmpty(candidate) || candidate == ownNamespace || list.Contains(candidate))
            {
                return;
            }

            list.Add(candidate);
        }

        private static void SplitFullName(string fullName, out string namespaceName, out string simpleName)
        {
            namespaceName = null;
            simpleName = null;
            if (string.IsNullOrEmpty(fullName))
            {
                return;
            }

            int lastDot = fullName.LastIndexOf('.');
            if (lastDot < 0)
            {
                simpleName = fullName;
                return;
            }

            namespaceName = fullName.Substring(0, lastDot);
            simpleName = fullName.Substring(lastDot + 1);
        }

        #endregion

        #region Private Helpers - enum 멤버

        private static List<string> CollectEnumMembers(string enumTypeName, IEnumerable<string> orderedValues)
        {
            List<string> members = new List<string>();
            if (orderedValues == null)
            {
                return members;
            }

            Dictionary<string, string> sourceByMember = new Dictionary<string, string>();
            foreach (string raw in orderedValues)
            {
                if (string.IsNullOrEmpty(raw))
                {
                    continue;
                }

                string trimmed = raw.Trim();
                string member = TableIdentifier.Sanitize(trimmed);

                if (!sourceByMember.TryGetValue(member, out string firstSource))
                {
                    sourceByMember.Add(member, trimmed);
                    members.Add(member);
                    continue;
                }

                //== "A B" 와 "A-B" 는 정제 후 둘 다 "A_B" 가 됩니다. 서로 다른 기획 값이 한 멤버로 합쳐지는 것을 드러냅니다.
                if (firstSource != trimmed)
                {
                    Log.Warning($"[TableGen] enum {enumTypeName} 에서 '{firstSource}' 와 '{trimmed}' 가 같은 멤버 {member} 로 합쳐집니다.", LogColor.Yellow);
                }
            }

            return members;
        }

        #endregion
    }
}
