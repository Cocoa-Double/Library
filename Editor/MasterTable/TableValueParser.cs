using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

using Cocoa.Lib.Util;

namespace Cocoa.Lib.Editor.MasterTable
{
    /// <summary>
    /// 시트 셀 문자열을 대상 필드 타입의 값으로 변환합니다. enum 코드 생성을 위한 컬럼 고유값 수집도 함께 담당합니다.
    /// </summary>
    /// <remarks>
    /// 변환에 실패한 셀은 예외를 던지지 않고 타입 기본값으로 떨어집니다.
    /// 다만 조용히 0 이 되면 기획 데이터의 오타를 아무도 찾지 못하므로 실패는 반드시 알립니다.
    /// report 를 넘기면 테이블 단위로 묶어 내보내고, 넘기지 않으면 그 자리에서 바로 경고합니다.
    /// </remarks>
    public static class TableValueParser
    {
        #region Constants

        private const string DefaultListSeparator = "^";

        //== 시트의 숫자 셀은 서식 때문에 "1,000" 으로 넘어오는 경우가 있어 천 단위 구분자를 허용합니다.
        private const NumberStyles IntegerStyles = NumberStyles.Integer | NumberStyles.AllowThousands;
        private const NumberStyles RealStyles = NumberStyles.Float | NumberStyles.AllowThousands;

        #endregion

        #region Static Fields

        //== Google Sheets 는 체크박스를 TRUE/FALSE 로 주지만, 기획자가 직접 1/0 이나 O/X 로 적어 둔 컬럼이 섞여 있습니다.
        private static readonly HashSet<string> _trueLiterals = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "true", "1", "y", "yes", "o", "on"
        };

