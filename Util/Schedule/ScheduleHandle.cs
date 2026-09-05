using System;

namespace Cocoa.Lib.Util
{
    /// <summary>
    /// <see cref="Scheduler"/> 가 등록한 스케줄을 가리키는 핸들입니다.
    /// long ID 를 그대로 노출하지 않고 감싸서 다른 시스템의 ID 와 섞이지 않게 합니다.
    /// </summary>
    public readonly struct ScheduleHandle : IEquatable<ScheduleHandle>
    {
        #region Static

        /// <summary>어떤 스케줄도 가리키지 않는 빈 핸들.</summary>
        public static ScheduleHandle None
        {
            get { return default; }
        }

        #endregion

        #region Fields

        private readonly Scheduler _owner;
        private readonly long _id;

        #endregion

        #region Properties

        /// <summary>스케줄러가 발급한 내부 ID. 빈 핸들은 0 입니다.</summary>
        public long Id
        {
            get { return _id; }
        }

        /// <summary>아직 살아있는 스케줄을 가리키고 있는지 여부.</summary>
        public bool IsValid
        {
            get { return _id != 0L && _owner != null && _owner.IsActive(_id); }
        }

        #endregion

        #region Initialization

        internal ScheduleHandle(Scheduler owner, long id)
        {
            _owner = owner;
            _id = id;
        }

        #endregion

        #region Public API - Control

        /// <summary>이 스케줄을 일시정지합니다. realtime 스케줄도 이 호출로는 멈춥니다.</summary>
        public void Pause()
        {
            if (_owner != null)
            {
                _owner.Pause(_id);
            }
        }

        /// <summary>일시정지된 스케줄을 재개합니다.</summary>
        public void Resume()
        {
            if (_owner != null)
            {
                _owner.Resume(_id);
            }
        }

        /// <summary>스케줄을 취소합니다. 이미 완료되었거나 취소된 경우 아무 일도 일어나지 않습니다.</summary>
        public void Unregister()
        {
            if (_owner != null)
            {
                _owner.Unregister(_id);
            }
        }

        #endregion

        #region Public API - Equality

        //== 구조체 기본 Equals 는 리플렉션과 박싱을 거치므로 딕셔너리 키로 쓰면 비용이 큽니다. 직접 구현해 둡니다.
        //== 스케줄러 비교에 ReferenceEquals 를 쓰는 이유는, UnityEngine.Object 의 == 는 파괴된 객체를 null 로 취급해
        //== 서로 다른 스케줄러를 가리키던 핸들이 둘 다 파괴된 뒤 같다고 판정되기 때문입니다.
        public bool Equals(ScheduleHandle other)
        {
            return _id == other._id && ReferenceEquals(_owner, other._owner);
        }

        public override bool Equals(object obj)
        {
            return obj is ScheduleHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            return _id.GetHashCode();
        }

        public static bool operator ==(ScheduleHandle left, ScheduleHandle right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(ScheduleHandle left, ScheduleHandle right)
        {
            return !left.Equals(right);
        }

        #endregion
    }
}
