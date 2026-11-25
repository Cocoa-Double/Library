using System.Globalization;
using UnityEditor;
using UnityEngine;

using Cocoa.Lib.Util;

namespace Cocoa.Lib.Editor
{
    /// <summary>
    /// ArrayTitleAttribute 가 붙은 배열 요소를 지정한 필드 값을 라벨로 삼아 그립니다.
    /// </summary>
    [CustomPropertyDrawer(typeof(ArrayTitleAttribute))]
    public sealed class ArrayTitleAttributeDrawer : PropertyDrawer
    {
        #region Fields

        //== OnGUI 마다 GUIContent 를 새로 만들지 않도록 하나를 재사용합니다.
        private readonly GUIContent _titleContent = new GUIContent();

        #endregion

        #region Public API

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUI.GetPropertyHeight(property, label, true);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            ArrayTitleAttribute target = (ArrayTitleAttribute)attribute;

            //== 요소 기준 상대 경로로 찾아야 다른 요소나 같은 이름의 바깥 필드를 잘못 집지 않습니다.
            SerializedProperty titleProperty = string.IsNullOrEmpty(target.FieldName)
                ? null
                : property.FindPropertyRelative(target.FieldName);

            string title = titleProperty != null ? ToTitle(titleProperty) : null;
            if (string.IsNullOrEmpty(title))
            {
                EditorGUI.PropertyField(position, property, label, true);
                return;
            }

            _titleContent.text = title;
            _titleContent.tooltip = label.tooltip;
            _titleContent.image = label.image;
            EditorGUI.PropertyField(position, property, _titleContent, true);
        }

        #endregion

        #region Private Helpers

        private static string ToTitle(SerializedProperty property)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.String:
                    return property.stringValue;
                case SerializedPropertyType.Integer:
                    return property.longValue.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.Float:
                    return property.doubleValue.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.Boolean:
                    return property.boolValue ? "True" : "False";
                case SerializedPropertyType.Enum:
                    return ToEnumTitle(property);
                case SerializedPropertyType.ObjectReference:
                    //== ToString() 은 "이름 (타입)" 형태라 괄호를 잘라내야 했지만, name 을 쓰면 이름만 바로 얻습니다.
                    return property.objectReferenceValue != null ? property.objectReferenceValue.name : null;
                case SerializedPropertyType.Vector2:
                    return property.vector2Value.ToString();
                case SerializedPropertyType.Vector3:
                    return property.vector3Value.ToString();
                case SerializedPropertyType.Vector2Int:
                    return property.vector2IntValue.ToString();
                case SerializedPropertyType.Vector3Int:
                    return property.vector3IntValue.ToString();
                case SerializedPropertyType.Color:
                    return "#" + ColorUtility.ToHtmlStringRGBA(property.colorValue);
                default:
                    return null;
            }
        }

        private static string ToEnumTitle(SerializedProperty property)
        {
            //== [Flags] enum 에 여러 비트가 켜져 있으면 enumValueIndex 가 -1 이 되므로 인덱스로 이름을 꺼내기 전에 범위를 확인합니다.
            int index = property.enumValueIndex;
            string[] names = property.enumDisplayNames;
            if (index >= 0 && index < names.Length)
            {
                return names[index];
            }

            return property.intValue.ToString(CultureInfo.InvariantCulture);
        }

        #endregion
    }
}
