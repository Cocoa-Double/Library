using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

using Cocoa.Lib.Sound;

namespace Cocoa.Lib.Editor.Sound
{
    /// <summary>
    /// Project 창 우클릭 메뉴에서 AudioClip 이나 폴더를 골라 SoundDefinition 을 만듭니다.
    /// </summary>
    public static class SoundAssetMenu
    {
        #region Constants

        private const string FromClipsMenu = "Assets/Create/Cocoa/Lib/Sound/SoundDefinition from AudioClip(s)";
        private const string FromFolderMenu = "Assets/Create/Cocoa/Lib/Sound/SoundDefinitions from Folder";

        #endregion

        #region Menu Items

        [MenuItem(FromClipsMenu, false, 100)]
        private static void CreateFromSelectedClips()
        {
            List<AudioClip> clips = GetSelectedClips();
            if (clips.Count == 0)
            {
                return;
            }

            //== 여러 폴더의 클립을 함께 골랐을 때도 각 클립 옆에 만들어지도록 부모 폴더를 지정하지 않습니다.
            List<SoundDefinition> created = SoundEditorTools.CreateDefinitions(clips);
            if (created.Count > 0)
            {
                Selection.objects = created.ToArray();
                EditorGUIUtility.PingObject(created[0]);
            }
        }

        [MenuItem(FromClipsMenu, true)]
        private static bool ValidateCreateFromSelectedClips()
        {
            Object[] selection = Selection.objects;
            for (int i = 0; i < selection.Length; i++)
            {
                if (selection[i] is AudioClip)
                {
                    return true;
                }
            }

            return false;
        }

        [MenuItem(FromFolderMenu, false, 101)]
        private static void CreateFromSelectedFolder()
        {
            string folder = GetSelectedFolder();
            if (folder == null)
            {
                return;
            }

            string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { folder });
            if (guids.Length == 0)
            {
                EditorUtility.DisplayDialog("SoundDefinition 생성", $"'{folder}' 에 AudioClip 이 없습니다.", "확인");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "SoundDefinition 생성",
                    $"'{folder}' 아래 AudioClip {guids.Length}개로 SoundDefinition 을 만듭니다.\n각 클립 폴더의 {SoundEditorTools.DefinitionsFolderName} 하위 폴더에 저장되며, 이미 있는 것은 건너뜁니다.",
                    "만들기",
                    "취소"))
            {
                return;
            }

            List<AudioClip> clips = new List<AudioClip>(guids.Length);
            for (int i = 0; i < guids.Length; i++)
            {
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (clip != null)
                {
                    clips.Add(clip);
                }
            }

            //== 하위 폴더의 클립까지 검색되므로 한 곳에 모으지 않고 각 클립의 폴더 옆에 만듭니다.
            List<SoundDefinition> created = SoundEditorTools.CreateDefinitions(clips);
            EditorUtility.DisplayDialog("SoundDefinition 생성", $"{created.Count}개를 준비했습니다.", "확인");
        }

        [MenuItem(FromFolderMenu, true)]
        private static bool ValidateCreateFromSelectedFolder()
        {
            return GetSelectedFolder() != null;
        }

        #endregion

        #region Private Helpers

        private static List<AudioClip> GetSelectedClips()
        {
            List<AudioClip> clips = new List<AudioClip>();
            Object[] selection = Selection.objects;
            for (int i = 0; i < selection.Length; i++)
            {
                AudioClip clip = selection[i] as AudioClip;
                if (clip != null)
                {
                    clips.Add(clip);
                }
            }

            return clips;
        }

        private static string GetSelectedFolder()
        {
            Object selected = Selection.activeObject;
            if (selected == null)
            {
                return null;
            }

            string path = AssetDatabase.GetAssetPath(selected);
            return AssetDatabase.IsValidFolder(path) ? path : null;
        }

        #endregion
    }
}
