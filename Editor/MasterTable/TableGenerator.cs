using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

using Cocoa.Lib.MasterTable;
using Cocoa.Lib.Util;

namespace Cocoa.Lib.Editor.MasterTable
{
    using FieldMeta = TableHeaderParser.FieldMeta;
    using ClusterMeta = TableClusterMap.ClusterMeta;

    /// <summary>
    /// 시트 다운로드부터 코드 생성, 컴파일 대기, 데이터 주입까지 엮는 실행기입니다.
    /// </summary>
    /// <remarks>
    /// 생성한 클래스에 데이터를 넣으려면 그 클래스가 컴파일되어 있어야 하는데 같은 프레임에는 불가능합니다.
    /// 그래서 코드 생성 단계에서 데이터를 파일로 보류해 두고, 컴파일이 끝난 뒤 다시 읽어 주입하는 2패스 구조입니다.
    /// 앞 단계는 enum 합치기와 서브 클래스 재사용 판정 때문에 시트 전체를 먼저 봐야 해서
    /// 다운로드, 헤더 분석, enum, 서브 클래스, Table/MasterTable, 보류 저장 순으로 다시 나뉩니다.
    /// </remarks>
    public static class TableGenerator
    {
        #region Nested Types - 보류 데이터

        //== 컴파일 경계를 넘겨야 해서 JsonUtility 로 직렬화됩니다. 중첩 제네릭을 못 다루는 제약에 맞춰 전부 평평한 클래스입니다.

        [Serializable]
        private sealed class PendingBatch
        {
            public List<PendingTable> Tables = new List<PendingTable>();
        }

        [Serializable]
        private sealed class PendingTable
        {
            public string TableTypeName;
            public string MasterTypeName;
            public string AssetPath;
            public string ListSeparator;

            //== 키 컬럼의 0-based 인덱스. 키를 정하지 못했으면 -1 입니다.
            public int KeyColumnIndex = -1;

            //== Rows[0] 이 시트에서 몇 번째 행인지(1부터). 경고에 시트 행 번호를 적기 위한 값입니다.
            public int SheetRowOffset;

            public List<PendingField> LooseFields = new List<PendingField>();
            public List<PendingCluster> Clusters = new List<PendingCluster>();
            public List<PendingRow> Rows = new List<PendingRow>();
        }

        [Serializable]
        private sealed class PendingField
        {
            public int ColumnIndex;
            public string Name;
        }

        [Serializable]
        private sealed class PendingCluster
        {
            public string FieldName;
            public string SubClassFullName;
            public bool IsList;
            public List<PendingClusterMember> Members = new List<PendingClusterMember>();
        }

        [Serializable]
        private sealed class PendingClusterMember
        {
            public int ColumnIndex;
            public string SubFieldName;
        }

        //== JsonUtility 가 List<List<string>> 을 직렬화하지 못해 행마다 래퍼를 씌웁니다.
        [Serializable]
        private sealed class PendingRow
        {
            public List<string> Cells = new List<string>();
        }

        #endregion

        #region Nested Types - 파이프라인 작업 데이터

        //== 다운로드한 원본과 헤더 분석 결과를 시트당 하나씩 들고 다닙니다.
        private sealed class SheetData
        {
            public string SheetName;
            public List<List<string>> Rows;
            public List<FieldMeta> AllFields;
            public TableClusterMap.AnalyzeResult ClusterAnalysis;
            public FieldMeta KeyField;
        }

        //== enum 이름별로 값을 모으는 통입니다. 번호가 순서에 달려 있어 등장 순서 목록과 중복 판정 집합을 함께 둡니다.
        private sealed class EnumBucket
        {
            public string TypeName;
            public HashSet<string> Seen = new HashSet<string>(StringComparer.Ordinal);
            public List<string> OrderedValues = new List<string>();

            public void Add(string value)
            {
                if (Seen.Add(value))
                {
                    OrderedValues.Add(value);
                }
            }
        }

        #endregion

        #region Constants

        private const string ClustersSubfolder = "Clusters";
        private const string DefaultListSeparator = "^";

        #endregion

        #region Static Fields

        //== Temp 는 에디터를 껐다 켜면 비워지므로 컴파일 중에만 살아 있어야 하는 파일에 적당합니다.
        private static readonly string _pendingPath = Path.Combine("Temp", "CocoaTableGen_pending.json");

        #endregion

        #region Public API - 코드 생성

