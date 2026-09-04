using UnityEditor;
using UnityEngine;

using Cocoa.Lib.Util;

namespace Cocoa.Lib.Editor
{
    /// <summary>
    /// ReadOnlyAttribute 가 붙은 필드를 인스펙터에서 비활성 상태로 그립니다.
    /// </summary>
    [CustomPropertyDrawer(typeof(ReadOnlyAttribute))]
    public sealed class ReadOnlyAttributeDrawer : PropertyDrawer
    {
        #region Public API

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUI.GetPropertyHeight(property, label, true);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            ReadOnlyAttribute target = (ReadOnlyAttribute)attribute;
            bool editable = target.RuntimeOnly && !Application.isPlaying;

            //== 바깥 스코프가 이미 비활성일 수 있으므로 이전 상태를 덮어쓰지 않고 복원합니다.
            bool previous = GUI.enabled;
            GUI.enabled = previous && editable;
            EditorGUI.PropertyField(position, property, label, true);
            GUI.enabled = previous;
        }

        #endregion
    }
}
