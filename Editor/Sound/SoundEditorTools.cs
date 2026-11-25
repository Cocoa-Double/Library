using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

using Cocoa.Lib.Sound;
using Cocoa.Lib.Util;

namespace Cocoa.Lib.Editor.Sound
{
    /// <summary>
    /// AudioClip 에서 SoundDefinition 을 만드는 공용 도구입니다. 메뉴, 임포트 후처리, 라이브러리 창이 함께 씁니다.
    /// </summary>
    /// <remarks>
    /// 채널과 ID 는 파일 이름 접두사로 정합니다. bgm_main 은 BGM 채널의 main, sfx_click 은 SFX 채널의 click 이 됩니다.
    /// 생성된 에셋은 클립 폴더(또는 지정한 폴더) 아래 SoundDefinitions 하위 폴더에 모읍니다.
    /// </remarks>
    public static class SoundEditorTools
    {
        #region Nested Types

        private readonly struct PrefixRule
        {
            public readonly string Prefix;
            public readonly SoundChannel Channel;

            public PrefixRule(string prefix, SoundChannel channel)
            {
                Prefix = prefix;
                Channel = channel;
            }
        }

        #endregion

        #region Constants

        /// <summary>SoundDefinition 에셋을 모아 두는 하위 폴더 이름.</summary>
        public const string DefinitionsFolderName = "SoundDefinitions";

        /// <summary>생성되는 에셋 파일 이름 접두사.</summary>
        public const string AssetNamePrefix = "Sound_";

        #endregion

        #region Static Fields

        //== 채널 추론과 ID 추출이 같은 표를 써야 접두사가 한쪽에만 등록되어 어긋나는 일이 없습니다.
        private static readonly PrefixRule[] _prefixRules =
        {
            new PrefixRule("bgm_", SoundChannel.BGM),
            new PrefixRule("bgm-", SoundChannel.BGM),
            new PrefixRule("music_", SoundChannel.BGM),
            new PrefixRule("voice_", SoundChannel.Voice),
            new PrefixRule("voc_", SoundChannel.Voice),
            new PrefixRule("vo_", SoundChannel.Voice),
            new PrefixRule("sfx_", SoundChannel.SFX),
            new PrefixRule("sfx-", SoundChannel.SFX)
        };

        #endregion

        #region Public API - Naming

        /// <summary>클립 이름의 접두사로 채널을 추론합니다. 알 수 없는 접두사는 SFX 로 봅니다.</summary>
        public static SoundChannel InferChannel(string clipName)
        {
            int index = FindPrefixRule(clipName);
            return index >= 0 ? _prefixRules[index].Channel : SoundChannel.SFX;
        }

        /// <summary>클립 이름에서 채널 접두사를 떼어 ID 로 쓸 이름을 반환합니다.</summary>
        public static string ExtractId(string clipName)
        {
            if (string.IsNullOrEmpty(clipName))
            {
                return string.Empty;
            }

            int index = FindPrefixRule(clipName);
            return index >= 0 ? clipName.Substring(_prefixRules[index].Prefix.Length) : clipName;
        }

        /// <summary>클립으로 만들어질 SoundDefinition 의 에셋 경로를 반환합니다.</summary>
        public static string GetDefinitionPath(AudioClip clip, string parentFolder)
        {
            return $"{NormalizePath(parentFolder)}/{DefinitionsFolderName}/{AssetNamePrefix}{ExtractId(clip.name)}.asset";
        }

        #endregion

        #region Public API - Paths

        /// <summary>
        /// 경로 구분자를 '/' 로 통일하고 끝의 '/' 를 뗍니다.
        /// System.IO.Path 는 Windows 에서 '\' 를 돌려주는데, AssetDatabase 경로와 섞이면 같은 폴더도 다른 문자열이 되어 비교가 어긋납니다.
        /// </summary>
        public static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            return path.Replace('\\', '/').TrimEnd('/');
        }

        /// <summary>에셋 경로의 부모 폴더 경로를 '/' 구분자로 반환합니다.</summary>
        public static string GetParentFolder(string assetPath)
        {
            string normalized = NormalizePath(assetPath);
            int slash = normalized.LastIndexOf('/');
            return slash > 0 ? normalized.Substring(0, slash) : normalized;
        }

        /// <summary>부모 폴더 아래 SoundDefinitions 폴더를 확보하고 경로를 반환합니다. 부모 폴더가 없으면 null.</summary>
        public static string EnsureDefinitionsFolder(string parentFolder)
        {
            string parent = NormalizePath(parentFolder);
            if (!AssetDatabase.IsValidFolder(parent))
            {
                Log.Warning($"[SoundEditorTools] '{parent}' 는 유효한 폴더가 아닙니다.", LogColor.Yellow);
                return null;
            }

            string folder = $"{parent}/{DefinitionsFolderName}";
            if (!AssetDatabase.IsValidFolder(folder))
            {
                AssetDatabase.CreateFolder(parent, DefinitionsFolderName);
            }

            return folder;
        }

        #endregion

        #region Public API - Creation