        /// <summary>시트를 순차로 받아 코드를 생성하고, 데이터는 보류 저장한 뒤 임포트를 예약합니다.</summary>
        public static void GenerateSheets(TableGenSettings settings, List<string> sheetNames, Action onComplete = null)
        {
            if (settings == null)
            {
                Log.Error("[TableGen] 설정(TableGenSettings)이 없습니다.", LogColor.Red);
                InvokeSafe(onComplete);
                return;
            }

            if (sheetNames == null || sheetNames.Count == 0)
            {
                Log.Warning("[TableGen] 생성할 시트가 없습니다.", LogColor.Yellow);
                InvokeSafe(onComplete);
                return;
            }

            List<SheetData> downloaded = new List<SheetData>();
            int index = 0;

            //== 동시에 던지면 API 쿼터에 걸리고 실패한 시트를 특정하기도 어려워 하나씩 받습니다.
            Action downloadNext = null;
            downloadNext = () =>
            {
                if (index >= sheetNames.Count)
                {
                    FinishPipeline(settings, downloaded);
                    InvokeSafe(onComplete);
                    return;
                }

                string sheetName = sheetNames[index];
                index++;

                if (string.IsNullOrEmpty(sheetName))
                {
                    downloadNext();
                    return;
                }

                TableSheetDownloader.DownloadAsync(settings.SpreadsheetId, sheetName, settings.ApiKey, result =>
                {
                    if (!result.Success)
                    {
                        Log.Error($"[TableGen] '{sheetName}' 다운로드 실패: {result.Error}", LogColor.Red);
                    }
                    else
                    {
                        BackupCsv(settings, sheetName, result.Rows);
                        downloaded.Add(new SheetData { SheetName = sheetName, Rows = result.Rows });
                    }

                    downloadNext();
                });
            };

            downloadNext();
        }

        #endregion

        #region Public API - 데이터 주입

        /// <summary>보류 데이터를 읽어 .asset 에 주입합니다. 컴파일 후 자동으로 불리며 직접 불러도 됩니다.</summary>
        public static void ImportPendingData()
        {
            if (!File.Exists(_pendingPath))
            {
                return;
            }

            PendingBatch batch;
            try
            {
                batch = JsonUtility.FromJson<PendingBatch>(File.ReadAllText(_pendingPath));
            }
            catch (Exception e)
            {
                Log.Error($"[TableGen] 보류 데이터를 읽지 못했습니다: {e.Message}", LogColor.Red);
                ClearPending();
                return;
            }

            if (batch == null || batch.Tables == null)
            {
                ClearPending();
                return;
            }

            int injected = 0;
            for (int i = 0; i < batch.Tables.Count; i++)
            {
                if (InjectOneTable(batch.Tables[i]))
                {
                    injected++;
                }
            }

            AssetDatabase.SaveAssets();

            //== 성공이든 실패든 보류 데이터는 지웁니다. 남기면 리로드마다 같은 실패를 반복하고,
            //== 원인은 대개 생성된 코드의 컴파일 오류라 고친 뒤 다시 생성하는 것이 정상 절차입니다.
            ClearPending();
            ReportInjectResult(injected, batch.Tables.Count);
        }

        #endregion

        #region Private Helpers - 코드 생성 파이프라인

        private static void FinishPipeline(TableGenSettings settings, List<SheetData> sheets)
        {
            if (sheets.Count == 0)
            {
                Log.Warning("[TableGen] 다운로드된 시트가 없습니다.", LogColor.Yellow);
                return;
            }

            int headerIndex = Mathf.Max(0, settings.HeaderRow - 1);
            int dataStartIndex = Mathf.Max(headerIndex + 1, settings.DataStartRow - 1);

            AnalyzeSheets(settings, sheets, headerIndex);
            if (sheets.Count == 0)
            {
                Log.Warning("[TableGen] 유효한 시트가 없습니다.", LogColor.Yellow);
                return;
            }

            //== 외부 매핑에 걸리는 enum 은 이름을 먼저 바꿔 둬야 뒤따르는 생성이 알아서 그 이름을 씁니다.
            RewriteEnumTypeNamesByExternalMap(settings, sheets);

            List<string> writtenPaths = new List<string>();
            GenerateEnums(settings, sheets, dataStartIndex, writtenPaths);
            GenerateClusterSubClasses(settings, sheets, writtenPaths);

            PendingBatch batch = new PendingBatch();
            for (int i = 0; i < sheets.Count; i++)
            {
                GenerateTableAndMaster(settings, sheets[i], dataStartIndex, batch, writtenPaths);
            }

            if (batch.Tables.Count == 0)
            {
                Log.Warning("[TableGen] 생성된 테이블이 없습니다.", LogColor.Yellow);
                return;
            }

            SavePending(batch);
            Log.Info($"[TableGen] 코드 생성 완료 (테이블 {batch.Tables.Count}개, 변경된 파일 {writtenPaths.Count}개). 컴파일이 끝나면 데이터를 주입합니다.");

            ImportWrittenAssets(writtenPaths);
            ScheduleImport();
        }

