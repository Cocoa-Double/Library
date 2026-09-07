using System;
using System.Collections.Generic;
using System.Text;

using Cocoa.Lib.Util;

namespace Cocoa.Lib.Editor.MasterTable
{
    /// <summary>
    /// 한 테이블을 주입하는 동안 생긴 값 변환 실패를 모아 두고, 끝에 컬럼별로 한 줄씩 정리해 내보냅니다.
    /// </summary>
    /// <remarks>
    /// 셀마다 로그를 남기면 컬럼 하나가 서식 문제로 전부 실패할 때 수천 줄이 쏟아져 정작 봐야 할 로그가 밀려 나갑니다.
    /// 시트를 고치는 데 필요한 것은 어느 컬럼에서 몇 건이 실패했고 그 값이 어떻게 생겼는지뿐입니다.
    /// </remarks>
    public sealed class TableParseReport
    {
        #region Nested Types

        private sealed class Entry
        {
            public string FieldName;
            public string Reason;
            public int Count;
            public List<string> Samples = new List<string>();
        }

        #endregion

        #region Constants

        //== 전부 나열하면 결국 셀마다 찍는 것과 같아집니다.
        private const int MaxSamples = 3;

        #endregion

        #region Fields

        //== "컬럼|사유" -> 누적 항목
        private readonly Dictionary<string, Entry> _entryByKey = new Dictionary<string, Entry>(StringComparer.Ordinal);

        //== 보고 순서를 만난 순서대로 유지하기 위한 목록입니다.
        private readonly List<Entry> _entries = new List<Entry>();

        #endregion

        #region Public API

        /// <summary>변환에 실패한 셀 하나를 기록합니다.</summary>
        public void Add(string fieldName, string reason, string rawValue)
        {
            string field = string.IsNullOrEmpty(fieldName) ? "(알 수 없는 컬럼)" : fieldName;
            string key = $"{field}|{reason}";

            if (!_entryByKey.TryGetValue(key, out Entry entry))
            {
                entry = new Entry { FieldName = field, Reason = reason };
                _entryByKey.Add(key, entry);
                _entries.Add(entry);
            }

            entry.Count++;
            if (entry.Samples.Count < MaxSamples && !string.IsNullOrEmpty(rawValue) && !entry.Samples.Contains(rawValue))
            {
                entry.Samples.Add(rawValue);
            }
        }

        /// <summary>모아 둔 실패를 항목별로 한 줄씩 내보내고 비웁니다.</summary>
        public void Flush(string tableName)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry entry = _entries[i];
                Log.Warning($"[TableGen] '{tableName}' 의 {entry.FieldName} 컬럼에서 {entry.Reason} 값 {entry.Count}개를 기본값으로 채웠습니다.{BuildSamples(entry)}", LogColor.Yellow);
            }

            _entryByKey.Clear();
            _entries.Clear();
        }

        #endregion

        #region Private Helpers

        private static string BuildSamples(Entry entry)
        {
            if (entry.Samples.Count == 0)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(" 예: ");
            for (int i = 0; i < entry.Samples.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                builder.Append('"').Append(entry.Samples[i]).Append('"');
            }

            return builder.ToString();
        }

        #endregion
    }
}
