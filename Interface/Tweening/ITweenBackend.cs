namespace Cocoa.Lib.Tweening
{
    /// <summary>
    /// <see cref="TweenHandle"/> 가 개별 트윈을 제어할 때 호출하는 백엔드 표면입니다.
    /// 백엔드가 발급한 ID 로만 대상을 지정하므로 백엔드 고유 타입이 드러나지 않습니다.
    /// </summary>
    /// <remarks>
    /// 구현체는 모르는 ID 나 이미 죽은 트윈의 ID 가 들어와도 예외를 던지지 않고 무시해야 합니다.
    /// 핸들은 값 복사로 돌아다니다 트윈이 끝난 뒤에 호출되는 일이 흔하기 때문입니다.
    /// </remarks>
    public interface ITweenBackend
    {
        /// <summary>해당 ID 의 트윈이 아직 살아 있는지 여부.</summary>
        bool IsAlive(long id);

        /// <summary>해당 트윈을 일시정지합니다.</summary>
        void Pause(long id);

        /// <summary>해당 트윈을 재개합니다. 전역 일시정지 중이면 전역 재개까지 멈춘 상태를 유지합니다.</summary>
        void Resume(long id);

        /// <summary>해당 트윈을 시작 지점으로 되감고 멈춥니다.</summary>
        void Rewind(long id);

        /// <summary>해당 트윈을 시작 지점으로 되감고 다시 재생합니다.</summary>
        void Restart(long id);

        /// <summary>해당 트윈을 종료합니다. complete 가 true 면 끝값으로 즉시 완료시킨 뒤 종료합니다.</summary>
        void Kill(long id, bool complete);
    }
}