        //== 헤더를 읽고 클러스터를 나눈 뒤 키 컬럼을 정합니다. 쓸 수 없는 시트는 목록에서 빼냅니다.
        private static void AnalyzeSheets(TableGenSettings settings, List<SheetData> sheets, int headerIndex)
        {
            for (int i = sheets.Count - 1; i >= 0; i--)
            {
                SheetData sheet = sheets[i];

                if (sheet.Rows.Count <= headerIndex)
                {
                    Log.Error($"[TableGen] '{sheet.SheetName}' 에 헤더 행이 없습니다. 건너뜁니다.", LogColor.Red);
                    sheets.RemoveAt(i);
                    continue;
                }

                sheet.AllFields = TableHeaderParser.Parse(sheet.Rows[headerIndex], sheet.SheetName);
                if (sheet.AllFields.Count == 0)
                {
                    Log.Warning($"[TableGen] '{sheet.SheetName}' 에 접두사가 붙은 컬럼이 없습니다. 건너뜁니다.", LogColor.Yellow);
                    sheets.RemoveAt(i);
                    continue;
                }

                sheet.ClusterAnalysis = TableClusterMap.Analyze(sheet.AllFields, settings);
                sheet.KeyField = ResolveKeyField(sheet);
            }
        }

        //== 키는 Table 클래스에 직접 선언된 단일 값 필드여야 합니다.
        //== 첫 컬럼이 클러스터에 묶여 있으면 그 필드는 클래스에 아예 없고, List 면 키로 쓸 수 없습니다.
        private static FieldMeta ResolveKeyField(SheetData sheet)
        {
            List<FieldMeta> looseFields = sheet.ClusterAnalysis.LooseFields;
            for (int i = 0; i < looseFields.Count; i++)
            {
                if (looseFields[i].Type.IsList)
                {
                    continue;
                }

                FieldMeta key = looseFields[i];
                if (!ReferenceEquals(key, sheet.AllFields[0]))
                {
                    Log.Warning($"[TableGen] '{sheet.SheetName}' 의 첫 컬럼은 키로 쓸 수 없어 '{key.Name}' 을 키로 씁니다.", LogColor.Yellow);
                }

                return key;
            }

            Log.Error($"[TableGen] '{sheet.SheetName}' 에서 키로 쓸 컬럼을 찾지 못했습니다. 클러스터에 묶이지 않은 단일 값 컬럼이 하나는 있어야 합니다.", LogColor.Red);
            return null;
        }

        //== enum 이름별로 모든 시트의 값을 모아 하나의 타입으로 만듭니다. 외부 참조로 등록된 이름은 전부 건너뜁니다.
        private static void GenerateEnums(
            TableGenSettings settings, List<SheetData> sheets, int dataStartIndex, List<string> writtenPaths)
        {
            HashSet<string> externalNames = CollectExternalEnumNames(settings);
            List<EnumBucket> buckets = new List<EnumBucket>();
            Dictionary<string, EnumBucket> bucketByName = new Dictionary<string, EnumBucket>(StringComparer.Ordinal);

            for (int si = 0; si < sheets.Count; si++)
            {
                SheetData sheet = sheets[si];
                List<List<string>> dataRows = ExtractDataRows(sheet.Rows, dataStartIndex);

                for (int fi = 0; fi < sheet.AllFields.Count; fi++)
                {
                    FieldMeta field = sheet.AllFields[fi];
                    if (!field.Type.IsEnum)
                    {
                        continue;
                    }

                    string typeName = field.EnumTypeName;
                    if (string.IsNullOrEmpty(typeName) || externalNames.Contains(typeName))
                    {
                        continue;
                    }

                    if (!bucketByName.TryGetValue(typeName, out EnumBucket bucket))
                    {
                        bucket = new EnumBucket { TypeName = typeName };
                        bucketByName.Add(typeName, bucket);
                        buckets.Add(bucket);
                    }

                    List<string> values = TableValueParser.CollectColumn(dataRows, field.ColumnIndex);

                    //== es_ 컬럼은 셀 하나가 "A^B^C" 형태라 쪼개야 개별 멤버가 나옵니다.
                    if (field.Type.IsList)
                    {
                        values = SplitListValues(values, settings.ListSeparator);
                    }

                    for (int vi = 0; vi < values.Count; vi++)
                    {
                        if (!string.IsNullOrEmpty(values[vi]))
                        {
                            bucket.Add(values[vi]);
                        }
                    }
                }
            }

            for (int i = 0; i < buckets.Count; i++)
            {
                EnumBucket bucket = buckets[i];
                string source = TableCodeGenerator.GenerateEnum(bucket.TypeName, bucket.OrderedValues, settings.TableNamespace);
                WriteIfChanged(Path.Combine(settings.TableOutputPath, $"{bucket.TypeName}.cs"), source, writtenPaths);
            }
        }

