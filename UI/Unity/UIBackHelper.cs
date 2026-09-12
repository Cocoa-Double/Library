using System;
using UnityEngine;

using Cocoa.Lib.Util;

namespace Cocoa.Lib.UI
{
    /// <summary>
    /// 매 프레임 Back 트리거를 감지해 <see cref="UIStack.Back()"/> 을 호출하고, 그 결과에 따라 콜백을 발생시키는 헬퍼입니다.
    /// </summary>
    /// <remarks>
    /// 기본 트리거는 레거시 입력의 ESC 키이지만 <see cref="SetBackTrigger"/> 로 임의의 조건을 주입할 수 있습니다.
    /// 신 Input System 이나 모바일 뒤로가기 버튼을 연결할 때 씁니다. 트리거를 주입하면 ESC 검사는 쓰이지 않습니다.
    /// </remarks>
    public class UIBackHelper : MonoBehaviour
    {
        #region Fields

#if ENABLE_LEGACY_INPUT_MANAGER
        //== 트리거를 주입하지 않았을 때 ESC 를 쓸지 여부입니다.
        [SerializeField] private bool _useLegacyEscape = true;
#endif

        //== 주입형 Back 트리거. true 를 반환하면 Back 을 실행합니다.
        private Func<bool> _backTrigger;

        //== 대상 스택. null 이면 Default 를 씁니다.
        private UIStack _stack;

        #endregion

        #region Events

        /// <summary>최상단 UI 가 닫혔을 때 발생합니다. 인자는 닫힌 UI 입니다.</summary>
        public event Action<IUI> Closed;

        /// <summary>최상단 UI 가 닫힘을 거부해 막혔을 때 발생합니다.</summary>
        public event Action Blocked;

        /// <summary>닫을 UI 가 없을 때 발생합니다. 앱 종료 확인을 띄우는 자리로 씁니다.</summary>
        public event Action Empty;

        /// <summary>전역 차단 조건으로 Back 입력 자체가 무시되었을 때 발생합니다.</summary>
        public event Action Suppressed;

        #endregion

        #region Properties

        /// <summary>Back 을 적용할 스택. 지정하지 않으면 <see cref="UIStack.Default"/> 입니다.</summary>
        protected virtual UIStack Stack
        {
            get { return _stack ?? UIStack.Default; }
        }

        #endregion

        #region Public API - Configuration

        /// <summary>Back 을 적용할 스택을 지정합니다. null 이면 Default 를 씁니다.</summary>
        public void SetStack(UIStack stack)
        {
            _stack = stack;
        }

        /// <summary>
        /// Back 트리거 조건을 주입합니다. 매 프레임 호출되어 true 면 Back 을 실행합니다.
        /// 주입하면 ESC 검사는 쓰이지 않으며, null 을 넘기면 주입이 해제됩니다.
        /// </summary>
        public void SetBackTrigger(Func<bool> trigger)
        {
            _backTrigger = trigger;
        }

        #endregion

        #region Unity Messages

        protected virtual void Update()
        {
            if (!IsBackTriggered())
            {
                return;
            }

            BackResult result = Stack.Back(out IUI closed);
            switch (result)
            {
                case BackResult.Closed:
                    {
                        Raise(Closed, closed);
                        break;
                    }
                case BackResult.Blocked:
                    {
                        Raise(Blocked);
                        break;
                    }
                case BackResult.Empty:
                    {
                        Raise(Empty);
                        break;
                    }
                case BackResult.Suppressed:
                    {
                        Raise(Suppressed);
                        break;
                    }
            }
        }

        #endregion

        #region Private Helpers

        private bool IsBackTriggered()
        {
            if (_backTrigger != null)
            {
                return _backTrigger.Invoke();
            }

            //== Active Input Handling 이 Input System 전용이면 UnityEngine.Input 타입 자체가 사라집니다.
            //== 런타임 판정으로는 피할 수 없어 조건부 컴파일이 유일한 방법입니다.
#if ENABLE_LEGACY_INPUT_MANAGER
            if (_useLegacyEscape)
            {
                return Input.GetKeyDown(KeyCode.Escape);
            }
#endif

            return false;
        }

        private static void Raise(Action handler)
        {
            if (handler == null)
            {
                return;
            }

            try
            {
                handler.Invoke();
            }
            catch (Exception e)
            {
                Log.Error($"[UIBackHelper] 콜백에서 예외 발생: {e}", LogColor.Red);
            }
        }

        private static void Raise(Action<IUI> handler, IUI ui)
        {
            if (handler == null)
            {
                return;
            }

            try
            {
                handler.Invoke(ui);
            }
            catch (Exception e)
            {
                Log.Error($"[UIBackHelper] 콜백에서 예외 발생: {e}", LogColor.Red);
            }
        }

        #endregion
    }
}
