using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

using Cocoa.Lib.Sound;

namespace Cocoa.Lib.Editor.Sound
{
    /// <summary>
    /// SoundLibrary 를 채널별로 묶어 보여주고 편집하는 창입니다.
    /// 검색, 채널 필터, 채널 단위 순차 미리듣기, ID 중복 검사, 드래그 앤 드롭 추가를 지원합니다.
    /// </summary>
    /// <remarks>
    /// 목록은 SerializedObject 로 편집하므로 추가와 제거가 Undo 에 기록되고 에셋 변경 표시도 자동으로 됩니다.
    /// </remarks>
    public sealed class SoundLibraryWindow : EditorWindow
    {
        #region Constants

        private const string WindowTitle = "Sound Library";

        //== 경로가 아니라 GUID 로 기억해야 라이브러리 에셋을 옮기거나 이름을 바꿔도 다시 열 때 찾을 수 있습니다.
        private const string LastLibraryKey = "Cocoa.Lib.SoundLibraryWindow.LastLibraryGuid";

        private const string ObjectSelectorClosedCommand = "ObjectSelectorClosed";
        private const double RepaintInterval = 0.2;

        #endregion

        #region Static Fields

        //== Master 를 뺀, 클립을 가질 수 있는 채널들. 표시 순서이기도 합니다.
        private static readonly SoundChannel[] _clipChannels = { SoundChannel.BGM, SoundChannel.SFX, SoundChannel.Voice };
        private static readonly string[] _filterLabels = { "All", "BGM", "SFX", "Voice" };

        private static readonly Color _playingColor = new Color(0.4f, 0.9f, 0.4f);
        private static readonly Color _removeColor = new Color(1f, 0.7f, 0.7f);
        private static readonly Color _dropAreaColor = new Color(0.3f, 0.3f, 0.3f, 0.3f);

        #endregion

        #region Fields

        private SoundLibrary _library;
        private SerializedObject _serializedLibrary;
        private SerializedProperty _definitionsProperty;

        private Vector2 _scrollPosition;
        private string _searchFilter = string.Empty;

        //== _filterLabels 의 인덱스. 0 은 전체, 그 외는 _clipChannels[index - 1] 입니다.
        private int _filterIndex;

        private readonly bool[] _foldouts = { true, true, true };

        //== 채널별로 걸러진 목록의 배열 인덱스. 제거할 때 SerializedProperty 위치로 씁니다.
        private readonly List<int>[] _channelIndices = { new List<int>(), new List<int>(), new List<int>() };

        private readonly HashSet<string> _duplicateIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _seenIds = new HashSet<string>(StringComparer.Ordinal);
        private int _nullEntryCount;

        //== 순차 재생을 시작한 채널의 _clipChannels 인덱스. 없으면 -1.
        private int _sequenceChannelIndex = -1;
        private double _lastRepaintTime;
        private int _pickerControlId;

        [NonSerialized] private GUIStyle _foldoutStyle;
        [NonSerialized] private GUIStyle _duplicateLabelStyle;
        [NonSerialized] private GUIStyle _centeredLabelStyle;
        [NonSerialized] private GUIStyle _dropAreaStyle;

        #endregion

        #region Menu

        [MenuItem("Cocoa/Lib/Sound Library", priority = 100)]
        public static void ShowWindow()
        {
            SoundLibraryWindow window = GetWindow<SoundLibraryWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(420f, 300f);
            window.Show();
        }

        #endregion

        #region Unity Messages

        private void OnEnable()
        {
            string guid = EditorPrefs.GetString(LastLibraryKey, string.Empty);
            if (!string.IsNullOrEmpty(guid))
            {
                SetLibrary(AssetDatabase.LoadAssetAtPath<SoundLibrary>(AssetDatabase.GUIDToAssetPath(guid)));
            }

            EditorApplication.update += OnEditorUpdate;
            Undo.undoRedoPerformed += Repaint;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            Undo.undoRedoPerformed -= Repaint;

            if (_sequenceChannelIndex >= 0)
            {
                SoundEditorUtility.StopAllPreviews();
                _sequenceChannelIndex = -1;
            }
        }