        //== 프로젝트에 없는 서브 클래스만 만듭니다. 여러 시트가 같은 것을 가리키면 먼저 만난 시트의 멤버 구조를 씁니다.
        private static void GenerateClusterSubClasses(
            TableGenSettings settings, List<SheetData> sheets, List<string> writtenPaths)
        {
            HashSet<string> generated = new HashSet<string>(StringComparer.Ordinal);
            string clusterFolder = Path.Combine(settings.TableOutputPath, ClustersSubfolder);

            for (int si = 0; si < sheets.Count; si++)
            {
                TableClusterMap.AnalyzeResult analysis = sheets[si].ClusterAnalysis;
                if (analysis == null)
                {
                    continue;
                }

                for (int ci = 0; ci < analysis.Clusters.Count; ci++)
                {
                    ClusterMeta cluster = analysis.Clusters[ci];
                    if (!cluster.NeedsGeneration || !generated.Add(cluster.SubClassFullName))
                    {
                        continue;
                    }

                    int lastDot = cluster.SubClassFullName.LastIndexOf('.');
                    string clusterNamespace = lastDot >= 0
                        ? cluster.SubClassFullName.Substring(0, lastDot)
                        : settings.TableNamespace;

                    string source = TableCodeGenerator.GenerateClusterSubClass(
                        cluster, clusterNamespace, settings.EnumExternalMaps);
                    WriteIfChanged(Path.Combine(clusterFolder, $"{cluster.SubClassSimpleName}.cs"), source, writtenPaths);
                }
            }
        }

        private static void GenerateTableAndMaster(
            TableGenSettings settings, SheetData sheet, int dataStartIndex, PendingBatch batch, List<string> writtenPaths)
        {
            string baseClassFullName = ResolveBaseClass(settings, sheet.SheetName);
            HashSet<string> inheritedFieldNames = CollectInheritedFieldNames(baseClassFullName);

            //== DontAutoClass 는 손으로 쓴 클래스를 덮어쓰지 않으면서 데이터와 MasterTable 은 계속 갱신하려는 목록입니다.
            if (IsDontAutoClass(settings, sheet.SheetName))
            {
                Log.Info($"[TableGen] '{sheet.SheetName}' 은 DontAutoClass 목록에 있어 Table 클래스 생성을 건너뜁니다.");
            }
            else
            {
                string tableSource = TableCodeGenerator.GenerateTableClass(
                    sheet.SheetName,
                    sheet.ClusterAnalysis.LooseFields,
                    sheet.ClusterAnalysis.Clusters,
                    sheet.KeyField,
                    settings.TableNamespace,
                    settings.RuntimeNamespace,
                    settings.EnumExternalMaps,
                    baseClassFullName,
                    inheritedFieldNames);
                WriteIfChanged(Path.Combine(settings.TableOutputPath, $"{sheet.SheetName}.cs"), tableSource, writtenPaths);
            }

            string masterSource = TableCodeGenerator.GenerateMasterTableClass(
                sheet.SheetName,
                sheet.KeyField,
                settings.TableNamespace,
                settings.MasterTableNamespace,
                settings.RuntimeNamespace,
                settings.EnumExternalMaps);
            WriteIfChanged(
                Path.Combine(settings.MasterTableOutputPath, $"MasterTable_{sheet.SheetName}.cs"),
                masterSource,
                writtenPaths);

            batch.Tables.Add(BuildPendingTable(settings, sheet, dataStartIndex));
        }

        private static PendingTable BuildPendingTable(TableGenSettings settings, SheetData sheet, int dataStartIndex)
        {
            string assetFolder = ResolveMasterAssetFolder(settings);
            PendingTable pending = new PendingTable
            {
                TableTypeName = $"{settings.TableNamespace}.{sheet.SheetName}",
                MasterTypeName = $"{settings.MasterTableNamespace}.MasterTable_{sheet.SheetName}",
                AssetPath = $"{assetFolder}/MasterTable_{sheet.SheetName}.asset",
                ListSeparator = settings.ListSeparator,
                KeyColumnIndex = sheet.KeyField != null ? sheet.KeyField.ColumnIndex : -1,
                SheetRowOffset = dataStartIndex + 1
            };

            List<FieldMeta> looseFields = sheet.ClusterAnalysis.LooseFields;
            for (int i = 0; i < looseFields.Count; i++)
            {
                pending.LooseFields.Add(new PendingField
                {
                    ColumnIndex = looseFields[i].ColumnIndex,
                    Name = looseFields[i].Name
                });
            }

            List<ClusterMeta> clusters = sheet.ClusterAnalysis.Clusters;
            for (int i = 0; i < clusters.Count; i++)
            {
                pending.Clusters.Add(BuildPendingCluster(clusters[i]));
            }

            //== 헤더와 설명 행은 주입에 쓰이지 않습니다. 잘라 두면 보류 JSON 이 작아지고 시작 인덱스도 다시 계산할 필요가 없습니다.
            for (int r = dataStartIndex; r < sheet.Rows.Count; r++)
            {
                pending.Rows.Add(new PendingRow { Cells = sheet.Rows[r] });
            }

            return pending;
        }

        private static PendingCluster BuildPendingCluster(ClusterMeta cluster)
        {
            PendingCluster pending = new PendingCluster
            {
                FieldName = cluster.FieldName,
                SubClassFullName = cluster.SubClassFullName,
                IsList = cluster.IsList
            };

            for (int i = 0; i < cluster.Members.Count; i++)
            {
                FieldMeta member = cluster.Members[i];
                pending.Members.Add(new PendingClusterMember
                {
                    ColumnIndex = member.ColumnIndex,
                    SubFieldName = TableClusterMap.ExtractSubFieldName(cluster.FieldName, member.Name)
                });
            }

            return pending;
        }

