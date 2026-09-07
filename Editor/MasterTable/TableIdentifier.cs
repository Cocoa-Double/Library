using System.Collections.Generic;
using System.Text;

namespace Cocoa.Lib.Editor.MasterTable
{
    /// <summary>
    /// 시트에 적힌 임의의 문자열을 C# 식별자로 정제합니다.
    /// </summary>
    /// <remarks>
    /// enum 멤버명을 만드는 코드 생성 단계와 셀 값을 그 멤버로 되돌리는 값 파싱 단계가 반드시 같은 규칙을 써야 하므로
    /// 정제 규칙을 이 클래스 하나에만 둡니다.
    /// </remarks>
    public static class TableIdentifier
    {
        #region Static Fields

        //== 정제 결과가 예약어와 겹치면 컴파일이 깨집니다. 시트에 실제로 등장할 수 있는 것만 담았습니다.
        private static readonly HashSet<string> _reservedWords = new HashSet<string>
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
            "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
            "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
            "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
            "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
            "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed",
            "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw",
            "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using",
            "virtual", "void", "volatile", "while"
        };

        #endregion

        #region Public API

        /// <summary>
        /// 영숫자와 '_' 외의 문자를 '_' 로 바꾸고, 숫자로 시작하거나 예약어와 겹치면 '_' 를 앞에 붙입니다.
        /// 빈 문자열은 "_" 가 됩니다.
        /// </summary>
        public static string Sanitize(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return "_";
            }

            StringBuilder builder = new StringBuilder(raw.Length + 1);
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                builder.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
            }

            string result = builder.ToString();

            //== 숫자로 시작하는 식별자는 문법에서 허용되지 않습니다.
            if (char.IsDigit(result[0]))
            {
                return "_" + result;
            }

            //== 예약어는 모두 소문자라 "Class" 나 "Default" 는 그대로 통과합니다.
            if (_reservedWords.Contains(result))
            {
                return "_" + result;
            }

            return result;
        }

        #endregion
    }
}