        private static readonly HashSet<string> _falseLiterals = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "false", "0", "n", "no", "x", "off"
        };

        #endregion

        #region Public API - 값 변환

        /// <summary>
        /// 셀 문자열을 fieldType 에 맞는 값으로 변환합니다.
        /// fieldType 이 List{E} 면 listSeparator 로 쪼개 List{E} 를 만듭니다.
        /// </summary>
        /// <param name="report">변환 실패를 모을 곳. null 이면 실패할 때마다 바로 경고합니다.</param>
        /// <param name="fieldName">실패 보고에 쓸 컬럼 이름.</param>
        public static object ParseCell(
            string cell, Type fieldType, string listSeparator, TableParseReport report = null, string fieldName = null)
        {
            if (fieldType == null)
            {
                return null;
            }

            if (!IsGenericList(fieldType))
            {
                return ConvertScalar(cell, fieldType, report, fieldName);
            }

            Type elementType = fieldType.GetGenericArguments()[0];
            IList list = (IList)Activator.CreateInstance(fieldType);
            if (string.IsNullOrEmpty(cell))
            {
                return list;
            }

            string separator = string.IsNullOrEmpty(listSeparator) ? DefaultListSeparator : listSeparator;
            string[] parts = cell.Split(new string[] { separator }, StringSplitOptions.None);
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();

                //== 구분자를 하나 더 넣어 "A^^B" 가 되는 실수가 흔해 빈 원소는 넣지 않습니다.
                if (part.Length == 0)
                {
                    continue;
                }

                list.Add(ConvertScalar(part, elementType, report, fieldName));
            }

            return list;
        }

        #endregion

        #region Public API - enum 고유값 수집

        /// <summary>데이터 행에서 한 컬럼의 원시 문자열을 등장 순서대로 모읍니다.</summary>
        public static List<string> CollectColumn(IReadOnlyList<IReadOnlyList<string>> dataRows, int columnIndex)
        {
            List<string> values = new List<string>();
            if (dataRows == null || columnIndex < 0)
            {
                return values;
            }

            for (int r = 0; r < dataRows.Count; r++)
            {
                IReadOnlyList<string> row = dataRows[r];

                //== 후행 빈 셀이 생략되어 행 길이가 들쭉날쭉합니다.
                if (row == null || columnIndex >= row.Count)
                {
                    continue;
                }

                string cell = row[columnIndex];
                if (!string.IsNullOrEmpty(cell))
                {
                    values.Add(cell.Trim());
                }
            }

            return values;
        }

        /// <summary>행에서 컬럼 인덱스의 셀을 읽습니다. 범위를 벗어나면 빈 문자열을 반환합니다.</summary>
        public static string CellAt(IReadOnlyList<string> row, int columnIndex)
        {
            if (row == null || columnIndex < 0 || columnIndex >= row.Count)
            {
                return string.Empty;
            }

            return row[columnIndex] ?? string.Empty;
        }

        #endregion

        #region Private Helpers

        private static bool IsGenericList(Type type)
        {
            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>);
        }

        //== 단일 값을 대상 타입으로 변환합니다. 빈 셀은 그 타입의 기본값입니다.
        private static object ConvertScalar(string raw, Type type, TableParseReport report, string fieldName)
        {
            if (type == typeof(string))
            {
                return raw ?? string.Empty;
            }

            if (string.IsNullOrEmpty(raw))
            {
                return Activator.CreateInstance(type);
            }

            string text = raw.Trim();

            if (type.IsEnum)
            {
                return ConvertEnum(text, type, report, fieldName);
            }

            if (type == typeof(int))
            {
                if (TryParseInteger(text, out long parsed))
                {
                    return (int)parsed;
                }

                ReportFailure(report, fieldName, "int 로 읽을 수 없는", text);
                return 0;
            }

            if (type == typeof(long))
            {
                if (TryParseInteger(text, out long parsed))
                {
                    return parsed;
                }

                ReportFailure(report, fieldName, "long 으로 읽을 수 없는", text);
                return 0L;
            }

            if (type == typeof(float))
            {
                if (float.TryParse(text, RealStyles, CultureInfo.InvariantCulture, out float value))
                {
                    return value;
                }

                ReportFailure(report, fieldName, "float 으로 읽을 수 없는", text);
                return 0f;
            }

            if (type == typeof(double))
            {
                if (double.TryParse(text, RealStyles, CultureInfo.InvariantCulture, out double value))
                {
                    return value;
                }

                ReportFailure(report, fieldName, "double 로 읽을 수 없는", text);
                return 0.0;
            }

            if (type == typeof(bool))
            {
                if (_trueLiterals.Contains(text))
                {
                    return true;
                }

                if (_falseLiterals.Contains(text))
                {
                    return false;
                }

                ReportFailure(report, fieldName, "bool 로 읽을 수 없는", text);
                return false;
            }

            //== 접두사 표에 없는 타입입니다. 헤더 규칙이 늘어났는데 여기가 따라오지 않은 상황입니다.
            Log.Error($"[TableGen] 지원하지 않는 필드 타입입니다: {type.Name} ({fieldName} 컬럼)", LogColor.Red);
            return Activator.CreateInstance(type);
        }

        private static object ConvertEnum(string text, Type enumType, TableParseReport report, string fieldName)
        {
            string member = TableIdentifier.Sanitize(text);

            //== 정의되지 않은 값이 섞이는 건 흔한 일인데 Enum.Parse 는 그때마다 예외를 던집니다.
            if (Enum.IsDefined(enumType, member))
            {
                return Enum.Parse(enumType, member, false);
            }

            ReportFailure(report, fieldName, $"enum {enumType.Name} 에 없는", text);
            return Activator.CreateInstance(enumType);
        }

        //== 정수 파싱만 시도하면 "3.0" 같은 값이 전부 0 이 되므로 실수로도 한 번 읽어 잘라 냅니다.
        private static bool TryParseInteger(string text, out long value)
        {
            if (long.TryParse(text, IntegerStyles, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }

            if (double.TryParse(text, RealStyles, CultureInfo.InvariantCulture, out double asDouble))
            {
                value = (long)asDouble;
                return true;
            }

            value = 0;
            return false;
        }

        private static void ReportFailure(TableParseReport report, string fieldName, string reason, string text)
        {
            if (report != null)
            {
                report.Add(fieldName, reason, text);
                return;
            }

            Log.Warning($"[TableGen] {fieldName} 컬럼의 {reason} 값 '{text}' 을 기본값으로 채웠습니다.", LogColor.Yellow);
        }

        #endregion
    }
}
