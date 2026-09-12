namespace Cocoa.Lib.UI
{
    /// <summary>
    /// <see cref="UIStack.Back()"/> 의 결과입니다.
    /// </summary>
    public enum BackResult
    {
        Closed = 0,     //== 최상단 UI 를 정상적으로 닫았습니다.
        Blocked,        //== 닫을 UI 는 있으나 최상단이 닫힘을 거부했습니다.
        Empty,          //== 열려 있는 UI 가 없어 닫을 것이 없습니다.
        Suppressed      //== 전역 차단 조건이 걸려 Back 입력 자체가 무시되었습니다.
    }
}
