namespace Cocoa.Lib.UI
{
    /// <summary>
    /// UI 레이어입니다. 열거값이 클수록 Back 우선순위가 높아 먼저 닫힙니다.
    /// 여러 레이어에 UI 가 열려 있으면 우선순위가 가장 높은 레이어의 최상단부터 닫습니다.
    /// </summary>
    /// <remarks>
    /// <see cref="UIStack"/> 이 열거값을 배열 인덱스로 쓰므로 0 부터 시작하는 연속된 값이어야 합니다.
    /// 값을 추가할 때 중간을 비우거나 음수를 쓰지 마세요.
    /// </remarks>
    public enum UILayer
    {
        HUD = 0,    //== 상시 표시 UI. 보통 Back 대상이 아닙니다. 최저 우선순위
        Popup,      //== 일반 패널과 팝업
        Modal,      //== 입력을 막는 대화상자
        Tooltip     //== 툴팁 같은 일시적 정보. 가장 먼저 닫힙니다. 최고 우선순위
    }
}
