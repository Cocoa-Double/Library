using System;

namespace Cocoa.Lib.Util
{
    /// <summary>
    /// <see cref="CoroutineHandler"/> 가 실행 중인 코루틴을 가리키는 핸들입니다.
    /// long ID 를 그대로 노출하지 않고 감싸서 다른 시스템의 ID 와 섞이지 않게 합니다.
    /// </summary>
    public readonly struct CoroutineHandle : IEquatable<CoroutineHandle>
    {
        #region Static

        /// <summary>어떤 코루틴도 가리키지 않는 빈 핸들.</summary>
        public static CoroutineHandle None
        {
            get { return default; }
        }

        #endregion

        #region Fields

        private readonly CoroutineHandler _owner;
        private readonly long _id;

        #endregion

        #region Properties

        /// <summary>핸들러가 발급한 내부 ID. 빈 핸들은 0 입니다.</summary>
        public long Id
        {
            get { return _id; }
        }

        /// <summary>실행 중이거나 일시정지된 코루틴을 가리키고 있는지 여부.</summary>
        public bool IsValid
        {
            get { return _id != 0L && _owner != null && _owner.IsActive(_id); }
        }

        #endregion

        #region Initialization

        internal CoroutineHandle(CoroutineHandler owner, long id)
        {
            _owner = owner;
            _id = id;
        }

        #endregion

        #region Public API - Control

        /// <summary>이 코루틴을 일시정지합니다. 다음 yield 경계에서 적용됩니다.</summary>
        public void Pause()
        {
            if (_owner != null)
            {
                _owner.Pause(_id);
            }
        }

        /// <summary>일시정지된 코루틴을 재개합니다.</summary>
        public void Resume()
        {
            if (_owner != null)
            {
                _owner.Resume(_id);
            }
        }

        /// <summary>코루틴을 중지하고 추적에서 제거합니다. 이미 끝났거나 중지되었다면 아무 일도 일어나지 않습니다.</summary>
        public void Stop()
        {
            if (_owner != null)
            {
                _owner.Stop(_id);
            }
        }

        #endregion

        #region Public API - Equality

        //== 구조체 기본 Equals 는 리플렉션과 박싱을 거치므로 딕셔너리 키로 쓰면 비용이 큽니다. 직접 구현해 둡니다.
        //== 핸들러 비교에 ReferenceEquals 를 쓰는 이유는, UnityEngine.Object 의 == 는 파괴된 객체를 null 로 취급해
        //== 서로 다른 핸들러를 가리키던 핸들이 둘 다 파괴된 뒤 같다고 판정되기 때문입니다.
        public bool Equals(CoroutineHandle other)
        {
            return _id == other._id && ReferenceEquals(_owner, other._owner);
        }

        public override bool Equals(object obj)
        {
            return obj is CoroutineHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            return _id.GetHashCode();
        }

        public static bool operator ==(CoroutineHandle left, CoroutineHandle right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(CoroutineHandle left, CoroutineHandle right)
        {
            return !left.Equals(right);
        }

        #endregion
    }
}
