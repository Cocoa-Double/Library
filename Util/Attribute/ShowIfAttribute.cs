using System;
using UnityEngine;

namespace Cocoa.Lib.Util
{
    /// <summary>
    /// 같은 객체 안의 다른 필드 값에 따라 인스펙터에서 필드를 보이거나 숨깁니다.
    /// 실제 그리기는 에디터 어셈블리의 ShowIfAttributeDrawer 가 담당합니다.
    /// </summary>
    /// <remarks>
    /// 비교 값을 주지 않으면 조건 필드가 기본값(false, 0, null, 빈 문자열)이 아닐 때 보입니다.
    /// 비교 값을 주면 그중 하나와 일치할 때 보이며, [Flags] enum 은 비트가 하나라도 겹치면 일치로 봅니다.
    /// <code>
    /// [ShowIf("_useLimit")] public int Limit;
    /// [ShowIf("_mode", Mode.A, Mode.B)] public float Speed;
    /// [ShowIf("_mode", Mode.None, Inverse = true)] public Transform Target;
    /// </code>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public sealed class ShowIfAttribute : PropertyAttribute
    {
        #region Fields

        private readonly string _fieldName;
        private readonly object[] _compareValues;

        #endregion

        #region Properties

        /// <summary>
        /// 조건으로 삼을 필드의 직렬화 이름. 같은 객체(중첩 클래스라면 같은 깊이)의 필드여야 합니다.
        /// </summary>
        public string FieldName
        {
            get { return _fieldName; }
        }

        /// <summary>
        /// 조건 필드와 비교할 값 목록. 비어 있으면 기본값 여부로 판정합니다.
        /// </summary>
        public object[] CompareValues
        {
            get { return _compareValues; }
        }

        /// <summary>
        /// true 이면 판정을 뒤집어 조건이 맞을 때 숨깁니다.
        /// </summary>
        public bool Inverse { get; set; }

        #endregion

        #region Constructors

        public ShowIfAttribute(string fieldName, params object[] compareValues)
        {
            _fieldName = fieldName;
            _compareValues = compareValues ?? Array.Empty<object>();
        }

        #endregion
    }
}