        #endregion

        #region Private Helpers - 데이터 주입

        private static bool InjectOneTable(PendingTable pending)
        {
            Type tableType = TableReflection.FindType(pending.TableTypeName);
            Type masterType = TableReflection.FindType(pending.MasterTypeName);
            if (tableType == null || masterType == null)
            {
                Log.Error($"[TableGen] 타입을 찾지 못했습니다(컴파일 실패로 보입니다): {pending.TableTypeName} / {pending.MasterTypeName}", LogColor.Red);
                return false;
            }

            MasterTableBase asset = LoadOrCreateAsset(masterType, pending.AssetPath);
            if (asset == null)
            {
                return false;
            }

            Dictionary<string, Type> subTypeByField = CollectSubTypes(pending);
            IList datas = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(tableType));
            int[] fieldColumns = CollectFieldColumns(pending);
            TableParseReport report = new TableParseReport();

            int skippedWithoutKey = 0;
            int firstSkippedRow = 0;

            for (int r = 0; r < pending.Rows.Count; r++)
            {
                List<string> row = pending.Rows[r] != null ? pending.Rows[r].Cells : null;

                //== 주석 컬럼만 채워진 행을 여기서 걸러야 합니다. 그 값은 어느 필드로도 들어가지 않으므로
                //== 통과시키면 전부 기본값인 껍데기가 생기고, 그런 행이 둘 이상이면 키까지 겹칩니다.
                if (!HasAnyFieldValue(row, fieldColumns))
                {
                    continue;
                }

                //== 값은 있는데 키만 빈 행은 조회가 불가능하므로 데이터 오류입니다.
                if (pending.KeyColumnIndex >= 0 &&
                    string.IsNullOrEmpty(TableValueParser.CellAt(row, pending.KeyColumnIndex).Trim()))
                {
                    if (skippedWithoutKey == 0)
                    {
                        firstSkippedRow = pending.SheetRowOffset + r;
                    }

                    skippedWithoutKey++;
                    continue;
                }

                object instance = Activator.CreateInstance(tableType);
                InjectLooseFields(pending, tableType, instance, row, report);
                InjectClusterFields(pending, tableType, instance, row, subTypeByField, report);
                datas.Add(instance);
            }

            if (skippedWithoutKey > 0)
            {
                Log.Warning($"[TableGen] '{tableType.Name}' 에서 키가 빈 행 {skippedWithoutKey}개를 건너뛰었습니다(첫 행: 시트 {firstSkippedRow}행).", LogColor.Yellow);
            }

            report.Flush(tableType.Name);

            asset.ApplyGeneratedDatas(datas);
            asset.RebuildCache();

            EditorUtility.SetDirty(asset);
            return true;
        }

        //== 서브 클래스 타입은 행마다 찾을 필요가 없어 미리 한 번만 찾아 둡니다.
        private static Dictionary<string, Type> CollectSubTypes(PendingTable pending)
        {
            Dictionary<string, Type> subTypeByField = new Dictionary<string, Type>(StringComparer.Ordinal);
            for (int ci = 0; ci < pending.Clusters.Count; ci++)
            {
                PendingCluster cluster = pending.Clusters[ci];
                Type subType = TableReflection.FindType(cluster.SubClassFullName);
                if (subType == null)
                {
                    Log.Error($"[TableGen] 서브 클래스를 찾지 못했습니다: {cluster.SubClassFullName}", LogColor.Red);
                    continue;
                }

                subTypeByField[cluster.FieldName] = subType;
            }

            return subTypeByField;
        }

        //== 값이 실제로 들어가는 컬럼 인덱스들입니다. 일반 필드와 클러스터 멤버를 모두 포함합니다.
        private static int[] CollectFieldColumns(PendingTable pending)
        {
            List<int> columns = new List<int>(pending.LooseFields.Count + pending.Clusters.Count);

            for (int i = 0; i < pending.LooseFields.Count; i++)
            {
                columns.Add(pending.LooseFields[i].ColumnIndex);
            }

            for (int ci = 0; ci < pending.Clusters.Count; ci++)
            {
                List<PendingClusterMember> members = pending.Clusters[ci].Members;
                for (int mi = 0; mi < members.Count; mi++)
                {
                    columns.Add(members[mi].ColumnIndex);
                }
            }

            return columns.ToArray();
        }

