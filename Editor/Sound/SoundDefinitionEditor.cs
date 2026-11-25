using UnityEditor;
using UnityEngine;

using Cocoa.Lib.Sound;

namespace Cocoa.Lib.Editor.Sound
{
    /// <summary>
    /// SoundDefinition 인스펙터. 채널 색상 헤더, 미리듣기, 클립 정보를 보여줍니다.
    /// </summary>
    [CustomEditor(typeof(SoundDefinition))]
    [CanEditMultipleObjects]
    public sealed class SoundDefinitionEditor : UnityEditor.Editor
    {
        #region Constants

        private const double RepaintInterval = 0.2;
        private const float HeaderHeight = 24f;
        private const float PreviewButtonHeight = 28f;

        #endregion

        #region Static Fields

        private static readonly Color _playingColor = new Color(0.4f, 0.9f, 0.4f);
        private static GUIStyle _headerStyle;

        #endregion

        #region Fields

        private SerializedProperty _idProperty;
        private SerializedProperty _channelProperty;
        private SerializedProperty _clipProperty;
        private SerializedProperty _defaultVolumeProperty;

        private bool _loopPreview;
        private bool _wasPlaying;
        private double _lastRepaintTime;

        #endregion

        #region Unity Messages

        private void OnEnable()
        {
            _idProperty = serializedObject.FindProperty("_id");
            _channelProperty = serializedObject.FindProperty("_channel");
            _clipProperty = serializedObject.FindProperty("_clip");
            _defaultVolumeProperty = serializedObject.FindProperty("_defaultVolume");

            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;

            //== 다른 창에서 시작한 미리듣기까지 끊지 않도록 이 에셋의 클립을 재생 중일 때만 멈춥니다.
            AudioClip clip = GetClip();
            if (clip != null && SoundEditorUtility.IsPreviewPlaying(clip))
            {
                SoundEditorUtility.StopAllPreviews();
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            //== 여러 에셋을 함께 선택했을 때 값이 섞여 있으면 헤더를 특정 채널로 그리지 않습니다.
            if (_channelProperty.hasMultipleDifferentValues)
            {
                DrawHeader("—", Color.gray);
            }
            else
            {
                SoundChannel channel = (SoundChannel)_channelProperty.intValue;
                DrawHeader(channel.ToString(), SoundEditorStyles.GetChannelColor(channel));
            }

            EditorGUILayout.Space(8f);

            EditorGUILayout.PropertyField(_idProperty);
            if (!_idProperty.hasMultipleDifferentValues && string.IsNullOrEmpty(_idProperty.stringValue))
            {
                EditorGUILayout.HelpBox("ID 가 비어 있습니다. SoundManager 에 등록할 때 키로 쓰이니 고유한 값을 넣어 주세요.", MessageType.Warning);
            }

            EditorGUILayout.PropertyField(_channelProperty);
            if (!_channelProperty.hasMultipleDifferentValues && _channelProperty.intValue == (int)SoundChannel.Master)
            {
                EditorGUILayout.HelpBox("Master 는 클립을 가질 수 없습니다. BGM, SFX, Voice 중에서 고르세요.", MessageType.Error);
            }

            EditorGUILayout.PropertyField(_clipProperty);

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(_defaultVolumeProperty);
            bool volumeChanged = EditorGUI.EndChangeCheck();

            serializedObject.ApplyModifiedProperties();

            //== 미리듣기와 클립 정보는 단일 선택일 때만 의미가 있습니다.
            if (targets.Length > 1)
            {
                return;
            }

            AudioClip clip = GetClip();
            if (volumeChanged && clip != null)
            {
                SoundEditorUtility.SetPreviewVolume(clip, _defaultVolumeProperty.floatValue);
            }

            EditorGUILayout.Space(10f);
            DrawPreview(clip);

            if (clip != null)
            {
                DrawClipInfo(clip);
            }
        }

        #endregion

        #region Private Helpers - Drawing

        private static void DrawHeader(string text, Color color)
        {
            if (_headerStyle == null)
            {
                _headerStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 12
                };
            }

            Rect rect = EditorGUILayout.GetControlRect(false, HeaderHeight);
            EditorGUI.DrawRect(rect, new Color(color.r, color.g, color.b, 0.3f));
            EditorGUI.LabelField(rect, text, _headerStyle);
        }

        private void DrawPreview(AudioClip clip)
        {
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

            if (clip == null)
            {
                EditorGUILayout.HelpBox("AudioClip 을 지정하면 미리 들을 수 있습니다.", MessageType.Info);
                return;
            }

            bool isPlaying = SoundEditorUtility.IsPreviewPlaying(clip);

            using (new EditorGUILayout.HorizontalScope())
            {
                Color previous = GUI.backgroundColor;
                if (isPlaying)
                {
                    GUI.backgroundColor = _playingColor;
                }

                if (GUILayout.Button(isPlaying ? "● Playing" : "▶ Play", GUILayout.Height(PreviewButtonHeight)))
                {
                    if (isPlaying)
                    {
                        SoundEditorUtility.StopAllPreviews();
                    }
                    else
                    {
                        //== 설정한 기본 볼륨 그대로 들어야 값을 고르는 의미가 있으므로 볼륨을 함께 넘깁니다.
                        SoundEditorUtility.PlayPreview(clip, _loopPreview, _defaultVolumeProperty.floatValue);
                    }
                }

                GUI.backgroundColor = previous;

                using (new EditorGUI.DisabledScope(!isPlaying))
                {
                    if (GUILayout.Button("■ Stop", GUILayout.Height(PreviewButtonHeight), GUILayout.Width(80f)))
                    {
                        SoundEditorUtility.StopAllPreviews();
                    }
                }

                _loopPreview = GUILayout.Toggle(_loopPreview, "Loop", GUILayout.Width(50f));
            }
        }

        private static void DrawClipInfo(AudioClip clip)
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Clip Info", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.LabelField("Length", $"{clip.length:F2}s");
                EditorGUILayout.LabelField("Channels", clip.channels.ToString());
                EditorGUILayout.LabelField("Frequency", $"{clip.frequency} Hz");
                EditorGUILayout.LabelField("Load Type", clip.loadType.ToString());
            }
        }

        #endregion

        #region Private Helpers - Misc

        private AudioClip GetClip()
        {
            SoundDefinition definition = target as SoundDefinition;
            return definition != null ? definition.Clip : null;
        }

        private void OnEditorUpdate()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - _lastRepaintTime < RepaintInterval)
            {
                return;
            }

            _lastRepaintTime = now;

            //== 재생 중일 때와, 재생이 막 끝난 한 번은 다시 그려야 버튼이 Play 로 돌아옵니다. 재생 중에만 그리면 마지막 상태 변화를 놓칩니다.
            AudioClip clip = GetClip();
            bool isPlaying = clip != null && SoundEditorUtility.IsPreviewPlaying(clip);
            if (isPlaying || _wasPlaying)
            {
                Repaint();
            }

            _wasPlaying = isPlaying;
        }

        #endregion
    }
}
