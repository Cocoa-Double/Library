namespace Cocoa.Lib.Save
{
    /// <summary>
    /// 세이브/로드 결과 상태.
    /// </summary>
    public enum SaveStatus
    {
        //== 성공.
        Success = 0,

        //== 해당 키의 데이터가 없습니다. 최초 실행처럼 정상 흐름인 경우가 대부분입니다.
        NotFound = 1,

        //== 읽기는 했지만 형식이 깨져 복원하지 못했습니다.
        Corrupted = 2,

        //== 저장된 버전을 현재 버전으로 올릴 수 없습니다. 마이그레이터가 빠졌거나, 더 새 버전의 앱이 쓴 세이브입니다.
        UnsupportedVersion = 3,

        //== 입출력 오류, 잘못된 키 등 그 밖의 실패입니다.
        Failed = 4
    }
}
