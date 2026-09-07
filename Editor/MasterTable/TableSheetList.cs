using System;
using System.Collections.Generic;
using System.Globalization;

namespace Cocoa.Lib.Editor.MasterTable
{
    /// <summary>
    /// 스프레드시트의 탭 목록을 받아옵니다. 주소창에서 복사해 온 URL 에서 스프레드시트 ID 를 뽑는 일도 함께 합니다.
    /// </summary>
    public static class TableSheetList
    {
        #region Nested Types

        public sealed class SheetInfo
        {
            public string Title;
            public string Gid;
        }

        public sealed class Result
        {
            public bool Success;
            public string Error;
            public List<SheetInfo> Sheets;

            public static Result Ok(List<SheetInfo> sheets)
            {
                return new Result { Success = true, Sheets = sheets };
            }

            public static Result Fail(string error)
            {
                return new Result { Success = false, Error = error, Sheets = null };
            }
        }

        #endregion

        #region Constants

        private const string IdMarker = "/d/";

        #endregion

        #region Static Fields

        //== ".../d/{ID}?usp=sharing" 처럼 슬래시 없이 끝나는 주소가 흔합니다.
        private static readonly char[] _idTerminators = { '/', '?', '#', '&' };

        #endregion

        #region Public API

        /// <summary>주소 또는 ID 에서 스프레드시트 ID 만 뽑습니다. "/d/" 가 없으면 이미 ID 를 넣은 것으로 봅니다.</summary>
        public static string ExtractSpreadsheetId(string urlOrId)
        {
            if (string.IsNullOrEmpty(urlOrId))
            {
                return urlOrId;
            }

            int start = urlOrId.IndexOf(IdMarker, StringComparison.Ordinal);
            if (start < 0)
            {
                return urlOrId.Trim();
            }

            start += IdMarker.Length;
            int end = urlOrId.IndexOfAny(_idTerminators, start);
            if (end < 0)
            {
                end = urlOrId.Length;
            }

            return urlOrId.Substring(start, end - start);
        }

        /// <summary>탭 목록(제목과 gid)을 받아옵니다.</summary>
        public static void FetchAsync(string spreadsheetId, string apiKey, Action<Result> onComplete)
        {
            if (onComplete == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(spreadsheetId) || string.IsNullOrEmpty(apiKey))
            {
                onComplete(Result.Fail("SpreadsheetId / ApiKey 가 비어 있습니다."));
                return;
            }

            //== fields 로 필요한 속성만 받습니다. 시트가 많으면 응답 크기 차이가 큽니다.
            string url = $"https://sheets.googleapis.com/v4/spreadsheets/{spreadsheetId}" +
                         $"?key={apiKey}&fields=sheets.properties(title,sheetId)";

            TableWebRequest.Get(url, response =>
            {
                if (!response.Success)
                {
                    onComplete(Result.Fail($"HTTP {response.Code}: {response.Error}\n{response.Text}"));
                    return;
                }

                try
                {
                    onComplete(Result.Ok(ExtractSheets(response.Text)));
                }
                catch (Exception e)
                {
                    onComplete(Result.Fail($"JSON 파싱 실패: {e.Message}"));
                }
            });
        }

        #endregion

        #region Private Helpers

        private static List<SheetInfo> ExtractSheets(string json)
        {
            List<SheetInfo> result = new List<SheetInfo>();

            Dictionary<string, object> root = TableJson.Parse(json) as Dictionary<string, object>;
            if (root == null || !root.TryGetValue("sheets", out object sheetsValue))
            {
                return result;
            }

            List<object> sheets = sheetsValue as List<object>;
            if (sheets == null)
            {
                return result;
            }

            for (int i = 0; i < sheets.Count; i++)
            {
                Dictionary<string, object> sheet = sheets[i] as Dictionary<string, object>;
                if (sheet == null || !sheet.TryGetValue("properties", out object propertiesValue))
                {
                    continue;
                }

                Dictionary<string, object> properties = propertiesValue as Dictionary<string, object>;
                if (properties == null)
                {
                    continue;
                }

                properties.TryGetValue("title", out object titleValue);
                string title = titleValue as string;
                if (string.IsNullOrEmpty(title))
                {
                    continue;
                }

                //== JSON 숫자는 전부 double 로 읽히므로 gid 는 소수점 없이 다시 문자열로 만듭니다.
                properties.TryGetValue("sheetId", out object sheetIdValue);
                string gid = sheetIdValue is double
                    ? ((double)sheetIdValue).ToString("0", CultureInfo.InvariantCulture)
                    : null;

                result.Add(new SheetInfo { Title = title, Gid = gid });
            }

            return result;
        }

        #endregion
    }
}