        /// <summary>
        /// 클립 하나로 SoundDefinition 을 만들어 parentFolder/SoundDefinitions 에 저장합니다.
        /// 같은 이름의 에셋이 이미 있으면 새로 만들지 않고 기존 것을 반환합니다.
        /// </summary>
        /// <param name="clip">원본 클립.</param>
        /// <param name="parentFolder">SoundDefinitions 폴더를 둘 부모 폴더. 비어 있으면 클립이 있는 폴더를 씁니다.</param>
        /// <param name="channelOverride">채널을 직접 지정합니다. null 이면 이름으로 추론합니다.</param>
        public static SoundDefinition CreateDefinition(AudioClip clip, string parentFolder = null, SoundChannel? channelOverride = null)
        {
            if (clip == null)
            {
                return null;
            }

            if (string.IsNullOrEmpty(parentFolder))
            {
                parentFolder = GetParentFolder(AssetDatabase.GetAssetPath(clip));
            }

            //== 다시 실행해도 같은 클립의 에셋이 _1, _2 로 불어나지 않도록 기존 에셋을 돌려줍니다.
            string assetPath = GetDefinitionPath(clip, parentFolder);
            SoundDefinition existing = AssetDatabase.LoadAssetAtPath<SoundDefinition>(assetPath);
            if (existing != null)
            {
                return existing;
            }

            if (EnsureDefinitionsFolder(parentFolder) == null)
            {
                return null;
            }

            SoundDefinition definition = ScriptableObject.CreateInstance<SoundDefinition>();

            //== 필드가 private 이라 SerializedObject 로 채웁니다. enum 은 인덱스가 아닌 값으로 넣어야 멤버 순서가 바뀌어도 안전합니다.
            SerializedObject serialized = new SerializedObject(definition);
            serialized.FindProperty("_id").stringValue = ExtractId(clip.name);
            serialized.FindProperty("_channel").intValue = (int)(channelOverride ?? InferChannel(clip.name));
            serialized.FindProperty("_clip").objectReferenceValue = clip;
            serialized.FindProperty("_defaultVolume").floatValue = 1f;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.CreateAsset(definition, assetPath);
            return definition;
        }

        /// <summary>
        /// 여러 클립으로 SoundDefinition 을 만들고, 만들었거나 이미 있던 에셋 목록을 반환합니다.
        /// </summary>
        /// <param name="parentFolder">비어 있으면 클립마다 자기 폴더를 씁니다.</param>
        public static List<SoundDefinition> CreateDefinitions(IList<AudioClip> clips, string parentFolder = null)
        {
            List<SoundDefinition> results = new List<SoundDefinition>();
            if (clips == null || clips.Count == 0)
            {
                return results;
            }

            //== StartAssetEditing 으로 묶으면 같은 배치에서 만든 폴더가 IsValidFolder 에 잡히지 않아 폴더가 중복 생성되므로 하나씩 만듭니다.
            for (int i = 0; i < clips.Count; i++)
            {
                SoundDefinition definition = CreateDefinition(clips[i], parentFolder);
                if (definition != null)
                {
                    results.Add(definition);
                }
            }

            //== 디스크 기록은 마지막에 한 번만 합니다.
            AssetDatabase.SaveAssets();
            return results;
        }

        #endregion

        #region Public API - Relocation

        /// <summary>
        /// 라이브러리에 추가할 SoundDefinition 이 라이브러리 폴더 밖에 있으면 옮길지 묻습니다.
        /// 옮겼거나 그대로 두기로 했으면 true, 취소했으면 false 를 반환합니다.
        /// </summary>
        public static bool PromptRelocation(SoundDefinition definition, string libraryFolder)
        {
            if (definition == null || string.IsNullOrEmpty(libraryFolder))
            {
                return false;
            }

            string currentPath = AssetDatabase.GetAssetPath(definition);
            if (string.IsNullOrEmpty(currentPath))
            {
                return false;
            }

            string targetFolder = $"{NormalizePath(libraryFolder)}/{DefinitionsFolderName}";
            if (string.Equals(GetParentFolder(currentPath), targetFolder, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            int choice = EditorUtility.DisplayDialogComplex(
                "SoundDefinition 위치",
                $"'{definition.name}' 이(가) 라이브러리 폴더 밖에 있습니다.\n\n현재: {currentPath}\n라이브러리: {targetFolder}\n\n라이브러리 폴더로 옮길까요?",
                "옮기기",
                "취소",
                "그대로 두기");

            if (choice == 1)
            {
                return false;
            }

            if (choice == 2)
            {
                return true;
            }

            if (EnsureDefinitionsFolder(libraryFolder) == null)
            {
                return false;
            }

            string newPath = AssetDatabase.GenerateUniqueAssetPath($"{targetFolder}/{definition.name}.asset");
            string error = AssetDatabase.MoveAsset(currentPath, newPath);
            if (!string.IsNullOrEmpty(error))
            {
                Log.Error($"[SoundEditorTools] '{currentPath}' 이동 실패: {error}", LogColor.Red);
                return false;
            }

            return true;
        }

        #endregion

        #region Private Helpers

        private static int FindPrefixRule(string clipName)
        {
            if (string.IsNullOrEmpty(clipName))
            {
                return -1;
            }

            for (int i = 0; i < _prefixRules.Length; i++)
            {
                //== 대소문자만 다른 BGM_Main 도 같은 규칙에 걸리도록 하되, 현재 문화권의 대소문자 규칙에는 영향을 받지 않게 합니다.
                if (clipName.StartsWith(_prefixRules[i].Prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        #endregion
    }
}