        private static bool HasAnyFieldValue(List<string> row, int[] fieldColumns)
        {
            if (row == null)
            {
                return false;
            }

            for (int i = 0; i < fieldColumns.Length; i++)
            {
                if (TableValueParser.CellAt(row, fieldColumns[i]).Trim().Length > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static void InjectLooseFields(
            PendingTable pending, Type tableType, object instance, List<string> row, TableParseReport report)
        {
            for (int i = 0; i < pending.LooseFields.Count; i++)
            {
                PendingField field = pending.LooseFields[i];
                FieldInfo fieldInfo = tableType.GetField(field.Name);
                if (fieldInfo == null)
                {
                    continue;
                }

                string cell = TableValueParser.CellAt(row, field.ColumnIndex);
                fieldInfo.SetValue(instance, TableValueParser.ParseCell(
                    cell, fieldInfo.FieldType, pending.ListSeparator, report, field.Name));
            }
        }

        private static void InjectClusterFields(
            PendingTable pending, Type tableType, object instance, List<string> row,
            Dictionary<string, Type> subTypeByField, TableParseReport report)
        {
            for (int ci = 0; ci < pending.Clusters.Count; ci++)
            {
                PendingCluster cluster = pending.Clusters[ci];
                if (!subTypeByField.TryGetValue(cluster.FieldName, out Type subType))
                {
                    continue;
                }

                FieldInfo tableFieldInfo = tableType.GetField(cluster.FieldName);
                if (tableFieldInfo == null)
                {
                    continue;
                }

                object value = cluster.IsList
                    ? BuildClusterList(cluster, subType, row, pending.ListSeparator, report)
                    : BuildClusterSingle(cluster, subType, row, pending.ListSeparator, report);
                tableFieldInfo.SetValue(instance, value);
            }
        }

        //== 하위 컬럼이 전부 비면 빈 인스턴스 대신 null 을 넣습니다. "보상 없음" 과 "보상 0개" 는 구분되어야 합니다.
        private static object BuildClusterSingle(
            PendingCluster cluster, Type subType, IReadOnlyList<string> row, string listSeparator, TableParseReport report)
        {
            object instance = Activator.CreateInstance(subType);
            bool hasAnyValue = false;

            for (int i = 0; i < cluster.Members.Count; i++)
            {
                PendingClusterMember member = cluster.Members[i];
                FieldInfo fieldInfo = subType.GetField(member.SubFieldName);
                if (fieldInfo == null)
                {
                    continue;
                }

                string cell = TableValueParser.CellAt(row, member.ColumnIndex);
                if (!string.IsNullOrEmpty(cell))
                {
                    hasAnyValue = true;
                }

                fieldInfo.SetValue(instance, TableValueParser.ParseCell(
                    cell, fieldInfo.FieldType, listSeparator, report, cluster.FieldName + member.SubFieldName));
            }

            return hasAnyValue ? instance : null;
        }

        //== 하위 컬럼들이 각각 "1^2^3" 처럼 나열된다는 전제로 같은 인덱스끼리 묶습니다.
        //== 개수가 다르면 가장 긴 쪽을 기준으로 하고 짧은 쪽은 빈 값으로 채웁니다.
        private static object BuildClusterList(
            PendingCluster cluster, Type subType, IReadOnlyList<string> row, string listSeparator, TableParseReport report)
        {
            IList result = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(subType));

            string separator = string.IsNullOrEmpty(listSeparator) ? DefaultListSeparator : listSeparator;
            string[] separators = new string[] { separator };
            string[][] partsByMember = new string[cluster.Members.Count][];
            int maxCount = 0;

            for (int i = 0; i < cluster.Members.Count; i++)
            {
                string cell = TableValueParser.CellAt(row, cluster.Members[i].ColumnIndex);
                partsByMember[i] = string.IsNullOrEmpty(cell)
                    ? Array.Empty<string>()
                    : cell.Split(separators, StringSplitOptions.None);

                if (partsByMember[i].Length > maxCount)
                {
                    maxCount = partsByMember[i].Length;
                }
            }

            for (int index = 0; index < maxCount; index++)
            {
                object instance = Activator.CreateInstance(subType);
                for (int mi = 0; mi < cluster.Members.Count; mi++)
                {
                    PendingClusterMember member = cluster.Members[mi];
                    FieldInfo fieldInfo = subType.GetField(member.SubFieldName);
                    if (fieldInfo == null)
                    {
                        continue;
                    }

                    string part = index < partsByMember[mi].Length ? partsByMember[mi][index] : string.Empty;
                    fieldInfo.SetValue(instance, TableValueParser.ParseCell(
                        part, fieldInfo.FieldType, listSeparator, report, cluster.FieldName + member.SubFieldName));
                }

                result.Add(instance);
            }

            return result;
        }

        private static void ReportInjectResult(int injected, int total)
        {
            if (injected == 0)
            {
                Log.Error($"[TableGen] 데이터를 하나도 주입하지 못했습니다(테이블 {total}개). 컴파일 오류를 고친 뒤 다시 생성하세요. 받아 둔 데이터는 지웠습니다.", LogColor.Red);
                return;
            }

            if (injected < total)
            {
                Log.Warning($"[TableGen] 데이터 주입 {injected} / {total}개 성공. 실패한 테이블은 위 로그를 확인하세요.", LogColor.Yellow);
                return;
            }

            Log.Info($"[TableGen] 데이터 주입 완료 ({injected}개 테이블).");
        }

        #endregion

        #region Private Helpers - 주입 예약

        //== delayCall 은 호출된 뒤 초기화되는 방식이라 자기 안에서 다시 등록하면 버전에 따라 유실됩니다.
        //== 끝날 때 직접 구독을 떼는 update 폴링을 씁니다.
        private static void ScheduleImport()
        {
            EditorApplication.update -= WaitForCompileThenImport;
            EditorApplication.update += WaitForCompileThenImport;
        }

        private static void WaitForCompileThenImport()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return;
            }

            EditorApplication.update -= WaitForCompileThenImport;
            ImportPendingData();
        }

