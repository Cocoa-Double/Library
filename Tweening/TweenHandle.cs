using System;

namespace Cocoa.Lib.Tweening
{
    /// <summary>
    /// 백엔드가 등록한 트윈 하나를 가리키는 핸들입니다.
    /// 원시 long ID 를 감싸 Scheduler 나 CoroutineHandler 같은 다른 시스템의 ID 와 섞이지 않게 합니다.
    /// </summary>
    /// <remarks>
    /// 백엔드 고유 타입 대신 <see cref="ITweenBackend"/> 를 들고 있어 DOTween 과 PrimeTween 이 같은 핸들 타입을 씁니다.
    /// 트윈이 이미 끝난 뒤에 호출해도 안전합니다.
    /// </remarks>
    public readonly struct TweenHandle : IEquatable<TweenHandle>
    {
        #region Static

        /// <summary>유효하지 않은 빈 핸들.</summary>
        public static TweenHandle None
        {
            get { return default; }
        }

        #endregion

        #region Fields

        private readonly ITweenBackend _backend;

        //== 0 은 무효 ID 로 예약되어 있습니다. default(TweenHandle) 이 곧 None 이 되도록 하기 위해서입니다.
        private readonly long _id;

        #endregion

        #region Properties

        /// <summary>백엔드가 발급한 내부 ID. 빈 핸들은 0 입니다.</summary>
        public long Id
        {
            get { return _id; }
        }

        /// <summary>핸들이 현재 살아 있는 트윈을 가리키는지 여부.</summary>
        public bool IsValid
        {
            get { return _backend != null && _id != 0L && _backend.IsAlive(_id); }
        }

        #endregion

        #region Initialization

        /// <summary>
        /// 백엔드 구현이 등록 결과를 돌려줄 때 호출합니다. 앱 코드가 직접 만들 일은 없습니다.
        /// </summary>
        /// <remarks>백엔드가 별도 어셈블리에 있어 internal 로 둘 수 없습니다.</remarks>
        public TweenHandle(ITweenBackend backend, long id)
        {
            _backend = backend;
            _id = id;
        }

        #endregion

        #region Public API - Control

        /// <summary>트윈을 일시정지합니다.</summary>
        public void Pause()
        {
            if (_backend != null)
            {
                _backend.Pause(_id);
            }
        }

        /// <summary>일시정지된 트윈을 재개합니다.</summary>
        public void Resume()
        {
            if (_backend != null)
            {
                _backend.Resume(_id);
            }
        }

        /// <summary>트윈을 시작 지점으로 되감고 멈춥니다.</summary>
        public void Rewind()
        {
            if (_backend != null)
            {
                _backend.Rewind(_id);
            }
        }

        /// <summary>트윈을 시작 지점으로 되감고 다시 재생합니다.</summary>
        public void Restart()
        {
            if (_backend != null)
            {
                _backend.Restart(_id);
            }
        }

        /// <summary>트윈을 종료합니다. complete 가 true 면 끝값으로 즉시 완료시킨 뒤 종료합니다.</summary>
        public void Kill(bool complete = false)
        {
            if (_backend != null)
            {
                _backend.Kill(_id, complete);
            }
        }

        #endregion

        #region Public API - Equality

        //== 구조체 기본 Equals 는 리플렉션과 박싱을 거치므로 딕셔너리 키로 쓰면 비용이 큽니다. 직접 구현해 둡니다.
        //== 백엔드 비교에 ReferenceEquals 를 쓰는 이유는, 백엔드가 MonoBehaviour 인 경우 == 가 파괴된 객체를
        //== null 로 취급해 서로 다른 백엔드를 가리키던 핸들이 둘 다 파괴된 뒤 같다고 판정되기 때문입니다.
        public bool Equals(TweenHandle other)
        {
            return _id == other._id && ReferenceEquals(_backend, other._backend);
        }

        public override bool Equals(object obj)
        {
            return obj is TweenHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            return _id.GetHashCode();
        }

        public static bool operator ==(TweenHandle left, TweenHandle right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(TweenHandle left, TweenHandle right)
        {
            return !left.Equals(right);
        }

        #endregion
    }
}
