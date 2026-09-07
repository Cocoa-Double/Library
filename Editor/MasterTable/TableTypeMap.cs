using System.Collections.Generic;

namespace Cocoa.Lib.Editor.MasterTable
{
    /// <summary>
    /// 시트 헤더 셀의 접두사를 C# 타입과 파싱 규칙으로 해석합니다.
    /// </summary>
    /// <remarks>
    /// 접두사 표를 이 클래스 하나만 들고 있게 해서 헤더 파서와 코드 생성기가 서로 다른 해석을 하지 않도록 합니다.
    /// 단일 값은 n_=int, s_=string, d_=double, f_=float, l_=long, b_=bool, e_=enum 이고,
    /// ns_ / ss_ / ds_ / fs_ / ls_ / bs_ / es_ 는 각각의 List 버전입니다.
    /// enum 은 그 컬럼에 등장한 문자열 고유값으로 별도 타입을 생성합니다.
    /// 필드명 뒤에 '#별칭' 을 붙이면 그 별칭이 타입명이 되고, 같은 별칭을 쓴 컬럼은 시트가 달라도 한 타입으로 합쳐집니다.
    /// "e_Grade#Common" 은 필드명 Grade 에 타입명 Common, "e_Grade" 는 필드명 Grade 에 타입명 {시트명}Grade 입니다.
    /// </remarks>
    public static class TableTypeMap
    {
        #region Nested Types

        /// <summary>접두사 하나에 대응하는 타입 정보입니다.</summary>
        public readonly struct TypeInfo
        {
            /// <summary>요소의 C# 타입 키워드. enum 이면 타입명이 나중에 정해지므로 빈 문자열입니다.</summary>
            public readonly string ElementType;

            /// <summary>List 접두사인지 여부.</summary>
            public readonly bool IsList;

            /// <summary>enum 생성 대상인지 여부.</summary>
            public readonly bool IsEnum;

            public TypeInfo(string elementType, bool isList, bool isEnum)
            {
                ElementType = elementType;
                IsList = isList;
                IsEnum = isEnum;
            }
        }

        /// <summary>헤더 셀 하나를 해석한 결과입니다.</summary>
        public readonly struct HeaderParseResult
        {
            public readonly TypeInfo Type;

            /// <summary>접두사와 '#별칭' 을 떼어 낸 순수 필드명.</summary>
            public readonly string FieldName;

            /// <summary>enum 컬럼에 '#별칭' 이 붙어 있으면 그 별칭, 없으면 null.</summary>
            public readonly string EnumAlias;

            public HeaderParseResult(TypeInfo type, string fieldName, string enumAlias)
            {
                Type = type;
                FieldName = fieldName;
                EnumAlias = enumAlias;
            }
        }

        #endregion

        #region Constants

        public const char EnumAliasSeparator = '#';

        private const int ListPrefixLength = 3;
        private const int ScalarPrefixLength = 2;

        #endregion

        #region Static Fields

        //== 접두사 -> 타입 정보
        private static readonly Dictionary<string, TypeInfo> _typeByPrefix = new Dictionary<string, TypeInfo>
        {
            { "n_",  new TypeInfo("int",    false, false) },
            { "s_",  new TypeInfo("string", false, false) },
            { "d_",  new TypeInfo("double", false, false) },
            { "f_",  new TypeInfo("float",  false, false) },
            { "l_",  new TypeInfo("long",   false, false) },
            { "b_",  new TypeInfo("bool",   false, false) },
            { "e_",  new TypeInfo("",       false, true)  },

            { "ns_", new TypeInfo("int",    true,  false) },
            { "ss_", new TypeInfo("string", true,  false) },
            { "ds_", new TypeInfo("double", true,  false) },
            { "fs_", new TypeInfo("float",  true,  false) },
            { "ls_", new TypeInfo("long",   true,  false) },
            { "bs_", new TypeInfo("bool",   true,  false) },
            { "es_", new TypeInfo("",       true,  true)  }
        };

        #endregion

        #region Public API

        /// <summary>
        /// 헤더 셀을 해석합니다. 유효한 접두사가 있으면 true 와 결과를, 없으면 주석 컬럼으로 보고 false 를 반환합니다.
        /// </summary>
        public static bool TryParse(string headerCell, out HeaderParseResult result)
        {
            result = default;
            if (string.IsNullOrEmpty(headerCell))
            {
                return false;
            }

            //== "ns_" 가 "n_" 으로 잘못 걸리지 않도록 긴 접두사를 먼저 봅니다.
            TypeInfo info;
            string body;
            if (headerCell.Length > ListPrefixLength &&
                _typeByPrefix.TryGetValue(headerCell.Substring(0, ListPrefixLength), out info))
            {
                body = headerCell.Substring(ListPrefixLength);
            }
            else if (headerCell.Length > ScalarPrefixLength &&
                     _typeByPrefix.TryGetValue(headerCell.Substring(0, ScalarPrefixLength), out info))
            {
                body = headerCell.Substring(ScalarPrefixLength);
            }
            else
            {
                return false;
            }

            //== enum 이 아닌 타입에 별칭이 붙어 있으면 별칭은 버리고 필드명만 앞부분으로 씁니다.
            string fieldName;
            string alias = null;
            int separatorIndex = body.IndexOf(EnumAliasSeparator);
            if (separatorIndex > 0 && separatorIndex < body.Length - 1)
            {
                fieldName = body.Substring(0, separatorIndex);
                if (info.IsEnum)
                {
                    alias = body.Substring(separatorIndex + 1);
                }
            }
            else
            {
                fieldName = body;
            }

            if (string.IsNullOrEmpty(fieldName))
            {
                return false;
            }

            result = new HeaderParseResult(info, fieldName, alias);
            return true;
        }

        /// <summary>선언에 쓸 C# 타입 문자열을 만듭니다. enum 이면 넘겨받은 enumTypeName 을 그대로 씁니다.</summary>
        public static string ToDeclaration(in TypeInfo info, string enumTypeName)
        {
            string element = info.IsEnum ? enumTypeName : info.ElementType;
            return info.IsList ? $"List<{element}>" : element;
        }

        #endregion
    }
}