        [DidReloadScripts]
        private static void OnScriptsReloaded()
        {
            //== 도메인 리로드로 위 구독이 날아가므로 보류 파일이 남아 있으면 다시 예약합니다.
            if (File.Exists(_pendingPath))
            {
                ScheduleImport();
            }
        }

        #endregion

        #region Private Helpers - 설정 조회

        private static string ResolveBaseClass(TableGenSettings settings, string sheetName)
        {
            if (settings == null || settings.SheetBaseClasses == null)
            {
                return null;
            }

            for (int i = 0; i < settings.SheetBaseClasses.Count; i++)
            {
                TableGenSettings.SheetBaseClass map = settings.SheetBaseClasses[i];
                if (map == null || !map.Enabled)
                {
                    continue;
                }

                if (map.SheetName != sheetName || string.IsNullOrEmpty(map.BaseClassFullName))
                {
                    continue;
                }

                return map.BaseClassFullName;
            }

            return null;
        }

        //== 아직 컴파일되지 않은 베이스면 null 을 돌려주고 그 회차는 필드를 다 선언합니다. 다음 회차에 정리됩니다.
        private static HashSet<string> CollectInheritedFieldNames(string baseClassFullName)
        {
            if (string.IsNullOrEmpty(baseClassFullName))
            {
                return null;
            }

            Type baseType = TableReflection.FindType(baseClassFullName);
            if (baseType == null)
            {
                Log.Warning($"[TableGen] 베이스 클래스를 찾지 못했습니다(컴파일이 필요합니다): {baseClassFullName}", LogColor.Yellow);
                return null;
            }

            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            FieldInfo[] fields = baseType.GetFields(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < fields.Length; i++)
            {
                names.Add(fields[i].Name);
            }

            return names;
        }

        private static bool IsDontAutoClass(TableGenSettings settings, string sheetName)
        {
            if (settings == null || settings.DontAutoClass == null)
            {
                return false;
            }

            for (int i = 0; i < settings.DontAutoClass.Count; i++)
            {
                if (settings.DontAutoClass[i] == sheetName)
                {
                    return true;
                }
            }

            return false;
        }

        private static HashSet<string> CollectExternalEnumNames(TableGenSettings settings)
        {
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            if (settings == null || settings.EnumExternalMaps == null)
            {
                return names;
            }

            for (int i = 0; i < settings.EnumExternalMaps.Count; i++)
            {
                TableGenSettings.EnumExternalMap map = settings.EnumExternalMaps[i];
                if (map == null || !map.Enabled)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(map.TypeName) || string.IsNullOrEmpty(map.FullTypeName))
                {
                    continue;
                }

                names.Add(map.TypeName);
            }

