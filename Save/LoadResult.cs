namespace Cocoa.Lib.Save
{
    /// <summary>
    /// 로드 작업의 결과입니다. 복원한 값과 상태를 함께 담습니다.
    /// </summary>
    /// <remarks>
    /// 실패해도 값은 default 로 채워지므로, NotFound 와 Corrupted 를 구분해야 하는 곳에서는 Status 를 먼저 확인합니다.
    /// 세이브가 손상됐는데 새 데이터로 덮어써 버리는 사고는 대부분 이 둘을 같은 실패로 다룰 때 생깁니다.
    /// </remarks>
    public readonly struct LoadResult<T>
    {
        #region Fields

        private readonly SaveStatus _status;
        private readonly T _value;

        #endregion

        #region Properties

        /// <summary>결과 상태.</summary>
        public SaveStatus Status
        {
            get { return _status; }
        }

        /// <summary>복원한 값. 실패하면 default.</summary>
        public T Value
        {
            get { return _value; }
        }

        /// <summary>성공 여부.</summary>
        public bool IsSuccess
        {
            get { return _status == SaveStatus.Success; }
        }

        #endregion

        #region Constructors

        public LoadResult(SaveStatus status, T value)
        {
            _status = status;
            _value = value;
        }

        #endregion

        #region Public API

        /// <summary>성공 결과를 만듭니다.</summary>
        public static LoadResult<T> Success(T value)
        {
            return new LoadResult<T>(SaveStatus.Success, value);
        }

        /// <summary>값 없이 상태만 담은 실패 결과를 만듭니다.</summary>
        public static LoadResult<T> Fail(SaveStatus status)
        {
            return new LoadResult<T>(status, default);
        }

        /// <summary>성공하면 값을, 실패하면 fallback 을 반환합니다.</summary>
        public T GetValueOrDefault(T fallback = default)
        {
            return IsSuccess ? _value : fallback;
        }

        public override string ToString()
        {
            return $"LoadResult<{typeof(T).Name}>({_status})";
        }

        #endregion
    }
}
