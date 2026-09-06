using System;
using UnityEngine;

namespace Cocoa.Lib.Util
{
    /// <summary>
    /// 앱 단위 생명주기 신호를 한곳에서 알려주는 정적 진입점입니다.
    /// MonoBehaviour 가 아닌 클래스도 종료와 백그라운드 전환을 구독할 수 있습니다.
    /// </summary>
    /// <remarks>
    /// 모바일에서는 백그라운드로 내려간 앱을 OS 가 그대로 종료할 수 있습니다.
    /// 그 경우 종료 신호가 아예 오지 않으므로, 놓치면 안 되는 저장은 PauseChanged 의 인자가 true 인 시점에 끝내야 합니다.
    /// </remarks>
    public static class AppLifecycle
    {
        #region Static Fields

        private static Action<bool> _pauseChanged;
        private static Action<bool> _focusChanged;
        private static AppLifecycleDriver _driver;

        #endregion

        #region Properties

        /// <summary>앱이 종료 절차에 들어갔는지 여부.</summary>
        public static bool IsQuitting { get; private set; }

        /// <summary>앱이 백그라운드에 있는지 여부.</summary>
        public static bool IsPaused { get; private set; }

        /// <summary>앱이 포커스를 가지고 있는지 여부.</summary>
        public static bool HasFocus { get; private set; }

        #endregion

        #region Events

        /// <summary>앱이 종료 절차에 들어갈 때 발생합니다.</summary>
        public static event Action Quitting;

        /// <summary>백그라운드로 내려가거나 다시 올라올 때 발생합니다. 인자는 백그라운드로 내려갔으면 true 입니다.</summary>
        /// <remarks>
        /// FocusChanged 와 함께 발생하는 경우가 많지만 항상 짝을 이루지는 않습니다. 발생 조합은 다음과 같습니다.
        /// iOS, Android 에서 홈 버튼을 누르거나 앱을 전환하면 둘 다 발생합니다.
        /// 데스크톱과 에디터에서는 Run In Background 가 꺼져 있으면(기본값) 포커스를 잃는 순간 실제로 멈추므로 둘 다 발생하고, 켜면 FocusChanged 만 발생합니다.
        /// Android 에서 온스크린 키보드를 띄우면 FocusChanged 만 발생하고, 반대로 키보드가 떠 있는 상태에서 홈 버튼을 누르면 PauseChanged 만 발생합니다.
        /// 마지막 두 경우 때문에 두 신호를 하나로 합칠 수 없습니다. 합치면 키보드를 띄울 때마다 저장이 돌고, 정작 그 상태에서 앱을 내릴 때는 저장이 누락됩니다.
        /// iOS 의 호출 순서는 내려갈 때 FocusChanged(false) 다음 PauseChanged(true), 올라올 때 PauseChanged(false) 다음 FocusChanged(true) 입니다.
        /// 플랫폼과 OS 버전에 따라 편차가 보고되므로 타겟 기기에서 한 번은 확인하는 편이 안전합니다.
        ///
        /// 위 내용은 2026-09 기준으로 아래 자료를 확인해 정리했습니다. OS 업데이트로 달라질 수 있는 영역이니 시점을 함께 봐주세요.
        /// https://docs.unity3d.com/ScriptReference/MonoBehaviour.OnApplicationPause.html
        /// https://docs.unity3d.com/ScriptReference/MonoBehaviour.OnApplicationFocus.html
        /// https://discussions.unity.com/t/can-somebody-explain-the-onapplicationpause-focus-scenarios/76324
        /// </remarks>
        public static event Action<bool> PauseChanged
        {
            add
            {
                EnsureDriver();
                _pauseChanged += value;
            }
            remove
            {
                _pauseChanged -= value;
            }
        }

        /// <summary>포커스를 얻거나 잃을 때 발생합니다. 인자는 포커스를 얻었으면 true 입니다.</summary>
        /// <remarks>
        /// 앱이 멈췄는지가 아니라 입력을 받을 수 있는 상태인지를 나타냅니다. 음소거 전환이나 입력 차단처럼 포커스에 반응하는 처리에 씁니다.
        /// PauseChanged 와의 발생 조합은 그쪽 설명을 참고하세요.
        /// </remarks>
        public static event Action<bool> FocusChanged
        {
            add
            {
                EnsureDriver();
                _focusChanged += value;
            }
            remove
            {
                _focusChanged -= value;
            }
        }

        #endregion

        #region Initialization

        //== 도메인 리로드를 끈 상태로 플레이를 다시 시작하면 이전 실행의 상태와 구독이 그대로 남습니다.
        //== SubsystemRegistration 은 그 경우에도 실행되므로 여기서 전부 되돌립니다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Initialize()
        {
            IsQuitting = false;
            IsPaused = false;
            HasFocus = true;

            Quitting = null;
            _pauseChanged = null;
            _focusChanged = null;
            _driver = null;

            Application.quitting -= OnApplicationQuitting;
            Application.quitting += OnApplicationQuitting;
        }

        #endregion

        #region Internal

        internal static void SetPaused(bool isPaused)
        {
            IsPaused = isPaused;
            Raise(_pauseChanged, isPaused, nameof(PauseChanged));
        }

        internal static void SetFocus(bool hasFocus)
        {
            HasFocus = hasFocus;
            Raise(_focusChanged, hasFocus, nameof(FocusChanged));
        }

        #endregion

        #region Private Helpers

        private static void OnApplicationQuitting()
        {
            IsQuitting = true;

            Action handlers = Quitting;
            if (handlers == null)
            {
                return;
            }

            Delegate[] list = handlers.GetInvocationList();
            for (int i = 0; i < list.Length; i++)
            {
                try
                {
                    ((Action)list[i]).Invoke();
                }
                catch (Exception e)
                {
                    Log.Error($"[AppLifecycle] {nameof(Quitting)} 구독자에서 예외 발생: {e}", LogColor.Red);
                }
            }
        }

        //== 구독자 하나의 예외가 나머지를 막지 않도록 개별로 호출합니다.
        //== GetInvocationList 는 배열을 만들지만 이 이벤트들은 세션당 몇 번 수준이라 문제되지 않습니다.
        private static void Raise(Action<bool> handlers, bool value, string eventName)
        {
            if (handlers == null)
            {
                return;
            }

            Delegate[] list = handlers.GetInvocationList();
            for (int i = 0; i < list.Length; i++)
            {
                try
                {
                    ((Action<bool>)list[i]).Invoke(value);
                }
                catch (Exception e)
                {
                    Log.Error($"[AppLifecycle] {eventName} 구독자에서 예외 발생: {e}", LogColor.Red);
                }
            }
        }

        private static void EnsureDriver()
        {
            if (_driver != null)
            {
                return;
            }

            //== OnApplicationPause 와 OnApplicationFocus 는 MonoBehaviour 메시지라 씬에 실체가 있어야 받습니다.
            //== 구독자가 생기는 시점에만 만들어, 쓰지 않는 프로젝트에는 오브젝트를 남기지 않습니다.
            GameObject host = new GameObject("[Cocoa.Lib] AppLifecycle");
            UnityEngine.Object.DontDestroyOnLoad(host);

            _driver = host.AddComponent<AppLifecycleDriver>();
        }

        #endregion
    }
}
