using System;

namespace Cocoa.Lib.Save
{
    /// <summary>
    /// 저장 데이터를 감싸는 봉투입니다. 스키마 버전과 직렬화된 본문을 나눠 담습니다.
    /// </summary>
    /// <remarks>
    /// 로드할 때 버전을 먼저 읽어 마이그레이션을 적용한 뒤 본문을 최종 타입으로 역직렬화합니다.
    /// 본문을 문자열로 한 번 더 감싸는 이유는 마이그레이터가 타입 없이 문자열만으로 이전 버전 데이터를 고칠 수 있게 하기 위해서입니다.
    /// 필드 이름은 이미 저장된 파일과의 호환을 위해 바꾸지 않습니다.
    /// </remarks>
    [Serializable]
    internal sealed class SaveEnvelope
    {
        public int version;
        public string payload;
    }
}
