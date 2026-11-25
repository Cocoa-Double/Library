using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

using Cocoa.Lib.Util;

namespace Cocoa.Lib.Editor.Sound
{
    /// <summary>
    /// 에디터에서 AudioClip 을 미리 듣는 유틸리티입니다. 단일 재생과 여러 클립의 순차 재생을 지원합니다.
    /// </summary>
    /// <remarks>
    /// Project 창과 같은 내부 API(UnityEditor.AudioUtility)를 리플렉션으로 먼저 쓰고,
    /// 버전 차이로 찾지 못하면 숨김 GameObject 의 AudioSource 로 재생합니다.
    /// 볼륨 조절은 내부 API 가 지원하지 않아 대체 경로에서만 적용됩니다.
    /// </remarks>
    [InitializeOnLoad]
    public static class SoundEditorUtility
    {
        #region Constants

        //== 순차 재생에서 클립 사이에 두는 간격(초). 앞 클립의 꼬리와 다음 클립의 시작이 겹쳐 들리지 않게 합니다.
        private const double SequenceGapSeconds = 0.05;

        private const string FallbackObjectName = "__CocoaSoundPreview__";

        #endregion

        #region Static Fields

        //== 내부 API 경로
        private static MethodInfo _playMethod;
        private static MethodInfo _stopMethod;
        private static MethodInfo _isPlayingMethod;
        private static int _isPlayingParameterCount;
        private static bool _reflectionInitialized;
        private static bool _reflectionAvailable;

        //== 대체 경로
        private static GameObject _fallbackObject;
        private static AudioSource _fallbackSource;

        private static AudioClip _currentClip;

        //== 순차 재생
        private static readonly List<AudioClip> _sequenceClips = new List<AudioClip>();
        private static int _sequenceIndex;
        private static double _sequenceClipEndTime;
        private static bool _isSequenceActive;

        #endregion

        #region Properties

        /// <summary>마지막으로 재생을 요청한 클립. 정지하면 null 입니다.</summary>
        public static AudioClip CurrentPreviewClip
        {
            get { return _currentClip; }
        }

        /// <summary>순차 재생 중인지 여부.</summary>
        public static bool IsSequenceActive
        {
            get { return _isSequenceActive; }
        }

        /// <summary>순차 재생의 현재 위치 (0 부터). 순차 재생 중이 아니면 -1.</summary>
        public static int SequenceCurrentIndex
        {
            get { return _isSequenceActive ? _sequenceIndex : -1; }
        }

        /// <summary>순차 재생의 전체 클립 수. 순차 재생 중이 아니면 0.</summary>
        public static int SequenceTotalCount
        {
            get { return _isSequenceActive ? _sequenceClips.Count : 0; }
        }

        #endregion

        #region Constructors

