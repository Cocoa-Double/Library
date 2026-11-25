namespace Cocoa.Lib.Save
{
    /// <summary>
    /// 저장 작업의 결과입니다. 예외를 던지는 대신 상태로 알립니다.
    /// </summary>
    public readonly struct SaveResult
    {
        #region Fields

        private readonly SaveStatus _status;

        #endregion

        #region Properties

        /// <summary>결과 상태.</summary>
        public SaveStatus Status
        {
            get { return _status; }
        }

        /// <summary>성공 여부.</summary>
        public bool IsSuccess
        {
            get { return _status == SaveStatus.Success; }
        }

        /// <summary>성공 결과.</summary>
        public static SaveResult Success
        {
            get { return new SaveResult(SaveStatus.Success); }
        }

        /// <summary>실패 결과.</summary>
        public static SaveResult Failed
        {
            get { return new SaveResult(SaveStatus.Failed); }
        }

        #endregion

        #region Constructors

        public SaveResult(SaveStatus status)
        {
            _status = status;
        }

        #endregion

        #region Public API

        public override string ToString()
        {
            return _status.ToString();
        }

        #endregion
    }
}
