using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;

namespace Cocoa.Lib.Editor.MasterTable
{
    /// <summary>
    /// 시트에서 받아온 원본 행을 CSV 파일로 남깁니다.
    /// 데이터가 언제 어떻게 바뀌었는지 추적하고 되돌리기 위한 백업이며, 생성기는 호출만 하고 결과에 의존하지 않습니다.
    /// </summary>
    public static class TableCsvBackup
    {
        #region Constants

        public const string DefaultOutputFolder = "Assets/Logs/MasterTable";

        #endregion

        #region Static Fields

        //== RFC 4180: 쉼표, 따옴표, 줄바꿈이 하나라도 있으면 따옴표로 감싸고 내부 따옴표는 두 번 씁니다.
        private static readonly char[] _quoteTriggers = { ',', '"', '\r', '\n' };

        #endregion

        #region Public API

        /// <summary>
        /// 한 탭의 행 데이터를 {outputFolder}/{sheetName}.csv 로 저장합니다.
        /// outputFolder 가 비어 있으면 <see cref="DefaultOutputFolder"/> 를 쓰고, 폴더가 없으면 만듭니다.
        /// </summary>
        public static void Save(string sheetName, IReadOnlyList<IReadOnlyList<string>> rows, string outputFolder = null)
        {
            if (string.IsNullOrEmpty(sheetName))
            {
                return;
            }

            string folder = string.IsNullOrEmpty(outputFolder) ? DefaultOutputFolder : outputFolder;
            Directory.CreateDirectory(folder);

            string path = Path.Combine(folder, $"{ToSafeFileName(sheetName)}.csv").Replace('\\', '/');

            //== BOM 이 없으면 Excel 이 UTF-8 로 열지 않아 한글이 깨집니다.
            File.WriteAllText(path, BuildCsv(rows), new UTF8Encoding(true));

            //== Assets 아래면 AssetDatabase 에 알립니다. 임포트만 돌고 컴파일은 트리거되지 않습니다.
            if (path.StartsWith("Assets/"))
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
        }

        #endregion

        #region Private Helpers

        //== 탭 이름에 '/' 나 ':' 가 들어간 시트가 있어 그대로는 파일명이 되지 않습니다.
        private static string ToSafeFileName(string sheetName)
        {
            char[] invalid = Path.GetInvalidFileNameChars();

            StringBuilder builder = new StringBuilder(sheetName.Length);
            for (int i = 0; i < sheetName.Length; i++)
            {
                char c = sheetName[i];
                builder.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            }

            return builder.ToString();
        }

        private static string BuildCsv(IReadOnlyList<IReadOnlyList<string>> rows)
        {
            if (rows == null || rows.Count == 0)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder();
            for (int r = 0; r < rows.Count; r++)
            {
                IReadOnlyList<string> row = rows[r];
                if (row != null)
                {
                    for (int c = 0; c < row.Count; c++)
                    {
                        if (c > 0)
                        {
                            builder.Append(',');
                        }

                        AppendEscaped(builder, row[c]);
                    }
                }

                builder.Append("\r\n");
            }

            return builder.ToString();
        }

        private static void AppendEscaped(StringBuilder builder, string cell)
        {
            if (string.IsNullOrEmpty(cell))
            {
                return;
            }

            if (cell.IndexOfAny(_quoteTriggers) < 0)
            {
                builder.Append(cell);
                return;
            }

            builder.Append('"');
            for (int i = 0; i < cell.Length; i++)
            {
                char c = cell[i];
                if (c == '"')
                {
                    builder.Append('"').Append('"');
                }
                else
                {
                    builder.Append(c);
                }
            }

            builder.Append('"');
        }

        #endregion
    }
}
