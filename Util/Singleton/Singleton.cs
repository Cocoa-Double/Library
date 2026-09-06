using UnityEngine;

namespace Cocoa.Lib.Util
{
    /// <summary>
    /// MonoBehaviour 싱글톤 베이스입니다. 씬에 배치된 인스턴스가 있으면 그것을 쓰고, 없으면 GameObject 를 만들어 붙입니다.
    /// </summary>
    /// <remarks>
    /// 인스턴스 등록을 베이스가 관리하므로 Awake 와 OnDestroy 는 virtual 이 아닙니다.
    /// 파생에서 다시 정의하면 베이스가 가려지니 초기화는 OnAwake, 정리는 OnBeforeDestroy 에 작성하세요.
    /// </remarks>
    public abstract class Singleton<T> : MonoBehaviour where T : Singleton<T>
    {
        #region Static

        private static T _instance;

        /// <summary>싱글톤 인스턴스. 앱 종료 중에는 null 을 반환합니다.</summary>
        public static T Instance
        {
            get
            {
                //== 종료 중에 만들면 정리되지 않은 GameObject 가 남습니다.
                if (AppLifecycle.IsQuitting)
                {
                    return null;
                }

                if (_instance != null)
                {
                    return _instance;
                }

                //== 비활성 오브젝트는 Awake 가 돌지 않아 대상에서 빠집니다.
                T[] found = FindObjectsByType<T>(FindObjectsSortMode.None);
                if (found.Length > 0)
                {
                    if (found.Length > 1)
                    {
                        Log.Error($"[Singleton] {typeof(T).Name} 인스턴스가 {found.Length} 개입니다. 첫 번째만 사용합니다.", LogColor.Red);
                    }

                    _instance = found[0];
                    return _instance;
                }

                //== AddComponent 가 Awake 를 동기로 부르므로 등록은 그 안에서 끝납니다.
                GameObject host = new GameObject($"[Cocoa.Lib] {typeof(T).Name}");
                T created = host.AddComponent<T>();

                //== 파생이 Awake 를 가린 경우의 보정. 없으면 접근할 때마다 GameObject 가 새로 생깁니다.
                if (_instance == null)
                {
                    _instance = created;
                    DontDestroyOnLoad(host);
                }

                return _instance;
            }
        }

        /// <summary>Instance 에 접근하지 않고 존재 여부만 확인합니다. 종료 중에는 false 입니다.</summary>
        public static bool HasInstance
        {
            get { return _instance != null && !AppLifecycle.IsQuitting; }
        }

        #endregion

        #region Fields

        //== 파생 클래스가 초기화 완료를 표시하는 용도입니다.
        protected bool _isInitialized;

        #endregion

        #region Properties

        /// <summary>파생 클래스가 표시한 초기화 완료 상태.</summary>
        public bool IsInitialized
        {
            get { return _isInitialized; }
        }

        #endregion

        #region Unity Messages

        protected void Awake()
        {
            //== 씬 배치와 자동 생성이 겹치면 나중에 온 쪽을 버립니다.
            if (_instance != null && _instance != this)
            {
                Log.Warning($"[Singleton] {typeof(T).Name} 중복 인스턴스를 파괴합니다.", LogColor.Yellow);
                Destroy(gameObject);
                return;
            }

            _instance = (T)this;

            //== DontDestroyOnLoad 는 루트 오브젝트에만 적용됩니다.
            if (transform.parent == null)
            {
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Log.Warning($"[Singleton] {typeof(T).Name} 이 자식 오브젝트라 씬 전환 시 유지되지 않습니다. 루트로 옮기세요.", LogColor.Yellow);
            }

            OnAwake();
        }

        protected void OnDestroy()
        {
            //== 중복으로 파괴되는 쪽은 OnAwake 를 거치지 않았으므로 정리 훅도 부르지 않습니다.
            if (_instance != this)
            {
                return;
            }

            OnBeforeDestroy(AppLifecycle.IsQuitting);
            _instance = null;
        }

        #endregion

        #region Overridable Hooks

        /// <summary>Awake 대신 쓰는 초기화 지점입니다. 이 시점에 Instance 가 확정되어 있습니다.</summary>
        protected virtual void OnAwake() { }

        /// <summary>
        /// 파괴 직전 정리 지점입니다. 씬 전환과 앱 종료를 구분해야 하면 isQuitting 으로 갈라 씁니다.
        /// 모바일에서는 이 훅이 호출되지 않을 수 있으니 저장은 <see cref="AppLifecycle.PauseChanged"/> 에서 처리하세요.
        /// </summary>
        protected virtual void OnBeforeDestroy(bool isQuitting) { }

        #endregion
    }
}
