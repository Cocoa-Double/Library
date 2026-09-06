using System.Collections.Generic;
using UnityEngine;

namespace Cocoa.Lib.Util
{
    /// <summary>
    /// 같은 대기 시간에 대한 <see cref="WaitForSeconds"/> 인스턴스를 캐싱해 재사용합니다.
    /// 코루틴에서 매번 새로 만들면 대기할 때마다 GC 부담이 쌓입니다.
    /// </summary>
    /// <remarks>
    /// 메인 스레드 전용입니다.
    /// WaitForSeconds 는 대기 시간만 들고 있고 경과 시각은 코루틴 스케줄러가 따로 관리하므로 재사용해도 안전합니다.
    /// 반면 WaitForSecondsRealtime 은 종료 시각을 스스로 들고 있어 재사용할 수 없으므로 캐싱 대상에서 제외합니다.
    /// </remarks>
    public static class WaitCache
    {
        #region Static Fields

        //== 밀리초 -> WaitForSeconds
        private static readonly Dictionary<int, WaitForSeconds> _waitByMilliseconds
            = new Dictionary<int, WaitForSeconds>();

        #endregion

        #region Public API

        /// <summary>
        /// 지정한 초에 대한 캐싱된 <see cref="WaitForSeconds"/> 를 반환합니다.
        /// 키를 밀리초로 반올림하므로 1ms 미만의 차이는 같은 인스턴스를 공유합니다.
        /// </summary>
        public static WaitForSeconds ForSeconds(float seconds)
        {
            //== float 을 그대로 키로 쓰면 계산으로 만들어진 값마다 항목이 생겨 캐시가 끝없이 자랍니다.
            //== 프레임 간격이 16ms 수준이라 1ms 미만의 차이는 대기 시간에 의미가 없습니다.
            int key = Mathf.RoundToInt(seconds * 1000f);
            if (!_waitByMilliseconds.TryGetValue(key, out WaitForSeconds wait))
            {
                //== 반올림한 값으로 만들어야 키와 실제 대기 시간이 어긋나지 않습니다.
                wait = new WaitForSeconds(key * 0.001f);
                _waitByMilliseconds.Add(key, wait);
            }

            return wait;
        }

        /// <summary>캐시를 비웁니다.</summary>
        public static void Clear()
        {
            _waitByMilliseconds.Clear();
        }

        #endregion
    }
}