        private void OnGUI()
        {
            EnsureStyles();
            DrawToolbar();

            if (_library == null)
            {
                DrawEmptyState();
                return;
            }

            //== 다른 곳(인스펙터, Undo)에서 바뀐 값을 매번 읽어 와야 ID 중복 표시가 늦게 갱신되지 않습니다.
            _serializedLibrary.Update();
            Collect();

            DrawSearchBar();
            DrawValidation();

            EditorGUILayout.Space(4f);
            DrawGroups();
            DrawDropArea();

            HandleObjectPicker();
            HandleDragAndDrop();
        }

        #endregion

        #region Private Helpers - Drawing

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUI.BeginChangeCheck();
                SoundLibrary picked = (SoundLibrary)EditorGUILayout.ObjectField(_library, typeof(SoundLibrary), false, GUILayout.Width(220f));
                if (EditorGUI.EndChangeCheck())
                {
                    SetLibrary(picked);
                }

                if (GUILayout.Button("New", EditorStyles.toolbarButton, GUILayout.Width(50f)))
                {
                    CreateLibrary();
                }

                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(_library == null))
                {
                    if (GUILayout.Button("+ Existing", EditorStyles.toolbarButton, GUILayout.Width(80f)))
                    {
                        _pickerControlId = GUIUtility.GetControlID(FocusType.Passive);
                        EditorGUIUtility.ShowObjectPicker<SoundDefinition>(null, false, string.Empty, _pickerControlId);
                    }
                }

                if (GUILayout.Button("■ Stop All", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                {
                    SoundEditorUtility.StopAllPreviews();
                    _sequenceChannelIndex = -1;
                }
            }
        }

        private void DrawEmptyState()
        {
            EditorGUILayout.Space(20f);
            EditorGUILayout.HelpBox("SoundLibrary 를 선택하거나 New 로 새로 만드세요.", MessageType.Info);
            EditorGUILayout.Space(10f);

            if (GUILayout.Button("Create New SoundLibrary", GUILayout.Height(30f)))
            {
                CreateLibrary();
            }
        }

