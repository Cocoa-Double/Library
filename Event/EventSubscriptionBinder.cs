using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cocoa.Lib.Event
{
    /// <summary>
    /// GameObject 수명에 구독 토큰을 묶어두는 내부 컴포넌트.
    /// EventBus.SubscribeFor 가 대상 GameObject에 알아서 붙이므로 직접 추가할 일은 없다.
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    internal sealed class EventSubscriptionBinder : MonoBehaviour
    {
        #region Fields

        private readonly List<IDisposable> _tokens = new List<IDisposable>();

        #endregion

        #region Public API

        public void Bind(IDisposable token)
        {
            if (token == null)
            {
                return;
            }

            _tokens.Add(token);
        }

        #endregion

        #region Unity Messages

        private void OnDestroy()
        {
            for (int i = 0; i < _tokens.Count; i++)
            {
                _tokens[i].Dispose();
            }

            _tokens.Clear();
        }

        #endregion
    }
}
