using UnityEngine;

namespace Cocoa.Lib.Util
{
    /// <summary>
    /// OnApplicationPause 와 OnApplicationFocus 메시지를 받아 <see cref="AppLifecycle"/> 로 넘기는 내부 컴포넌트입니다.
    /// AppLifecycle 이 첫 구독자가 생기는 시점에 직접 만들므로 씬에 배치하거나 직접 붙일 일은 없습니다.
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    internal sealed class AppLifecycleDriver : MonoBehaviour
    {
        #region Unity Messages

        private void OnApplicationPause(bool isPaused)
        {
            AppLifecycle.SetPaused(isPaused);
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            AppLifecycle.SetFocus(hasFocus);
        }

        #endregion
    }
}
