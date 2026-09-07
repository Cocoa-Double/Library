namespace Cocoa.Lib.MasterTable
{
    /// <summary>
    /// 마스터 테이블에서 키로 조회하기 위한 계약입니다.
    /// 생성되는 Table 클래스가 구현하며, Key 는 시트의 키 컬럼 값을 그대로 돌려줍니다.
    /// </summary>
    /// <remarks>
    /// 키 타입에는 제약을 두지 않습니다. 시트의 키 컬럼이 int, string, long, enum 중 무엇이든 될 수 있습니다.
    /// </remarks>
    public interface ITableData<TKey>
    {
        /// <summary>조회에 쓰는 키.</summary>
        TKey Key { get; }
    }
}
