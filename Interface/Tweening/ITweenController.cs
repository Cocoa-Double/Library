namespace Cocoa.Lib.Tweening
{
    /// <summary>
    /// 트윈 백엔드(DOTween, PrimeTween 등)에 의존하지 않는 전역 제어 표면입니다.
    /// 백엔드 고유 타입을 참조하지 않으므로 코어 어셈블리에 둡니다.
    /// </summary>
    /// <remarks>
    /// 트윈 생성은 백엔드마다 타입이 달라 넣지 않았습니다. 전역 일시정지와 종료만 다룹니다.
    /// </remarks>
    public interface ITweenController
    {
        /// <summary>전역 일시정지 상태인지 여부.</summary>
        bool IsPausedAll { get; }

        /// <summary>모든 트윈을 일시정지하고 전역 일시정지 상태로 들어갑니다.</summary>
        void PauseAll();

        /// <summary>모든 트윈을 재개하고 전역 일시정지 상태를 풉니다.</summary>
        void ResumeAll();

        /// <summary>모든 트윈을 종료합니다. complete 가 true 면 끝값으로 즉시 완료시킨 뒤 종료합니다.</summary>
        void KillAll(bool complete = false);
    }
}