            return names;
        }

        //== 별칭을 안 써도 필드명이 외부 매핑에 있으면 그 타입을 쓰게 이름을 바꿔 둡니다.
        private static void RewriteEnumTypeNamesByExternalMap(TableGenSettings settings, List<SheetData> sheets)
        {
            HashSet<string> mappedNames = CollectExternalEnumNames(settings);
            if (mappedNames.Count == 0)
            {
                return;
            }

            for (int si = 0; si < sheets.Count; si++)
            {
                List<FieldMeta> fields = sheets[si].AllFields;
                if (fields == null)
                {
                    continue;
                }

                for (int fi = 0; fi < fields.Count; fi++)
                {
                    FieldMeta field = fields[fi];

                    //== 별칭을 직접 적었다면 그 이름이 이미 의도한 타입명입니다.
                    if (!field.Type.IsEnum || field.HasEnumAlias)
                    {
                        continue;
                    }

                    if (mappedNames.Contains(field.Name))
                    {
                        field.EnumTypeName = field.Name;
                    }
                }
            }
        }

        private static string ResolveMasterAssetFolder(TableGenSettings settings)
        {
            return string.IsNullOrEmpty(settings.MasterTableAssetPath)
                ? settings.MasterTableOutputPath
                : settings.MasterTableAssetPath;
        }

        #endregion

        #region Private Helpers - 파일 / 에셋

        //== 내용이 같으면 쓰지 않습니다. 파일 시각만 바뀌어도 Unity 가 전체 재컴파일을 돕니다.
        private static void WriteIfChanged(string path, string content, List<string> writtenPaths)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(path) && File.ReadAllText(path) == content)
            {
                return;
            }

            File.WriteAllText(path, content, new UTF8Encoding(false));
            if (writtenPaths != null)
            {
                writtenPaths.Add(path.Replace('\\', '/'));
            }
        }

        //== 새로 만든 폴더와 파일을 AssetDatabase 에 알립니다. 건너뛰면 방금 만든 폴더를 모르는 상태로 남습니다.
        private static void ImportWrittenAssets(List<string> writtenPaths)
        {
            AssetDatabase.Refresh();
            for (int i = 0; i < writtenPaths.Count; i++)
            {
                if (writtenPaths[i].StartsWith("Assets/"))
                {
                    AssetDatabase.ImportAsset(writtenPaths[i], ImportAssetOptions.ForceUpdate);
                }
            }
        }

        private static void SavePending(PendingBatch batch)
        {
            string directory = Path.GetDirectoryName(_pendingPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_pendingPath, JsonUtility.ToJson(batch));
        }

        private static void ClearPending()
        {
            if (File.Exists(_pendingPath))
            {
                File.Delete(_pendingPath);
            }
        }

        private static MasterTableBase LoadOrCreateAsset(Type masterType, string assetPath)
        {
            //== 생성된 MasterTable 클래스는 항상 MasterTableSO 를 상속합니다. 여기서 걸리면 설정이나 생성 코드가 어긋난 것입니다.
            if (!typeof(MasterTableBase).IsAssignableFrom(masterType))
            {
                Log.Error($"[TableGen] {masterType.FullName} 이 MasterTableBase 를 상속하지 않습니다.", LogColor.Red);
                return null;
            }

            MasterTableBase existing = AssetDatabase.LoadAssetAtPath(assetPath, masterType) as MasterTableBase;
            if (existing != null)
            {
                return existing;
            }

            if (!EnsureAssetFolder(Path.GetDirectoryName(assetPath)))
            {
                Log.Error($"[TableGen] 에셋 폴더를 만들지 못했습니다: {assetPath}", LogColor.Red);
                return null;
            }

            MasterTableBase created = ScriptableObject.CreateInstance(masterType) as MasterTableBase;
            if (created == null)
            {
                Log.Error($"[TableGen] 에셋 생성 실패: {assetPath}", LogColor.Red);
                return null;
            }

            AssetDatabase.CreateAsset(created, assetPath);
            return created;
        }

        //== Directory.CreateDirectory 만 쓰면 Refresh 전까지 AssetDatabase 가 폴더를 몰라 같은 프레임의 CreateAsset 이 실패합니다.
        private static bool EnsureAssetFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder))
            {
                return false;
            }

            string normalized = folder.Replace('\\', '/');
            if (!normalized.StartsWith("Assets"))
            {
                Directory.CreateDirectory(normalized);
                return true;
            }

            if (AssetDatabase.IsValidFolder(normalized))
            {
                return true;
            }

            string[] segments = normalized.Split('/');
            string current = segments[0];
            for (int i = 1; i < segments.Length; i++)
            {
                if (string.IsNullOrEmpty(segments[i]))
                {
                    continue;
                }

                string next = $"{current}/{segments[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, segments[i]);
                }

                current = next;
            }

            return AssetDatabase.IsValidFolder(normalized);
        }

        #endregion

        #region Private Helpers - 그 외

        private static void BackupCsv(TableGenSettings settings, string sheetName, List<List<string>> rows)
        {
            if (!settings.EnableCsvBackup)
            {
                return;
            }

            //== 백업은 생성 결과에 영향을 주지 않으므로 실패해도 파이프라인을 멈추지 않습니다.
            try
            {
                TableCsvBackup.Save(sheetName, rows, settings.CsvBackupPath);
            }
            catch (Exception e)
            {
                Log.Warning($"[TableGen] '{sheetName}' CSV 백업 실패: {e.Message}", LogColor.Yellow);
            }
        }

        private static List<List<string>> ExtractDataRows(List<List<string>> allRows, int dataStartIndex)
        {
            List<List<string>> result = new List<List<string>>();
            for (int r = dataStartIndex; r < allRows.Count; r++)
            {
                result.Add(allRows[r]);
            }

            return result;
        }

        private static List<string> SplitListValues(List<string> raw, string listSeparator)
        {
            string separator = string.IsNullOrEmpty(listSeparator) ? DefaultListSeparator : listSeparator;
            string[] separators = new string[] { separator };

            List<string> result = new List<string>(raw.Count);
            for (int i = 0; i < raw.Count; i++)
            {
                string cell = raw[i];
                if (string.IsNullOrEmpty(cell))
                {
                    continue;
                }

                string[] parts = cell.Split(separators, StringSplitOptions.None);
                for (int j = 0; j < parts.Length; j++)
                {
                    string part = parts[j].Trim();
                    if (part.Length > 0)
                    {
                        result.Add(part);
                    }
                }
            }

            return result;
        }

        private static void InvokeSafe(Action action)
        {
            if (action != null)
            {
                action();
            }
        }

        #endregion
    }
}
