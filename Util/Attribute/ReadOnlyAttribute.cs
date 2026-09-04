using System;
using UnityEngine;

namespace Cocoa.Lib.Util
{
    /// <summary>
    /// 인스펙터에서 필드를 읽기 전용으로 표시합니다.
    /// 실제 그리기는 에디터 어셈블리의 ReadOnlyAttributeDrawer 가 담당합니다.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class ReadOnlyAttribute : PropertyAttribute
    {
        #region Fields

        private readonly bool _runtimeOnly;

        #endregion

        #region Properties

        /// <summary>
        /// true 이면 플레이 모드에서만 잠기고 에디트 모드에서는 편집할 수 있습니다.
        /// false 이면 항상 잠깁니다.
        /// </summary>
        public bool RuntimeOnly
        {
            get { return _runtimeOnly; }
        }

        #endregion

        #region Constructors

        public ReadOnlyAttribute(bool runtimeOnly = false)
        {
            _runtimeOnly = runtimeOnly;
        }

        #endregion
    }
}
