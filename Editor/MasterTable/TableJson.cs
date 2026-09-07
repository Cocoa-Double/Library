using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Cocoa.Lib.Editor.MasterTable
{
    /// <summary>
    /// 외부 라이브러리 없이 쓰는 최소 JSON 리더입니다.
    /// object / array / string / number / bool / null 을 각각
    /// Dictionary{string,object} / List{object} / string / double / bool / null 로 읽습니다.
    /// </summary>
    /// <remarks>
    /// Google Sheets 응답만 읽으면 되므로 JSON 스펙을 완전히 구현하지는 않습니다.
    /// JsonUtility 를 쓰지 않는 이유는 응답이 문자열 2차원 배열이라 대응할 직렬화 클래스를 만들 수 없기 때문입니다.
    /// </remarks>
    public static class TableJson
    {
        #region Public API

        public static object Parse(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            int index = 0;
            return ParseValue(json, ref index);
        }

        #endregion

        #region Private Helpers

        private static object ParseValue(string s, ref int i)
        {
            SkipWhitespace(s, ref i);
            if (i >= s.Length)
            {
                return null;
            }

            switch (s[i])
            {
                case '{':
                {
                    return ParseObject(s, ref i);
                }
                case '[':
                {
                    return ParseArray(s, ref i);
                }
                case '"':
                {
                    return ParseString(s, ref i);
                }
                case 't':
                case 'f':
                {
                    return ParseBool(s, ref i);
                }
                case 'n':
                {
                    i += 4;
                    return null;
                }
                default:
                {
                    return ParseNumber(s, ref i);
                }
            }
        }

        private static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            Dictionary<string, object> dict = new Dictionary<string, object>();
            i++;

            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == '}')
            {
                i++;
                return dict;
            }

            while (i < s.Length)
            {
                SkipWhitespace(s, ref i);

                //== 키가 문자열로 시작하지 않으면 우리가 읽을 수 있는 응답이 아닙니다.
                if (i >= s.Length || s[i] != '"')
                {
                    break;
                }

                string key = ParseString(s, ref i);
                SkipWhitespace(s, ref i);
                if (i < s.Length && s[i] == ':')
                {
                    i++;
                }

                dict[key] = ParseValue(s, ref i);
                SkipWhitespace(s, ref i);

                if (i < s.Length && s[i] == ',')
                {
                    i++;
                    continue;
                }

                if (i < s.Length && s[i] == '}')
                {
                    i++;
                }

                break;
            }

            return dict;
        }

        private static List<object> ParseArray(string s, ref int i)
        {
            List<object> list = new List<object>();
            i++;

            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == ']')
            {
                i++;
                return list;
            }

            while (i < s.Length)
            {
                int before = i;
                list.Add(ParseValue(s, ref i));

                //== 한 글자도 소비하지 못하면 같은 위치를 영원히 다시 읽어 에디터가 멈춥니다.
                if (i == before)
                {
                    break;
                }

                SkipWhitespace(s, ref i);
                if (i < s.Length && s[i] == ',')
                {
                    i++;
                    continue;
                }

                if (i < s.Length && s[i] == ']')
                {
                    i++;
                }

                break;
            }

            return list;
        }

        private static string ParseString(string s, ref int i)
        {
            StringBuilder builder = new StringBuilder();
            i++;

            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"')
                {
                    break;
                }

                if (c != '\\' || i >= s.Length)
                {
                    builder.Append(c);
                    continue;
                }

                char escaped = s[i++];
                switch (escaped)
                {
                    case '"':  builder.Append('"');  break;
                    case '\\': builder.Append('\\'); break;
                    case '/':  builder.Append('/');  break;
                    case 'n':  builder.Append('\n'); break;
                    case 't':  builder.Append('\t'); break;
                    case 'r':  builder.Append('\r'); break;
                    case 'b':  builder.Append('\b'); break;
                    case 'f':  builder.Append('\f'); break;
                    case 'u':
                    {
                        AppendUnicode(s, ref i, builder);
                        break;
                    }
                    default:
                    {
                        builder.Append(escaped);
                        break;
                    }
                }
            }

            return builder.ToString();
        }

        private static void AppendUnicode(string s, ref int i, StringBuilder builder)
        {
            if (i + 4 > s.Length)
            {
                return;
            }

            if (int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
            {
                builder.Append((char)code);
            }

            i += 4;
        }

        private static object ParseBool(string s, ref int i)
        {
            if (i < s.Length && s[i] == 't')
            {
                i += 4;
                return true;
            }

            i += 5;
            return false;
        }

        private static object ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0)
            {
                i++;
            }

            //== 숫자로 시작하지 않는 문자입니다. 소비하지 않고 돌아가면 호출한 루프가 제자리를 돕니다.
            if (i == start)
            {
                i++;
                return null;
            }

            string number = s.Substring(start, i - start);
            return double.TryParse(number, NumberStyles.Any, CultureInfo.InvariantCulture, out double value)
                ? (object)value
                : number;
        }

        private static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i]))
            {
                i++;
            }
        }

        #endregion
    }
}
