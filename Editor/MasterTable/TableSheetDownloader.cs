using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine.Networking;

namespace Cocoa.Lib.Editor.MasterTable
{
    /// <summary>
    /// Google Sheets API v4 의 values.get 으로 한 탭의 전체 값을 받아 행 목록으로 돌려줍니다.
    /// </summary>
    /// <remarks>
    /// 응답의 values 는 행과 열의 배열이지만 후행 빈 셀이 생략되어 행마다 길이가 다릅니다.
    /// 값을 꺼낼 때는 <see cref="TableValueParser.CellAt"/> 로 범위를 확인해야 합니다.
    ///
    /// valueRenderOption 은 기본값(FORMATTED_VALUE)을 그대로 씁니다.
    /// 셀 서식이 적용된 문자열이 오므로 천 단위 서식이 걸린 1000 은 "1,000" 으로 넘어오지만, 그 처리는 TableValueParser 가 합니다.
    /// UNFORMATTED_VALUE 로 바꾸면 숫자는 깔끔해지는 대신 dateTimeRenderOption 이 살아나 날짜 서식 셀이
    /// 문자열이 아니라 일련번호(2026-01-01 이 46023)로 넘어옵니다.
    ///
    /// 위 내용은 2026-09 기준으로 아래 문서를 확인해 정리했습니다.
    /// https://developers.google.com/workspace/sheets/api/reference/rest/v4/spreadsheets.values/get
    /// </remarks>
    public static class TableSheetDownloader
    {
        #region Nested Types

        public sealed class Result
        {
            public bool Success;
            public string Error;
            public List<List<string>> Rows;

            public static Result Ok(List<List<string>> rows)
            {
                return new Result { Success = true, Rows = rows };
            }

            public static Result Fail(string error)
            {
                return new Result { Success = false, Error = error, Rows = null };
            }
        }

        #endregion

        #region Public API

        /// <summary>한 탭의 전체 값을 받습니다. 완료 시 onComplete 를 호출합니다.</summary>
        public static void DownloadAsync(string spreadsheetId, string sheetName, string apiKey, Action<Result> onComplete)
        {
            if (onComplete == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(spreadsheetId) || string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(sheetName))
            {
                onComplete(Result.Fail("SpreadsheetId / ApiKey / SheetName 중 비어 있는 값이 있습니다."));
                return;
            }

            //== range 에 탭 이름만 넣으면 그 탭 전체가 옵니다. 이름에 공백이나 느낌표가 있으면 A1 표기로 오해받아 홑따옴표로 감쌉니다.
            string quotedName = sheetName.Replace("'", "''");
            string range = UnityWebRequest.EscapeURL($"'{quotedName}'");
            string escapedKey = UnityWebRequest.EscapeURL(apiKey);
            string url = $"https://sheets.googleapis.com/v4/spreadsheets/{spreadsheetId}/values/{range}?key={escapedKey}";

            TableWebRequest.Get(url, response =>
            {
                if (!response.Success)
                {
                    onComplete(Result.Fail($"HTTP {response.Code}: {response.Error}\n{response.Text}"));
                    return;
                }

                try
                {
                    onComplete(Result.Ok(ExtractRows(response.Text)));
                }
                catch (Exception e)
                {
                    onComplete(Result.Fail($"JSON 파싱 실패: {e.Message}"));
                }
            });
        }

        #endregion

        #region Private Helpers

        private static List<List<string>> ExtractRows(string json)
        {
            List<List<string>> rows = new List<List<string>>();

            Dictionary<string, object> root = TableJson.Parse(json) as Dictionary<string, object>;
            if (root == null || !root.TryGetValue("values", out object valuesValue))
            {
                return rows;
            }

            List<object> values = valuesValue as List<object>;
            if (values == null)
            {
                return rows;
            }

            for (int r = 0; r < values.Count; r++)
            {
                List<object> cells = values[r] as List<object>;
                List<string> row = new List<string>(cells != null ? cells.Count : 0);
                if (cells != null)
                {
                    for (int c = 0; c < cells.Count; c++)
                    {
                        row.Add(CellToString(cells[c]));
                    }
                }

                rows.Add(row);
            }

            return rows;
        }

        private static string CellToString(object cell)
        {
            if (cell == null)
            {
                return string.Empty;
            }

            string text = cell as string;
            if (text != null)
            {
                return text;
            }

            if (cell is double)
            {
                //== "R" 은 왕복 변환을 보장합니다. "G" 를 쓰면 큰 값이 지수 표기로 바뀌어 파싱이 깨집니다.
                return ((double)cell).ToString("R", CultureInfo.InvariantCulture);
            }

            if (cell is bool)
            {
                return (bool)cell ? "true" : "false";
            }

            return cell.ToString();
        }

        #endregion
    }
}
