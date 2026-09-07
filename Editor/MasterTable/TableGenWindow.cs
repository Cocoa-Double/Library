using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Cocoa.Lib.Editor.MasterTable
{
    /// <summary>
    /// 구글 시트에서 탭 목록을 받아 버튼 하나로 테이블을 생성하는 에디터 창입니다.
    /// </summary>
    /// <remarks>
    /// 설정 에셋은 경로가 아니라 GUID 로 기억하므로 에셋을 다른 폴더로 옮겨도 계속 찾아냅니다.
    /// </remarks>
    public sealed class TableGenWindow : EditorWindow
    {
        #region Constants

        private const string SettingsGuidKey = "Cocoa.Lib.TableGen.SettingsGuid";
        private const string SortAlphabeticalKey = "Cocoa.Lib.TableGen.SortAlphabetical";
        private const string SheetListCacheKey = "Cocoa.Lib.TableGen.SheetListCache";

        private const float SheetListHeight = 320f;
        private const float SheetButtonHeight = 28f;
        private const int SheetButtonFontSize = 13;
        private const int StatusFontSize = 15;
        private const int HelpBoxFontSize = 13;
        private const float LoadButtonWidth = 80f;
        private const int LoadingGaugeSegments = 12;
        private const float BadgeContentWidth = 54f;

        //== 시트 이름에 절대 나올 수 없는 제어문자를 목록 캐시의 구분자로 씁니다.
        private const char CacheRowSeparator = '\u0001';
        private const char CacheFieldSeparator = '\u0002';

        #endregion

        #region Static Fields

        //== 게이지는 작업 중 매 프레임 다시 그려지므로 재사용합니다.
        private static readonly StringBuilder _gaugeBuilder = new StringBuilder(LoadingGaugeSegments + 2);

        #endregion

        #region Fields

        private TableGenSettings _settings;
        private string _urlOrId = string.Empty;
        private List<TableSheetList.SheetInfo> _sheets;
        private Vector2 _scroll;
        private bool _isBusy;
        private string _status;

        private string _searchText = string.Empty;
        private bool _sortAlphabetical;

        //== 이미 컴파일된 Table 클래스 이름들. [ New ] 배지를 붙일지 판단하는 데 씁니다.
        private HashSet<string> _existingClassNames;

        //== GUIStyle 은 OnGUI 첫 호출 때 한 번만 만듭니다.
        private GUIStyle _sheetButtonStyle;
        private GUIStyle _sortToggleActiveStyle;
        private GUIStyle _sortToggleInactiveStyle;
        private GUIStyle _fetchAllButtonStyle;
        private GUIStyle _loadButtonStyle;
        private GUIStyle _statusStyle;
        private GUIStyle _helpBoxStyle;
        private GUIStyle _newBadgeStyle;

        #endregion

        #region Menu

        [MenuItem("Cocoa/Lib/Table Generator")]
        private static void Open()
        {
            TableGenWindow window = GetWindow<TableGenWindow>("Table Generator");
            window.minSize = new Vector2(440, 380);
            window.Show();
        }

        #endregion

        #region Unity Messages

        private void OnEnable()
        {
            _settings = LoadRemembered();
            _sortAlphabetical = EditorPrefs.GetBool(SortAlphabeticalKey, false);
            LoadSheetListCache();
            RefreshExistingClassCache();
            TryAutoLoadSheets();
        }

        //== 평소에는 이벤트가 있을 때만 그려지므로 작업 중에만 직접 다시 그립니다.
        private void Update()
        {
            if (_isBusy)
            {
                Repaint();
            }
        }

        private void OnGUI()
        {
            EnsureStyles();

            EditorGUILayout.LabelField("구글 시트 로드 도구", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            DrawSettingsField();
            if (_settings == null)
            {
                return;
            }

            EditorGUILayout.Space();
            DrawSpreadsheetInputs();

            EditorGUILayout.Space();
            DrawSheetList();

            EditorGUILayout.Space();
            DrawManualList();

            DrawStatus();
        }

        #endregion

        #region GUI - 설정 에셋

        private void DrawSettingsField()
        {
            EditorGUI.BeginChangeCheck();
            _settings = (TableGenSettings)EditorGUILayout.ObjectField("설정", _settings, typeof(TableGenSettings), false);
            if (EditorGUI.EndChangeCheck())
            {
                Remember(_settings);
                _sheets = null;
                _urlOrId = string.Empty;
                SessionState.EraseString(SheetListCacheKey);
                RefreshExistingClassCache();
                TryAutoLoadSheets();
            }

            if (_settings != null)
            {
                return;
            }

            EditorGUILayout.LabelField("ℹ  TableGenSettings 에셋을 지정하세요.", _helpBoxStyle);

            //== 처음 창을 연 사람의 시선을 유도하려고 '전체 받기'와 같은 강조 톤을 씁니다.
            Color previousBackground = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.55f, 0.88f, 0.98f);
            if (GUILayout.Button("＋  <b>설정 에셋 만들기</b>", _fetchAllButtonStyle, GUILayout.Height(40)))
            {
                CreateSettingsAsset();
            }

            GUI.backgroundColor = previousBackground;
        }

        #endregion

        #region GUI - 스프레드시트 입력

        private void DrawSpreadsheetInputs()
        {
            EditorGUILayout.LabelField("스프레드시트", EditorStyles.boldLabel);

            if (string.IsNullOrEmpty(_urlOrId) && !string.IsNullOrEmpty(_settings.SpreadsheetId))
            {
                _urlOrId = _settings.SpreadsheetId;
            }

            //== 오른쪽 버튼이 왼쪽 두 줄과 같은 높이를 차지하도록 직접 계산합니다.
            float lineHeight = EditorGUIUtility.singleLineHeight;
            float buttonHeight = lineHeight * 2 + EditorGUIUtility.standardVerticalSpacing;

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Height(buttonHeight)))
                {
                    string apiKey = EditorGUILayout.TextField("API 키", _settings.ApiKey);
                    if (apiKey != _settings.ApiKey)
                    {
                        _settings.ApiKey = apiKey;
                        EditorUtility.SetDirty(_settings);
                    }

                    _urlOrId = EditorGUILayout.TextField("URL 또는 ID", _urlOrId);
                }

                using (new EditorGUI.DisabledScope(_isBusy))
                {
                    if (GUILayout.Button("불러오기", _loadButtonStyle, GUILayout.Width(LoadButtonWidth), GUILayout.Height(buttonHeight)))
                    {
                        LoadSheetList();
                    }
                }
            }
        }

        #endregion

        #region GUI - 시트 목록

        private void DrawSheetList()
        {
            EditorGUILayout.LabelField("▽ 시트 목록  [ 버튼 클릭시 데이터 생성 또는 갱신 ]", EditorStyles.boldLabel);

            if (_sheets == null || _sheets.Count == 0)
            {
                EditorGUILayout.LabelField("ℹ  위에서 '불러오기'를 누르세요.", _helpBoxStyle);
                return;
            }

            DrawSheetToolbar();

            List<TableSheetList.SheetInfo> view = BuildSheetView();

            using (new EditorGUI.DisabledScope(_isBusy))
            {
                //== A-Z 토글의 진한 파랑과 같은 계열이면서 채도로 구분되는 밝은 시안을 씁니다.
                Color previousBackground = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.55f, 0.88f, 0.98f);
                if (GUILayout.Button($"▼  <b>전체 받기</b>   ({view.Count}개)", _fetchAllButtonStyle, GUILayout.Height(36)))
                {
                    Generate(ToTitleList(view));
                }

                GUI.backgroundColor = previousBackground;

                _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MaxHeight(SheetListHeight));
                if (view.Count == 0)
                {
                    EditorGUILayout.LabelField("ℹ  검색 조건에 맞는 시트가 없습니다.", _helpBoxStyle);
                }
                else
                {
                    DrawSheetRows(view);
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawSheetRows(List<TableSheetList.SheetInfo> view)
        {
            float badgeSlot = BadgeContentWidth + _newBadgeStyle.margin.horizontal;

            for (int i = 0; i < view.Count; i++)
            {
                string title = view[i].Title;
                bool isNew = _existingClassNames == null || !_existingClassNames.Contains(title);

                using (new EditorGUILayout.HorizontalScope())
                {
                    //== 배지가 없는 행도 같은 폭을 비워 둬서 시트명 시작점을 맞춥니다.
                    if (isNew)
                    {
                        GUILayout.Label("[ New ]", _newBadgeStyle, GUILayout.Width(BadgeContentWidth), GUILayout.Height(SheetButtonHeight));
                    }
                    else
                    {
                        GUILayout.Space(badgeSlot);
                    }

                    if (GUILayout.Button(title, _sheetButtonStyle, GUILayout.Height(SheetButtonHeight)))
                    {
                        Generate(new List<string> { title });
                    }
                }
            }
        }

        private void DrawSheetToolbar()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("검색", GUILayout.Width(36));
                _searchText = EditorGUILayout.TextField(_searchText);

                //== 배경색만으로는 헷갈리므로 켜짐과 꺼짐을 색과 기호(●/○) 양쪽으로 구분합니다.
                GUIStyle style = _sortAlphabetical ? _sortToggleActiveStyle : _sortToggleInactiveStyle;

                Color previousBackground = GUI.backgroundColor;
                if (_sortAlphabetical)
                {
                    GUI.backgroundColor = new Color(0.3f, 0.6f, 1f);
                }

                if (GUILayout.Button(_sortAlphabetical ? "● A-Z" : "○ A-Z", style, GUILayout.Width(64), GUILayout.Height(20)))
                {
                    _sortAlphabetical = !_sortAlphabetical;
                    EditorPrefs.SetBool(SortAlphabeticalKey, _sortAlphabetical);
                }

                GUI.backgroundColor = previousBackground;
            }
        }

        //== 검색과 정렬이 적용된 표시용 목록입니다. 받아 온 원본 순서는 건드리지 않습니다.
        private List<TableSheetList.SheetInfo> BuildSheetView()
        {
            List<TableSheetList.SheetInfo> view = new List<TableSheetList.SheetInfo>(_sheets.Count);
            string filter = (_searchText ?? string.Empty).Trim();
            bool hasFilter = filter.Length > 0;

            for (int i = 0; i < _sheets.Count; i++)
            {
                TableSheetList.SheetInfo info = _sheets[i];
                if (info == null || string.IsNullOrEmpty(info.Title))
                {
                    continue;
                }

                if (IsIgnoredSheet(info.Title))
                {
                    continue;
                }

                if (hasFilter && info.Title.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                view.Add(info);
            }

            if (_sortAlphabetical)
            {
                view.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
            }

            return view;
        }

        private bool IsIgnoredSheet(string title)
        {
            if (_settings == null || _settings.IgnoredSheetPrefixes == null)
            {
                return false;
            }

            for (int i = 0; i < _settings.IgnoredSheetPrefixes.Count; i++)
            {
                string prefix = _settings.IgnoredSheetPrefixes[i];
                if (!string.IsNullOrEmpty(prefix) && title.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private void DrawManualList()
        {
            if (_settings.Sheets == null || _settings.Sheets.Count == 0)
            {
                return;
            }

            EditorGUILayout.LabelField("수동 목록 (설정 SO)", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(_isBusy))
            {
                if (!GUILayout.Button("수동 목록 전체 생성"))
                {
                    return;
                }

                List<string> names = new List<string>();
                for (int i = 0; i < _settings.Sheets.Count; i++)
                {
                    TableGenSettings.SheetTarget target = _settings.Sheets[i];
                    if (target != null && target.Enabled && !string.IsNullOrEmpty(target.SheetName))
                    {
                        names.Add(target.SheetName);
                    }
                }

                Generate(names);
            }
        }

        private void DrawStatus()
        {
            if (_isBusy)
            {
                EditorGUILayout.Space();
                string message = string.IsNullOrEmpty(_status) ? "처리 중..." : _status;
                EditorGUILayout.LabelField($"{BuildLoadingGauge()}   {message}", _statusStyle);
                return;
            }

            if (string.IsNullOrEmpty(_status))
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(_status, _statusStyle);
        }

        //== 진행률을 알 수 없는 작업이라 시간 기반으로 칸을 채우고, 끝까지 차면 처음으로 돌아갑니다.
        private static string BuildLoadingGauge()
        {
            int filled = (int)(EditorApplication.timeSinceStartup * 6) % (LoadingGaugeSegments + 1);

            _gaugeBuilder.Length = 0;
            _gaugeBuilder.Append('[');
            for (int i = 0; i < LoadingGaugeSegments; i++)
            {
                _gaugeBuilder.Append(i < filled ? '█' : '░');
            }

            _gaugeBuilder.Append(']');
            return _gaugeBuilder.ToString();
        }

        #endregion

        #region Actions

        private void LoadSheetList()
        {
            //== 붙여넣은 주소를 ID 로 정리해 저장하면 다음에 창을 열 때 자동으로 목록을 받습니다.
            string id = TableSheetList.ExtractSpreadsheetId(_urlOrId);
            _settings.SpreadsheetId = id;
            EditorUtility.SetDirty(_settings);

            _isBusy = true;
            _status = "시트 목록을 불러오는 중...";

            TableSheetList.FetchAsync(id, _settings.ApiKey, result =>
            {
                //== 응답을 기다리는 동안 창을 닫으면 파괴된 창을 건드리게 됩니다.
                if (this == null)
                {
                    return;
                }

                _isBusy = false;
                if (!result.Success)
                {
                    _sheets = null;
                    _status = $"목록 실패: {result.Error}";
                }
                else
                {
                    _sheets = result.Sheets;
                    _status = $"시트 {_sheets.Count}개를 찾았습니다.";
                    RefreshExistingClassCache();
                    SaveSheetListCache();
                }

                Repaint();
            });
        }

        private void Generate(List<string> sheetNames)
        {
            if (sheetNames == null || sheetNames.Count == 0)
            {
                _status = "생성할 시트가 없습니다.";
                return;
            }

            _isBusy = true;
            _status = $"{sheetNames.Count}개 시트 생성 중...";

            TableGenerator.GenerateSheets(_settings, sheetNames, () =>
            {
                if (this == null)
                {
                    return;
                }

                _isBusy = false;
                _status = "코드 생성 요청 완료. 컴파일이 끝나면 데이터가 자동 주입됩니다.";
                Repaint();
            });
        }

        #endregion

        #region Private Helpers - 스타일

        //== EditorStyles 와 GUI.skin 은 OnEnable 시점에 아직 준비되지 않습니다.
        private void EnsureStyles()
        {
            if (_sheetButtonStyle == null)
            {
                _sheetButtonStyle = new GUIStyle(GUI.skin.button)
                {
                    fontSize = SheetButtonFontSize,
                    alignment = TextAnchor.MiddleLeft,
                    padding = new RectOffset(10, 10, 4, 4),
                    richText = true
                };
            }

            if (_sortToggleActiveStyle == null)
            {
                _sortToggleActiveStyle = new GUIStyle(GUI.skin.button)
                {
                    fontStyle = FontStyle.Bold
                };
                _sortToggleActiveStyle.normal.textColor = new Color(0.4f, 0.85f, 1f);
                _sortToggleActiveStyle.hover.textColor = new Color(0.5f, 0.9f, 1f);
            }

            if (_sortToggleInactiveStyle == null)
            {
                //== 꺼진 상태는 배경을 칠하지 않고 무광 회색 텍스트만 둡니다.
                _sortToggleInactiveStyle = new GUIStyle(GUI.skin.button)
                {
                    fontStyle = FontStyle.Normal
                };
                _sortToggleInactiveStyle.normal.textColor = new Color(0.55f, 0.55f, 0.6f);
                _sortToggleInactiveStyle.hover.textColor = new Color(0.75f, 0.75f, 0.8f);
            }

            if (_fetchAllButtonStyle == null)
            {
                _fetchAllButtonStyle = new GUIStyle(GUI.skin.button)
                {
                    fontSize = 14,
                    fontStyle = FontStyle.Bold,
                    richText = true,
                    alignment = TextAnchor.MiddleCenter
                };
                _fetchAllButtonStyle.normal.textColor = Color.white;
                _fetchAllButtonStyle.hover.textColor = new Color(0.9f, 0.98f, 1f);
                _fetchAllButtonStyle.active.textColor = Color.white;
            }

            if (_loadButtonStyle == null)
            {
                _loadButtonStyle = new GUIStyle(GUI.skin.button)
                {
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
            }

            if (_statusStyle == null)
            {
                //== helpBox 의 배경과 여백은 그대로 쓰고 폰트만 키웁니다.
                _statusStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    fontSize = StatusFontSize,
                    richText = true,
                    wordWrap = true
                };
            }

            if (_helpBoxStyle == null)
            {
                _helpBoxStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    fontSize = HelpBoxFontSize,
                    richText = true,
                    wordWrap = true
                };
            }

            if (_newBadgeStyle == null)
            {
                //== 버튼 라벨 안의 색은 비활성 상태에서 뭉개지므로 배지는 별도 라벨로 뽑습니다.
                _newBadgeStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = SheetButtonFontSize,
                    alignment = TextAnchor.MiddleCenter,
                    padding = new RectOffset(0, 0, 0, 0)
                };
                _newBadgeStyle.normal.textColor = new Color(1f, 0.88f, 0.4f);
            }
        }

        #endregion

        #region Private Helpers - 캐시

        //== 도메인 리로드마다 OnEnable 이 불리므로 이미 받아 둔 경우는 건너뜁니다.
        private void TryAutoLoadSheets()
        {
            if (_settings == null || _isBusy)
            {
                return;
            }

            if (_sheets != null && _sheets.Count > 0)
            {
                return;
            }

            if (string.IsNullOrEmpty(_settings.SpreadsheetId) || string.IsNullOrEmpty(_settings.ApiKey))
            {
                return;
            }

            _urlOrId = _settings.SpreadsheetId;
            LoadSheetList();
        }

        //== 어셈블리 전체를 훑는 작업이라 목록을 받은 직후와 리로드 직후에만 한 번씩 부릅니다.
        private void RefreshExistingClassCache()
        {
            _existingClassNames = new HashSet<string>(StringComparer.Ordinal);
            if (_settings == null || string.IsNullOrEmpty(_settings.TableNamespace))
            {
                return;
            }

            string tableNamespace = _settings.TableNamespace;
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int a = 0; a < assemblies.Length; a++)
            {
                Type[] types;
                try
                {
                    types = assemblies[a].GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    //== 일부만 로드된 어셈블리입니다. 읽을 수 있는 타입만 쓰고 못 읽은 자리는 null 로 옵니다.
                    types = e.Types;
                }
                catch (Exception)
                {
                    continue;
                }

                for (int t = 0; t < types.Length; t++)
                {
                    Type type = types[t];
                    if (type != null && type.Namespace == tableNamespace)
                    {
                        _existingClassNames.Add(type.Name);
                    }
                }
            }
        }

        //== 생성 후 도메인 리로드가 도는데 그때마다 목록을 다시 받으면 API 호출이 늘고 대기도 길어집니다.
        //== SheetInfo 가 직렬화 대상이 아니라 JsonUtility 를 쓸 수 없어 구분자로 직접 이어 붙입니다.
        private void SaveSheetListCache()
        {
            if (_sheets == null || _sheets.Count == 0)
            {
                SessionState.EraseString(SheetListCacheKey);
                return;
            }

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < _sheets.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(CacheRowSeparator);
                }

                builder.Append(_sheets[i].Title ?? string.Empty);
                builder.Append(CacheFieldSeparator);
                builder.Append(_sheets[i].Gid ?? string.Empty);
            }

            SessionState.SetString(SheetListCacheKey, builder.ToString());
        }

        private void LoadSheetListCache()
        {
            string raw = SessionState.GetString(SheetListCacheKey, string.Empty);
            if (string.IsNullOrEmpty(raw))
            {
                return;
            }

            string[] rows = raw.Split(CacheRowSeparator);
            List<TableSheetList.SheetInfo> list = new List<TableSheetList.SheetInfo>(rows.Length);
            for (int i = 0; i < rows.Length; i++)
            {
                string[] columns = rows[i].Split(CacheFieldSeparator);
                list.Add(new TableSheetList.SheetInfo
                {
                    Title = columns.Length > 0 ? columns[0] : string.Empty,
                    Gid = columns.Length > 1 ? columns[1] : null
                });
            }

            _sheets = list;
        }

        private static List<string> ToTitleList(List<TableSheetList.SheetInfo> sheets)
        {
            List<string> titles = new List<string>(sheets.Count);
            for (int i = 0; i < sheets.Count; i++)
            {
                titles.Add(sheets[i].Title);
            }

            return titles;
        }

        #endregion

        #region Private Helpers - 설정 기억

        private static TableGenSettings LoadRemembered()
        {
            string guid = EditorPrefs.GetString(SettingsGuidKey, string.Empty);
            if (string.IsNullOrEmpty(guid))
            {
                return null;
            }

            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<TableGenSettings>(path);
        }

        private static void Remember(TableGenSettings settings)
        {
            if (settings == null)
            {
                EditorPrefs.DeleteKey(SettingsGuidKey);
                return;
            }

            string path = AssetDatabase.GetAssetPath(settings);
            EditorPrefs.SetString(SettingsGuidKey, AssetDatabase.AssetPathToGUID(path));
        }

        private void CreateSettingsAsset()
        {
            TableGenSettings settings = CreateInstance<TableGenSettings>();
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/TableGenSettings.asset");

            AssetDatabase.CreateAsset(settings, path);
            AssetDatabase.SaveAssets();

            _settings = settings;
            Remember(settings);
            EditorGUIUtility.PingObject(settings);
            _status = $"설정 에셋을 만들었습니다: {path}";
        }

        #endregion
    }
}
