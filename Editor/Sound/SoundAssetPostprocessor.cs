using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

using Cocoa.Lib.Util;

namespace Cocoa.Lib.Editor.Sound
{
    /// <summary>
    /// 감시 폴더에 AudioClip 이 임포트되면 SoundDefinition 을 자동으로 만듭니다.
    /// 기본은 꺼져 있으며 Preferences > Cocoa Lib > Sound 에서 켜고 감시 폴더를 지정합니다.
    /// </summary>
    public sealed class SoundAssetPostprocessor : AssetPostprocessor
    {
        #region Constants

        private const string EnabledKey = "Cocoa.Lib.Sound.AutoDefinition.Enabled";
        private const string WatchFolderKey = "Cocoa.Lib.Sound.AutoDefinition.WatchFolder";
        private const string DefaultWatchFolder = "Assets/Sounds";

        #endregion

        #region Properties

        /// <summary>자동 생성 사용 여부. 사용자별 설정이라 EditorPrefs 에 저장합니다.</summary>
        public static bool IsEnabled
        {
            get { return EditorPrefs.GetBool(EnabledKey, false); }
            set { EditorPrefs.SetBool(EnabledKey, value); }
        }

        /// <summary>감시할 폴더. 이 폴더와 그 하위에 임포트된 클립만 처리합니다.</summary>
        public static string WatchFolder
        {
            get { return EditorPrefs.GetString(WatchFolderKey, DefaultWatchFolder); }
            set { EditorPrefs.SetString(WatchFolderKey, SoundEditorTools.NormalizePath(value)); }
        }

        #endregion

        #region AssetPostprocessor Callbacks

        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (!IsEnabled)
            {
                return;
            }

            string watchFolder = SoundEditorTools.NormalizePath(WatchFolder);
            if (string.IsNullOrEmpty(watchFolder))
            {
                return;
            }

            //== 'Assets/Sounds' 가 'Assets/SoundsBackup' 까지 잡지 않도록 구분자를 붙여 비교합니다.
            string watchPrefix = watchFolder + "/";
            List<AudioClip> clips = null;

            for (int i = 0; i < importedAssets.Length; i++)
            {
                string path = importedAssets[i];
                if (!path.StartsWith(watchPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                //== 확장자 목록을 따로 두지 않고 임포터 타입으로 판단해 Unity 가 지원하는 오디오 형식을 모두 받습니다.
                if (!(AssetImporter.GetAtPath(path) is AudioImporter))
                {
                    continue;
                }

                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null)
                {
                    continue;
                }

                //== 재임포트될 때마다 다시 만들지 않도록 이미 있는 것은 건너뜁니다.
                string definitionPath = SoundEditorTools.GetDefinitionPath(clip, SoundEditorTools.GetParentFolder(path));
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(definitionPath) != null)
                {
                    continue;
                }

                if (clips == null)
                {
                    clips = new List<AudioClip>();
                }

                clips.Add(clip);
            }

            if (clips == null)
            {
                return;
            }

            SoundEditorTools.CreateDefinitions(clips);
            Log.Info($"[SoundAssetPostprocessor] SoundDefinition {clips.Count}개를 자동으로 만들었습니다.");
        }

        #endregion

        #region Settings

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new SettingsProvider("Preferences/Cocoa Lib/Sound", SettingsScope.User)
            {
                label = "Sound",
                keywords = new HashSet<string> { "Sound", "Audio", "SoundDefinition", "Cocoa" },
                guiHandler = searchContext =>
                {
                    EditorGUILayout.LabelField("SoundDefinition 자동 생성", EditorStyles.boldLabel);
                    IsEnabled = EditorGUILayout.Toggle("Enabled", IsEnabled);

                    using (new EditorGUI.DisabledScope(!IsEnabled))
                    {
                        EditorGUI.BeginChangeCheck();
                        string watchFolder = EditorGUILayout.DelayedTextField("Watch Folder", WatchFolder);
                        if (EditorGUI.EndChangeCheck())
                        {
                            WatchFolder = watchFolder;
                        }

                        if (IsEnabled && !AssetDatabase.IsValidFolder(WatchFolder))
                        {
                            EditorGUILayout.HelpBox($"'{WatchFolder}' 폴더가 없습니다.", MessageType.Warning);
                        }

                        EditorGUILayout.HelpBox(
                            $"감시 폴더에 임포트된 AudioClip 옆의 '{SoundEditorTools.DefinitionsFolderName}' 폴더에 SoundDefinition 을 만듭니다.\n" +
                            "채널은 파일 이름 접두사로 정합니다.\n" +
                            "• bgm_, bgm-, music_ → BGM\n" +
                            "• voice_, voc_, vo_ → Voice\n" +
                            "• 그 외 → SFX",
                            MessageType.Info);
                    }
                }
            };
        }

        #endregion
    }
}