        static SoundEditorUtility()
        {
            //== 숨김 오브젝트를 정리하지 않으면 도메인 리로드마다 누수되고, 플레이 모드에 들어가면 AudioListener 가 둘이 됩니다.
            AssemblyReloadEvents.beforeAssemblyReload += DestroyFallback;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        #endregion

        #region Public API

        /// <summary>클립을 재생합니다. 진행 중인 단일 재생과 순차 재생은 모두 멈춥니다.</summary>
        public static void PlayPreview(AudioClip clip, bool loop = false, float volume = 1f)
        {
            if (clip == null)
            {
                return;
            }

            StopAllPreviews();
            PlaySingle(clip, loop, volume);
        }

        /// <summary>단일 재생과 순차 재생을 모두 멈춥니다.</summary>
        public static void StopAllPreviews()
        {
            StopSequence();
            StopSingle();
        }

        /// <summary>해당 클립이 재생 중인지 확인합니다.</summary>
        public static bool IsPreviewPlaying(AudioClip clip)
        {
            if (clip == null)
            {
                return false;
            }

            if (_fallbackSource != null && _fallbackSource.clip == clip && _fallbackSource.isPlaying)
            {
                return true;
            }

            if (!_reflectionAvailable || _isPlayingMethod == null || _currentClip != clip)
            {
                return false;
            }

            try
            {
                //== 신버전은 인자 없이 '미리듣기 중인 것이 있는지' 를, 구버전은 클립을 받아 그 클립의 재생 여부를 돌려줍니다.
                object result = _isPlayingParameterCount == 0
                    ? _isPlayingMethod.Invoke(null, null)
                    : _isPlayingMethod.Invoke(null, new object[] { clip });
                return (bool)result;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>재생 중인 미리듣기의 볼륨을 바꿉니다. 대체 경로로 재생 중일 때만 반영됩니다.</summary>
        public static void SetPreviewVolume(AudioClip clip, float volume)
        {
            if (_fallbackSource != null && _fallbackSource.clip == clip)
            {
                _fallbackSource.volume = Mathf.Clamp01(volume);
            }
        }

        /// <summary>
        /// 여러 클립을 순서대로 재생합니다. 비어 있는 항목은 건너뜁니다.
        /// 진행 중인 재생은 모두 멈춘 뒤 시작합니다.
        /// </summary>
        public static void PlaySequence(IEnumerable<AudioClip> clips)
        {
            StopAllPreviews();

            if (clips == null)
            {
                return;
            }

            foreach (AudioClip clip in clips)
            {
                if (clip != null)
                {
                    _sequenceClips.Add(clip);
                }
            }

            if (_sequenceClips.Count == 0)
            {
                return;
            }

            _sequenceIndex = 0;
            _isSequenceActive = true;
            EditorApplication.update += UpdateSequence;

            PlayCurrentInSequence();
        }

        /// <summary>어느 재생 경로를 쓰는지 등 진단 정보를 문자열로 반환합니다. 버전 호환 문제를 확인할 때 씁니다.</summary>
        public static string Diagnose()
        {
            EnsureReflection();

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("[SoundEditorUtility] 진단");
            builder.AppendLine($"Unity: {Application.unityVersion}");
            builder.AppendLine($"내부 API 사용 가능: {_reflectionAvailable}");
            builder.AppendLine($"  Play: {FormatMethod(_playMethod)}");
            builder.AppendLine($"  Stop: {FormatMethod(_stopMethod)}");
            builder.AppendLine($"  IsPlaying: {FormatMethod(_isPlayingMethod)}");
            builder.AppendLine($"대체 경로 오브젝트: {(_fallbackObject != null ? "있음" : "없음")}");
            builder.AppendLine($"씬의 AudioListener: {(UnityEngine.Object.FindAnyObjectByType<AudioListener>() != null ? "있음" : "없음")}");
            builder.AppendLine($"순차 재생: {(_isSequenceActive ? $"{_sequenceIndex + 1}/{_sequenceClips.Count}" : "꺼짐")}");
            builder.Append($"현재 클립: {(_currentClip != null ? _currentClip.name : "없음")}");
            return builder.ToString();
        }

        #endregion

        #region Private Helpers - Single

        private static void PlaySingle(AudioClip clip, bool loop, float volume)
        {
            _currentClip = clip;

            //== 볼륨을 낮춰 들어야 하는 요청은 볼륨을 지원하는 대체 경로로 보냅니다.
            bool needsVolume = volume < 1f;
            if (!needsVolume && TryPlayWithReflection(clip, loop))
            {
                return;
            }

            PlayWithFallback(clip, loop, volume);
        }

        private static void StopSingle()
        {
            if (_reflectionAvailable && _stopMethod != null)
            {
                try
                {
                    _stopMethod.Invoke(null, null);
                }
                catch (Exception)
                {
                    //== 정지 실패는 다음 재생 요청에서 다시 정지를 시도하므로 무시합니다.
                }
            }

            if (_fallbackSource != null && _fallbackSource.isPlaying)
            {
                _fallbackSource.Stop();
            }

            _currentClip = null;
        }

        #endregion

        #region Private Helpers - Sequence

        private static void PlayCurrentInSequence()
        {
            if (_sequenceIndex >= _sequenceClips.Count)
            {
                StopSequence();
                return;
            }

            AudioClip clip = _sequenceClips[_sequenceIndex];
            _sequenceClipEndTime = EditorApplication.timeSinceStartup + clip.length + SequenceGapSeconds;

            StopSingle();
            PlaySingle(clip, false, 1f);
        }

        private static void UpdateSequence()
        {
            if (_isSequenceActive && EditorApplication.timeSinceStartup >= _sequenceClipEndTime)
            {
                _sequenceIndex++;
                PlayCurrentInSequence();
            }
        }

        private static void StopSequence()
        {
            EditorApplication.update -= UpdateSequence;
            _isSequenceActive = false;
            _sequenceClips.Clear();
            _sequenceIndex = 0;
        }

        #endregion

        #region Private Helpers - Reflection

        private static bool TryPlayWithReflection(AudioClip clip, bool loop)
        {
            if (!EnsureReflection())
            {
                return false;
            }

            try
            {
                _playMethod.Invoke(null, new object[] { clip, 0, loop });
                return true;
            }
            catch (Exception exception)
            {
                Log.Warning($"[SoundEditorUtility] 내부 API 재생에 실패해 대체 경로로 재생합니다. {exception.Message}", LogColor.Yellow);
                return false;
            }
        }

        private static bool EnsureReflection()
        {
            if (_reflectionInitialized)
            {
                return _reflectionAvailable;
            }

            _reflectionInitialized = true;

            try
            {
                Type audioUtility = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtility");
                if (audioUtility == null)
                {
                    return false;
                }

                //== 버전에 따라 이름이 다르므로 신버전 이름부터 차례로 찾습니다.
                _playMethod = FindMethod(audioUtility, new[] { "PlayPreviewClip", "PlayClip" }, new[] { typeof(AudioClip), typeof(int), typeof(bool) });
                _stopMethod = FindMethod(audioUtility, new[] { "StopAllPreviewClips", "StopAllClips" }, Type.EmptyTypes);
                _isPlayingMethod = FindBoolMethod(audioUtility, new[] { "IsPreviewClipPlaying", "IsClipPlaying" });
                _isPlayingParameterCount = _isPlayingMethod != null ? _isPlayingMethod.GetParameters().Length : 0;

                _reflectionAvailable = _playMethod != null && _stopMethod != null;
            }
            catch (Exception exception)
            {
                Log.Warning($"[SoundEditorUtility] 내부 API 를 찾지 못해 대체 경로를 씁니다. {exception.Message}", LogColor.Yellow);
                _reflectionAvailable = false;
            }

            return _reflectionAvailable;
        }

        private static MethodInfo FindMethod(Type type, string[] names, Type[] parameterTypes)
        {
            for (int i = 0; i < names.Length; i++)
            {
                MethodInfo method = type.GetMethod(names[i], BindingFlags.Public | BindingFlags.Static, null, parameterTypes, null);
                if (method != null)
                {
                    return method;
                }
            }

            return null;
        }

        private static MethodInfo FindBoolMethod(Type type, string[] names)
        {
            MethodInfo[] methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static);
            for (int i = 0; i < names.Length; i++)
            {
                for (int j = 0; j < methods.Length; j++)
                {
                    if (methods[j].Name == names[i] && methods[j].ReturnType == typeof(bool))
                    {
                        return methods[j];
                    }
                }
            }

            return null;
        }

        private static string FormatMethod(MethodInfo method)
        {
            if (method == null)
            {
                return "없음";
            }

            ParameterInfo[] parameters = method.GetParameters();
            string[] parts = new string[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                parts[i] = parameters[i].ParameterType.Name;
            }

            return $"{method.Name}({string.Join(", ", parts)})";
        }

        #endregion

        #region Private Helpers - Fallback

        private static void PlayWithFallback(AudioClip clip, bool loop, float volume)
        {
            EnsureFallback();

            _fallbackSource.clip = clip;
            _fallbackSource.loop = loop;
            _fallbackSource.volume = Mathf.Clamp01(volume);
            _fallbackSource.timeSamples = 0;
            _fallbackSource.Play();
        }

        private static void EnsureFallback()
        {
            if (_fallbackSource != null)
            {
                return;
            }

            _fallbackObject = new GameObject(FallbackObjectName)
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            _fallbackSource = _fallbackObject.AddComponent<AudioSource>();
            _fallbackSource.playOnAwake = false;
            _fallbackSource.spatialBlend = 0f;

            //== 에디트 모드의 씬에는 리스너가 없을 수 있어 그때만 직접 붙입니다.
            if (UnityEngine.Object.FindAnyObjectByType<AudioListener>() == null)
            {
                _fallbackObject.AddComponent<AudioListener>();
            }
        }

        private static void DestroyFallback()
        {
            StopAllPreviews();

            if (_fallbackObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_fallbackObject);
            }

            _fallbackObject = null;
            _fallbackSource = null;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode || change == PlayModeStateChange.ExitingPlayMode)
            {
                DestroyFallback();
            }
        }

        #endregion
    }
}
