using System;
using UnityEngine;

namespace Cocoa.Lib.Util
{
    /// <summary>
    /// 배열/리스트 요소의 인스펙터 라벨을 "Element 0" 대신 요소 안의 필드 값으로 표시합니다.
    /// 실제 그리기는 에디터 어셈블리의 ArrayTitleAttributeDrawer 가 담당합니다.
    /// </summary>
    /// <remarks>
    /// <code>
    /// [ArrayTitle("Name")] public List&lt;StageData&gt; Stages;
    /// </code>
    /// 값이 비어 있거나 표시할 수 없는 타입이면 원래 라벨을 그대로 씁니다.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public sealed class ArrayTitleAttribute : PropertyAttribute
    {
        #region Fields

        private readonly string _fieldName;

        #endregion

        #region Properties

        /// <summary>
        /// 라벨로 쓸 요소 내부 필드의 직렬화 이름.
        /// </summary>
        public string FieldName
        {
            get { return _fieldName; }
        }

        #endregion

        #region Constructors

        public ArrayTitleAttribute(string fieldName)
        {
            _fieldName = fieldName;
        }

        #endregion
    }
}
