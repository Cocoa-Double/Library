using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cocoa.Lib.Save
{
    /// <summary>
    /// Unity 내장 JsonUtility 를 쓰는 직렬화기입니다. 외부 의존이 없어 기본값으로 쓰입니다.
    /// </summary>
    /// <remarks>
    /// 대상은 [Serializable] 클래스/구조체이며 public 또는 [SerializeField] 필드만 직렬화됩니다.
    /// Dictionary, DateTime, 다형성, null 은 다룰 수 없으니 필요하면 NewtonsoftSerializer 를 씁니다.
    /// JsonUtility 가 최상위로 받지 못하는 기본형, 문자열, enum, 배열, List 는 한 겹 감싸서 저장하므로 Save("coins", 100) 처럼 써도 됩니다.
    /// </remarks>
    public sealed class JsonUtilitySerializer : ISaveSerializer
    {
        #region Nested Types

        //== JsonUtility 는 최상위가 객체여야 하므로 그렇지 않은 타입을 필드 하나짜리 객체로 감쌉니다.
        [Serializable]
        private sealed class Box<T>
        {
            public T Value;
        }

        private static class WrapCache<T>
        {
            //== 타입별로 한 번만 판정합니다.
            public static readonly bool NeedsWrap = ComputeNeedsWrap(typeof(T));
        }

        #endregion

        #region Fields

        private readonly bool _prettyPrint;

        #endregion

        #region Constructors

        /// <param name="prettyPrint">true 면 사람이 읽기 좋게 들여쓰기합니다. 파일 크기가 커지니 디버그용으로만 씁니다.</param>
        public JsonUtilitySerializer(bool prettyPrint = false)
        {
            _prettyPrint = prettyPrint;
        }

        #endregion

        #region Public API

        public string Serialize<T>(T value)
        {
            if (WrapCache<T>.NeedsWrap)
            {
                return JsonUtility.ToJson(new Box<T> { Value = value }, _prettyPrint);
            }

            return JsonUtility.ToJson(value, _prettyPrint);
        }

        public T Deserialize<T>(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return default;
            }

            if (WrapCache<T>.NeedsWrap)
            {
                Box<T> box = JsonUtility.FromJson<Box<T>>(raw);
                return box != null ? box.Value : default;
            }

            return JsonUtility.FromJson<T>(raw);
        }

        #endregion

        #region Private Helpers

        private static bool ComputeNeedsWrap(Type type)
        {
            //== 감싸지 않으면 ToJson(100) 은 "{}" 를 돌려주고, 다시 읽으면 0 이 됩니다. 오류 없이 값이 사라지므로 미리 막습니다.
            if (type.IsPrimitive || type.IsEnum || type.IsArray || type == typeof(string) || type == typeof(decimal))
            {
                return true;
            }

            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>);
        }

        #endregion
    }
}
