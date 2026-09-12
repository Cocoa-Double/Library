namespace Cocoa.Lib.UI
{
    /// <summary>
    /// Back 으로 닫힐 수 있는지 스스로 판단하는 UI 입니다.
    /// 이 인터페이스를 구현하지 않은 UI 는 항상 닫힘 가능으로 봅니다.
    /// </summary>
    public interface IUIBlockable
    {
        /// <summary>
        /// false 면 Back 으로 닫히지 않고 <see cref="BackResult.Blocked"/> 가 반환됩니다.
        /// 강제 동의 팝업이나 닫기 버튼 전용 모달에 씁니다.
        /// </summary>
        bool CanClose { get; }
    }
}
