using System.Collections;
using UnityEngine;

namespace Cocoa.Lib.MasterTable
{
    /// <summary>
    /// 모든 마스터 테이블의 공통 베이스입니다. 한 시트의 데이터는 흩어진 에셋이 아니라 이 에셋 하나에 담깁니다.
    /// </summary>
    /// <remarks>
    /// 키 타입과 데이터 타입이 빠진 비제네릭 베이스를 따로 두는 이유는, 에디터 도구가 타입 인자를 모르는 상태에서도
    /// 데이터를 넣고 캐시를 다시 만들 수 있어야 하기 때문입니다.
    /// 그 두 멤버만 조건부 컴파일로 감싼 것은 동작을 분기하려는 것이 아니라 데이터를 통째로 갈아 끼우는 API 를
    /// 빌드에 포함시키지 않으려는 것입니다. 런타임 판정으로 대체하면 게임 코드에서 호출할 수 있는 상태로 남습니다.
    /// </remarks>
    public abstract class MasterTableBase : ScriptableObject
    {
        #region Properties

        /// <summary>보유한 데이터 개수.</summary>
        public abstract int Count { get; }

        #endregion

#if UNITY_EDITOR
        #region Editor API

        /// <summary>생성기가 만든 데이터를 통째로 설정합니다. 타입 인자를 모르는 곳에서 호출하기 위한 통로입니다.</summary>
        public abstract void ApplyGeneratedDatas(IList datas);

        /// <summary>조회 캐시를 버리고 다시 만듭니다.</summary>
        public abstract void RebuildCache();

        #endregion
#endif
    }
}
