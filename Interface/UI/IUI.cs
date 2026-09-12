namespace Cocoa.Lib.UI
{
    /// <summary>
    /// 표시와 숨김이 가능한 UI 요소의 최소 계약입니다. 특정 UI 프레임워크에 묶이지 않습니다.
    /// </summary>
    /// <remarks>
    /// Hide 구현은 반드시 자신이 등록된 <see cref="UIStack"/> 의 Close 를 호출해야 합니다.
    /// UIStack 의 Back 은 최상단 UI 의 Hide 를 부르는 것으로 닫기를 위임하기 때문입니다.
    /// </remarks>
    public interface IUI
    {
        void Show();

        void Hide();
    }

    /// <summary>
    /// 버튼형 UI 입니다. 표시와 숨김에 더해 콜백 등록 단계를 가집니다.
    /// </summary>
    public interface IUIButton : IUI
    {
        void RegisterButtonCallbacks();
    }
}