        private void DrawSearchBar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                _searchFilter = EditorGUILayout.TextField(_searchFilter, EditorStyles.toolbarSearchField);
                _filterIndex = EditorGUILayout.Popup(_filterIndex, _filterLabels, EditorStyles.toolbarPopup, GUILayout.Width(80f));
            }
        }

        private void DrawValidation()
        {
            if (_duplicateIds.Count > 0)
            {
                EditorGUILayout.HelpBox($"중복된 ID: {string.Join(", ", _duplicateIds)}\n먼저 등록된 것만 SoundManager 에 들어갑니다.", MessageType.Error);
            }

            if (_nullEntryCount > 0)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.HelpBox($"비어 있는 항목이 {_nullEntryCount}개 있습니다.", MessageType.Warning);
                    if (GUILayout.Button("정리", GUILayout.Width(60f), GUILayout.Height(38f)))
                    {
                        RemoveNullEntries();
                        GUIUtility.ExitGUI();
                    }
                }
            }
        }

        private void DrawGroups()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            for (int c = 0; c < _clipChannels.Length; c++)
            {
                if (_filterIndex != 0 && _filterIndex - 1 != c)
                {
                    continue;
                }

                //== 검색 중에는 결과가 없는 채널을 숨겨 결과만 보이게 합니다.
                if (_channelIndices[c].Count == 0 && !string.IsNullOrEmpty(_searchFilter))
                {
                    continue;
                }

                DrawGroup(c);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawGroup(int channelIndex)
        {
            SoundChannel channel = _clipChannels[channelIndex];
            List<int> indices = _channelIndices[channelIndex];

            Rect header = EditorGUILayout.GetControlRect(false, 22f);
            Color color = SoundEditorStyles.GetChannelColor(channel);
            EditorGUI.DrawRect(header, new Color(color.r, color.g, color.b, 0.4f));

            Rect foldoutRect = new Rect(header.x + 4f, header.y + 2f, header.width - 70f, header.height);
            _foldouts[channelIndex] = EditorGUI.Foldout(foldoutRect, _foldouts[channelIndex], $"{channel} ({indices.Count})", true, _foldoutStyle);

            bool isSequencing = _sequenceChannelIndex == channelIndex && SoundEditorUtility.IsSequenceActive;
            string playAllLabel = isSequencing
                ? $"■ {SoundEditorUtility.SequenceCurrentIndex + 1}/{SoundEditorUtility.SequenceTotalCount}"
                : "▶ All";

            Rect playAllRect = new Rect(header.xMax - 64f, header.y + 2f, 60f, header.height - 4f);
            using (new EditorGUI.DisabledScope(indices.Count == 0))
            {
                if (GUI.Button(playAllRect, playAllLabel))
                {
                    if (isSequencing)
                    {
                        SoundEditorUtility.StopAllPreviews();
                        _sequenceChannelIndex = -1;
                    }
                    else
                    {
                        PlayChannel(channelIndex);
                    }
                }
            }

            if (!_foldouts[channelIndex])
            {
                return;
            }

            for (int i = 0; i < indices.Count; i++)
            {
                if (DrawRow(indices[i]))
                {
                    //== 목록이 바뀌었으므로 이번 이벤트의 나머지 레이아웃을 버리고 다음 프레임에 다시 그립니다.
                    GUIUtility.ExitGUI();
                }
            }

            EditorGUILayout.Space(2f);
        }

        //== 항목을 제거했으면 true 를 반환합니다.
        private bool DrawRow(int arrayIndex)
        {
            SoundDefinition definition = GetDefinition(arrayIndex);
            AudioClip clip = definition.Clip;
            bool isPlaying = clip != null && SoundEditorUtility.IsPreviewPlaying(clip);

            using (new EditorGUILayout.HorizontalScope())
            {
                Color previous = GUI.backgroundColor;
                if (isPlaying)
                {
                    GUI.backgroundColor = _playingColor;
                }

                using (new EditorGUI.DisabledScope(clip == null))
                {
                    if (GUILayout.Button(isPlaying ? "●" : "▶", GUILayout.Width(24f)))
                    {
                        if (isPlaying)
                        {
                            SoundEditorUtility.StopAllPreviews();
                        }
                        else
                        {
                            _sequenceChannelIndex = -1;
                            SoundEditorUtility.PlayPreview(clip, false, definition.DefaultVolume);
                        }
                    }
                }

                GUI.backgroundColor = previous;

                string id = string.IsNullOrEmpty(definition.Id) ? "(ID 없음)" : definition.Id;
                GUIStyle labelStyle = _duplicateIds.Contains(definition.Id ?? string.Empty) ? _duplicateLabelStyle : EditorStyles.label;
                EditorGUILayout.LabelField(id, labelStyle, GUILayout.Width(150f));

                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.ObjectField(clip, typeof(AudioClip), false);
                }

                //== 클립 필드의 선택 아이콘과 볼륨 숫자가 붙어 보이지 않도록 띄웁니다.
                GUILayout.Space(10f);
                EditorGUILayout.LabelField(definition.DefaultVolume.ToString("F2"), _centeredLabelStyle, GUILayout.Width(48f));
                GUILayout.Space(4f);

                if (GUILayout.Button("Edit", GUILayout.Width(40f)))
                {
                    Selection.activeObject = definition;
                    EditorGUIUtility.PingObject(definition);
                }

                GUI.backgroundColor = _removeColor;
                bool remove = GUILayout.Button("X", GUILayout.Width(24f))
                    && EditorUtility.DisplayDialog("라이브러리에서 제거", $"'{id}' 를 라이브러리에서 뺍니다.\n에셋 파일은 지워지지 않습니다.", "제거", "취소");
                GUI.backgroundColor = previous;

                if (remove)
                {
                    RemoveAt(arrayIndex);
                    return true;
                }
            }

            return false;
        }

        private void DrawDropArea()
        {
            EditorGUILayout.Space(4f);
            Rect rect = GUILayoutUtility.GetRect(0f, 40f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, _dropAreaColor);
            EditorGUI.LabelField(rect, "AudioClip 이나 SoundDefinition 을 끌어다 놓으면 추가됩니다", _dropAreaStyle);
        }

        #endregion

        #region Private Helpers - Input

        private void HandleObjectPicker()
        {
            Event current = Event.current;
            if (current.type != EventType.ExecuteCommand || current.commandName != ObjectSelectorClosedCommand)
            {
                return;
            }

            if (EditorGUIUtility.GetObjectPickerControlID() != _pickerControlId)
            {
                return;
            }

            _pickerControlId = 0;
            SoundDefinition picked = EditorGUIUtility.GetObjectPickerObject() as SoundDefinition;
            if (picked != null && SoundEditorTools.PromptRelocation(picked, GetLibraryFolder()))
            {
                Add(picked);
                _serializedLibrary.ApplyModifiedProperties();
            }

            current.Use();
        }

        private void HandleDragAndDrop()
        {
            Event current = Event.current;
            if (current.type != EventType.DragUpdated && current.type != EventType.DragPerform)
            {
                return;
            }

            UnityEngine.Object[] dragged = DragAndDrop.objectReferences;
            bool hasValid = false;
            for (int i = 0; i < dragged.Length; i++)
            {
                if (dragged[i] is AudioClip || dragged[i] is SoundDefinition)
                {
                    hasValid = true;
                    break;
                }
            }

            if (!hasValid)
            {
                return;
            }

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

            if (current.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                AddDropped(dragged);
            }

            current.Use();
        }

        private void AddDropped(UnityEngine.Object[] dropped)
        {
            string libraryFolder = GetLibraryFolder();
            List<AudioClip> clips = new List<AudioClip>();
            int added = 0;

            for (int i = 0; i < dropped.Length; i++)
            {
                AudioClip clip = dropped[i] as AudioClip;
                if (clip != null)
                {
                    clips.Add(clip);
                    continue;
                }

                SoundDefinition definition = dropped[i] as SoundDefinition;
                if (definition != null && SoundEditorTools.PromptRelocation(definition, libraryFolder) && Add(definition))
                {
                    added++;
                }
            }

            //== 클립으로 만든 정의는 라이브러리 옆에 모아야 라이브러리 단위로 관리할 수 있습니다.
            List<SoundDefinition> created = SoundEditorTools.CreateDefinitions(clips, libraryFolder);
            for (int i = 0; i < created.Count; i++)
            {
                if (Add(created[i]))
                {
                    added++;
                }
            }

            if (added > 0)
            {
                _serializedLibrary.ApplyModifiedProperties();
            }
        }

        #endregion

        #region Private Helpers - Library

        private void SetLibrary(SoundLibrary library)
        {
            _library = library;
            _serializedLibrary = library != null ? new SerializedObject(library) : null;
            _definitionsProperty = _serializedLibrary != null ? _serializedLibrary.FindProperty("_definitions") : null;

            string path = library != null ? AssetDatabase.GetAssetPath(library) : string.Empty;
            EditorPrefs.SetString(LastLibraryKey, string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path));
        }

        private void CreateLibrary()
        {
            string path = EditorUtility.SaveFilePanelInProject("새 SoundLibrary", "NewSoundLibrary", "asset", "SoundLibrary 를 저장할 위치를 고르세요.");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            SoundLibrary library = CreateInstance<SoundLibrary>();
            AssetDatabase.CreateAsset(library, path);
            AssetDatabase.SaveAssets();
            SetLibrary(library);
        }

        private string GetLibraryFolder()
        {
            return SoundEditorTools.GetParentFolder(AssetDatabase.GetAssetPath(_library));
        }

        private SoundDefinition GetDefinition(int arrayIndex)
        {
            return _definitionsProperty.GetArrayElementAtIndex(arrayIndex).objectReferenceValue as SoundDefinition;
        }

        //== 이미 들어 있으면 false 를 반환합니다. 반영은 호출하는 쪽이 ApplyModifiedProperties 로 합니다.
        private bool Add(SoundDefinition definition)
        {
            int size = _definitionsProperty.arraySize;
            for (int i = 0; i < size; i++)
            {
                if (_definitionsProperty.GetArrayElementAtIndex(i).objectReferenceValue == definition)
                {
                    return false;
                }
            }

            _definitionsProperty.InsertArrayElementAtIndex(size);
            _definitionsProperty.GetArrayElementAtIndex(size).objectReferenceValue = definition;
            return true;
        }

        private void RemoveAt(int arrayIndex)
        {
            //== 버전에 따라 참조가 남은 요소는 첫 삭제에서 null 로만 바뀌므로 먼저 비우고 지웁니다.
            _definitionsProperty.GetArrayElementAtIndex(arrayIndex).objectReferenceValue = null;
            _definitionsProperty.DeleteArrayElementAtIndex(arrayIndex);
            _serializedLibrary.ApplyModifiedProperties();
        }

        private void RemoveNullEntries()
        {
            for (int i = _definitionsProperty.arraySize - 1; i >= 0; i--)
            {
                if (_definitionsProperty.GetArrayElementAtIndex(i).objectReferenceValue == null)
                {
                    _definitionsProperty.DeleteArrayElementAtIndex(i);
                }
            }

            _serializedLibrary.ApplyModifiedProperties();
        }

        //== 채널별 표시 목록과 검증 결과를 한 번의 순회로 만듭니다.
        private void Collect()
        {
            for (int c = 0; c < _channelIndices.Length; c++)
            {
                _channelIndices[c].Clear();
            }

            _duplicateIds.Clear();
            _seenIds.Clear();
            _nullEntryCount = 0;

            int size = _definitionsProperty.arraySize;
            for (int i = 0; i < size; i++)
            {
                SoundDefinition definition = GetDefinition(i);
                if (definition == null)
                {
                    _nullEntryCount++;
                    continue;
                }

                if (!string.IsNullOrEmpty(definition.Id) && !_seenIds.Add(definition.Id))
                {
                    _duplicateIds.Add(definition.Id);
                }

                int channelIndex = Array.IndexOf(_clipChannels, definition.Channel);
                if (channelIndex < 0 || !MatchesSearch(definition))
                {
                    continue;
                }

                _channelIndices[channelIndex].Add(i);
            }
        }

        private bool MatchesSearch(SoundDefinition definition)
        {
            if (string.IsNullOrEmpty(_searchFilter))
            {
                return true;
            }

            return !string.IsNullOrEmpty(definition.Id) && definition.Id.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void PlayChannel(int channelIndex)
        {
            List<int> indices = _channelIndices[channelIndex];
            List<AudioClip> clips = new List<AudioClip>(indices.Count);
            for (int i = 0; i < indices.Count; i++)
            {
                AudioClip clip = GetDefinition(indices[i]).Clip;
                if (clip != null)
                {
                    clips.Add(clip);
                }
            }

            if (clips.Count == 0)
            {
                return;
            }

            _sequenceChannelIndex = channelIndex;
            SoundEditorUtility.PlaySequence(clips);
        }

        #endregion

        #region Private Helpers - Misc

        private void OnEditorUpdate()
        {
            if (_sequenceChannelIndex < 0)
            {
                return;
            }

            //== 순차 재생이 끝나면 버튼을 원래대로 돌려놓습니다.
            if (!SoundEditorUtility.IsSequenceActive)
            {
                _sequenceChannelIndex = -1;
                Repaint();
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            if (now - _lastRepaintTime > RepaintInterval)
            {
                _lastRepaintTime = now;
                Repaint();
            }
        }

        private void EnsureStyles()
        {
            if (_foldoutStyle != null)
            {
                return;
            }

            _foldoutStyle = new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold };
            _duplicateLabelStyle = new GUIStyle(EditorStyles.label);
            _duplicateLabelStyle.normal.textColor = Color.red;
            _centeredLabelStyle = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter };
            _dropAreaStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 11 };
        }

        #endregion
    }
}
