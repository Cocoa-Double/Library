using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

using Cocoa.Lib.Util;

namespace Cocoa.Lib.Editor
{
    /// <summary>
    /// ShowIfAttribute 의 조건을 판정해 필드를 그리거나 숨깁니다.
    /// </summary>
    /// <remarks>
    /// MyBox 의 ConditionalField 를 참고해 작성했습니다.
    /// https://github.com/Deadcows/MyBox
    /// </remarks>
    [CustomPropertyDrawer(typeof(ShowIfAttribute))]
    public sealed class ShowIfAttributeDrawer : PropertyDrawer
    {
        #region Constants

        private const string ArrayElementMarker = ".Array.data[";

        #endregion

        #region Static Fields

        //== 조건 필드를 못 찾은 경고는 다시 그릴 때마다 반복되므로 필드 경로당 한 번만 출력합니다.
        private static readonly HashSet<string> _warnedPaths = new HashSet<string>();

        #endregion

        #region Public API

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!IsVisible(property))
            {
                //== 기본 인스펙터는 필드 사이에 간격을 더하므로, 숨긴 필드는 그 간격만큼 빼서 빈 줄이 남지 않게 합니다.
                return -EditorGUIUtility.standardVerticalSpacing;
            }

            return EditorGUI.GetPropertyHeight(property, label, true);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            //== 배열 요소들은 드로어 인스턴스 하나를 공유하므로 높이 계산 때의 판정을 저장해 두지 않고 매번 다시 판정합니다.
            if (!IsVisible(property))
            {
                return;
            }

            //== 같은 프로퍼티로 다시 그리면 Unity 가 다음 순서의 드로어(다른 어트리뷰트나 타입 드로어)로 넘겨 줍니다.
            EditorGUI.PropertyField(position, property, label, true);
        }

        #endregion

        #region Private Helpers

        private bool IsVisible(SerializedProperty property)
        {
            ShowIfAttribute target = (ShowIfAttribute)attribute;
            SerializedProperty condition = FindSibling(property, target.FieldName);
            if (condition == null)
            {
                //== 이름 오타로 필드가 통째로 사라져 설정을 놓치는 쪽이 더 위험하므로 찾지 못하면 숨기지 않습니다.
                WarnMissingField(property, target.FieldName);
                return true;
            }

            bool matched = Matches(condition, target.CompareValues);
            return matched != target.Inverse;
        }

        private static SerializedProperty FindSibling(SerializedProperty property, string fieldName)
        {
            if (string.IsNullOrEmpty(fieldName))
            {
                return null;
            }

            string path = property.propertyPath;

            //== 리스트 필드에 붙은 드로어는 요소마다 불리므로 요소 경로를 걷어내 리스트 필드 자신의 경로로 되돌립니다.
            if (path.EndsWith("]", StringComparison.Ordinal))
            {
                int marker = path.LastIndexOf(ArrayElementMarker, StringComparison.Ordinal);
                if (marker >= 0)
                {
                    path = path.Substring(0, marker);
                }
            }

            //== 마지막 경로 조각만 바꾸면 중첩 클래스나 리스트 요소 안에서도 같은 깊이의 필드를 찾을 수 있습니다.
            int lastDot = path.LastIndexOf('.');
            string siblingPath = lastDot < 0 ? fieldName : path.Substring(0, lastDot + 1) + fieldName;
            return property.serializedObject.FindProperty(siblingPath);
        }

        private static bool Matches(SerializedProperty condition, object[] compareValues)
        {
            if (compareValues.Length == 0)
            {
                return HasValue(condition);
            }

            for (int i = 0; i < compareValues.Length; i++)
            {
                if (MatchesValue(condition, compareValues[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasValue(SerializedProperty property)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Boolean:
                    return property.boolValue;
                case SerializedPropertyType.Integer:
                    return property.longValue != 0;
                case SerializedPropertyType.Enum:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.Character:
                    return property.intValue != 0;
                case SerializedPropertyType.Float:
                    return property.doubleValue != 0d;
                case SerializedPropertyType.String:
                    return !string.IsNullOrEmpty(property.stringValue);
                case SerializedPropertyType.ObjectReference:
                    return property.objectReferenceValue != null;
                case SerializedPropertyType.ManagedReference:
                    return !string.IsNullOrEmpty(property.managedReferenceFullTypename);
                default:
                    //== 판정할 수 없는 타입은 숨기지 않습니다.
                    return true;
            }
        }

        private static bool MatchesValue(SerializedProperty property, object expected)
        {
            if (expected == null)
            {
                return !HasValue(property);
            }

            switch (property.propertyType)
            {
                case SerializedPropertyType.Enum:
                    return MatchesEnum(property.intValue, expected);
                case SerializedPropertyType.Boolean:
                    return expected is bool flag && property.boolValue == flag;
                case SerializedPropertyType.Integer:
                    return IsInteger(expected) && property.longValue == Convert.ToInt64(expected);
                case SerializedPropertyType.Float:
                    return (expected is float || expected is double || IsInteger(expected))
                        && Mathf.Approximately((float)property.doubleValue, Convert.ToSingle(expected));
                case SerializedPropertyType.String:
                    return expected is string text && string.Equals(property.stringValue, text, StringComparison.Ordinal);
                case SerializedPropertyType.ObjectReference:
                    return ReferenceEquals(property.objectReferenceValue, expected);
                default:
                    return false;
            }
        }

        private static bool MatchesEnum(long actual, object expected)
        {
            if (expected is Enum enumValue)
            {
                long wanted = Convert.ToInt64(enumValue);
                if (enumValue.GetType().IsDefined(typeof(FlagsAttribute), false))
                {
                    //== 0 은 비트 AND 로 판정할 수 없으므로 '아무 플래그도 없음' 과의 정확한 일치로 다룹니다.
                    return wanted == 0 ? actual == 0 : (actual & wanted) != 0;
                }

                return actual == wanted;
            }

            //== enum 필드를 정수 상수로 비교하는 경우도 값으로 비교합니다.
            return IsInteger(expected) && actual == Convert.ToInt64(expected);
        }

        private static bool IsInteger(object value)
        {
            return value is int || value is long || value is short || value is byte
                || value is sbyte || value is ushort || value is uint;
        }

        private static void WarnMissingField(SerializedProperty property, string fieldName)
        {
            UnityEngine.Object owner = property.serializedObject.targetObject;
            string ownerName = owner != null ? owner.GetType().Name : "Unknown";
            string key = ownerName + "/" + property.propertyPath;
            if (!_warnedPaths.Add(key))
            {
                return;
            }

            Log.Warning($"[ShowIf] {ownerName}.{property.propertyPath} 의 조건 필드 '{fieldName}' 를 찾을 수 없습니다.", LogColor.Yellow);
        }

        #endregion
    }
}
