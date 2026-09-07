using System;
using System.Collections.Generic;

using Cocoa.Lib.Util;

namespace Cocoa.Lib.Editor.MasterTable
{
    /// <summary>
    /// 시트의 헤더 행을 읽어 필드 컬럼만 골라 메타데이터로 분해합니다.
    /// 접두사가 없는 셀은 기획자용 주석으로 보고 버리지만, 값을 뽑을 때 필요한 컬럼 인덱스는 남깁니다.
    /// </summary>
    public static class TableHeaderParser
    {
        #region Nested Types

        /// <summary>필드 컬럼 하나의 메타데이터입니다.</summary>
        public sealed class FieldMeta
        {
            /// <summary>시트에서의 0-based 컬럼 인덱스.</summary>
            public int ColumnIndex;

            /// <summary>접두사와 '#별칭' 을 떼어 낸 필드명.</summary>
            public string Name;

            /// <summary>요소 타입, List 여부, enum 여부.</summary>
            public TableTypeMap.TypeInfo Type;

            /// <summary>생성될 enum 타입명. enum 컬럼일 때만 채워집니다.</summary>
            public string EnumTypeName;

            /// <summary>'#' 별칭으로 타입명을 직접 지정했는지 여부.</summary>
            public bool HasEnumAlias;
        }

        #endregion

        #region Public API

        /// <summary>헤더 행을 파싱합니다. sheetName 은 별칭 없는 enum 의 타입명을 만들 때 접두로 쓰입니다.</summary>
        public static List<FieldMeta> Parse(IReadOnlyList<string> headerCells, string sheetName)
        {
            List<FieldMeta> fields = new List<FieldMeta>();
            if (headerCells == null)
            {
                return fields;
            }

            //== 같은 필드명이 두 컬럼에 있으면 필드가 중복 선언된 클래스가 나와 컴파일이 깨집니다.
            HashSet<string> usedNames = new HashSet<string>(StringComparer.Ordinal);

            for (int col = 0; col < headerCells.Count; col++)
            {
                string cell = headerCells[col] != null ? headerCells[col].Trim() : null;
                if (!TableTypeMap.TryParse(cell, out TableTypeMap.HeaderParseResult parsed))
                {
                    continue;
                }

                if (!usedNames.Add(parsed.FieldName))
                {
                    Log.Warning($"[TableGen] '{sheetName}' 의 {ToColumnLabel(col)}열 필드명 '{parsed.FieldName}' 이 앞 컬럼과 겹칩니다. 이 컬럼은 무시합니다.", LogColor.Yellow);
                    continue;
                }

                string enumTypeName = null;
                bool hasAlias = false;
                if (parsed.Type.IsEnum)
                {
                    hasAlias = !string.IsNullOrEmpty(parsed.EnumAlias);
                    enumTypeName = hasAlias ? parsed.EnumAlias : sheetName + parsed.FieldName;
                }

                fields.Add(new FieldMeta
                {
                    ColumnIndex = col,
                    Name = parsed.FieldName,
                    Type = parsed.Type,
                    EnumTypeName = enumTypeName,
                    HasEnumAlias = hasAlias
                });
            }

            return fields;
        }

        #endregion

        #region Private Helpers

        //== 경고를 보고 시트에서 바로 그 컬럼을 찾을 수 있어야 하므로 보이는 열 이름(A, B, ... AA)으로 바꿉니다.
        private static string ToColumnLabel(int columnIndex)
        {
            if (columnIndex < 0)
            {
                return "?";
            }

            string label = string.Empty;
            int value = columnIndex;
            while (true)
            {
                label = (char)('A' + value % 26) + label;
                value = value / 26 - 1;
                if (value < 0)
                {
                    break;
                }
            }

            return label;
        }

        #endregion
    }
}
