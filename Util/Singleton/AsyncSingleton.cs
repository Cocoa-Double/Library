using System;
using UnityEngine;

#if UNITASK_SUPPORTED
using Task = Cysharp.Threading.Tasks.UniTask;
#else
using Task = System.Threading.Tasks.Task;
#endif

namespace Cocoa.Lib.Util
{
    /// <summary>
    /// 비동기 초기화가 필요한 MonoBehaviour 싱글톤입니다. 파생 클래스는 Start 대신 InitializeAsync 를 override 합니다.
    /// </summary>
    /// <remarks>
    /// Start 는 Awake 보다 늦게 실행되므로 Instance 를 얻은 직후에는 아직 초기화 전일 수 있습니다. IsInitialized 를 확인하세요.
    /// 반환 타입은 using 별칭이라 UNITASK_SUPPORTED 가 정의되면 UniTask, 아니면 Task 로 해석됩니다.
    /// </remarks>
    public abstract class AsyncSingleton<T> : Singleton<T> where T : AsyncSingleton<T>
    {
        #region Unity Messages

        //== 파생에서 Start 를 다시 정의하면 이 메서드가 가려집니다. 초기화는 InitializeAsync 에 작성하세요.
        private async void Start()
        {
            try
            {
                await InitializeAsync();
            }
            catch (Exception e)
            {
                //== async void 는 예외를 호출부로 전달하지 못합니다.
                Log.Error($"[AsyncSingleton] {typeof(T).Name} 초기화 실패: {e}", LogColor.Red);
                return;
            }

            //== 실패하면 완료 표시를 남기지 않도록 catch 밖에 둡니다.
            _isInitialized = true;
            OnInitialized();
        }

        #endregion

        #region Overridable Hooks

        /// <summary>파생 클래스가 override 하는 비동기 초기화입니다. base 호출은 필요하지 않습니다.</summary>
        protected virtual Task InitializeAsync()
        {
            return Task.CompletedTask;
        }

        /// <summary>InitializeAsync 가 성공한 뒤 호출됩니다.</summary>
        protected virtual void OnInitialized() { }

        #endregion
    }
}
