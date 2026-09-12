using System;
using System.Collections.Generic;
using UnityEngine;

using Cocoa.Lib.Util;

namespace Cocoa.Lib.UI
{
    /// <summary>
    /// 레이어별로 열린 UI 를 추적하고 Back 동작을 중재하는 스택입니다.
    /// MonoBehaviour 가 아닌 순수 C# 클래스이며 특정 UI 프레임워크에 의존하지 않습니다.
    /// </summary>
    /// <remarks>
    /// 차단은 두 종류입니다.
    /// 전역 차단은 <see cref="AddBlockCondition"/> 으로 등록한 조건이 하나라도 true 면 Back 입력 자체를 무시합니다. 서버 통신이나 씬 전환 중에 씁니다.
    /// 항목별 차단은 최상단 UI 가 <see cref="IUIBlockable"/> 이고 CanClose 가 false 일 때 그 자리에서 막습니다. 하위 레이어로 내려가지 않습니다.
    /// </remarks>
    public class UIStack
    {
        #region Static Fields

        //== Back 검사 순서. 열거값이 큰 레이어부터 닫습니다.
        private static readonly UILayer[] _priorityOrder = BuildPriorityOrder();

        //== 열거값을 배열 인덱스로 쓰기 위한 슬롯 수. 최대 열거값 + 1 입니다.
        private static readonly int _layerSlotCount = BuildLayerSlotCount();

        /// <summary>라이브러리가 제공하는 공용 스택입니다.</summary>
        public static UIStack Default { get; } = new UIStack();

        #endregion

        #region Fields

        //== 레이어 인덱스 -> 그 레이어의 스택. 리스트의 끝이 최상단입니다.
        //== 열거값이 0 부터 연속이라 Dictionary 대신 배열을 씁니다. enum 키 Dictionary 는 조회마다 박싱합니다.
        private readonly List<IUI>[] _stackByLayer;

        //== UI -> 현재 레이어. Close 에서 어느 스택에 있는지 바로 찾기 위한 역참조입니다.
        private readonly Dictionary<IUI, UILayer> _layerByUI = new Dictionary<IUI, UILayer>();

        //== 전역 차단 조건. 하나라도 true 면 Back 을 무시합니다.
        private readonly List<Func<bool>> _blockConditions = new List<Func<bool>>();

        #endregion

        #region Events

        /// <summary>UI 가 스택에 새로 등록되었을 때 발생합니다. 이미 등록된 UI 를 다시 Open 해도 발생하지 않습니다.</summary>
        public event Action<IUI> Opened;

        /// <summary>UI 가 스택에서 제거되었을 때 발생합니다.</summary>
        public event Action<IUI> Closed;

        #endregion

        #region Properties

        /// <summary>모든 레이어를 통틀어 열려 있는 UI 수.</summary>
        public int TotalCount
        {
            get { return _layerByUI.Count; }
        }

        /// <summary>열려 있는 UI 가 하나라도 있는지 여부.</summary>
        public bool HasAnyOpen
        {
            get { return _layerByUI.Count > 0; }
        }

        #endregion

        #region Initialization

        public UIStack()
        {
            _stackByLayer = new List<IUI>[_layerSlotCount];
            for (int i = 0; i < _stackByLayer.Length; i++)
            {
                _stackByLayer[i] = new List<IUI>();
            }
        }

        //== 도메인 리로드를 끈 프로젝트에서는 이전 실행의 스택과 구독이 그대로 남습니다.
        //== 그 안의 UI 는 이미 파괴된 GameObject 라 Back 이 죽은 참조의 Hide 를 부르게 됩니다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetDefault()
        {
            Default.Clear();
            Default.ClearBlockConditions();
            Default.Opened = null;
            Default.Closed = null;
        }

        #endregion

        #region Public API - Open / Close

        /// <summary>
        /// UI 를 지정 레이어의 최상단으로 등록합니다. 이미 등록되어 있으면 옮기기만 합니다.
        /// 보통 UI 의 Show 구현에서 호출합니다.
        /// </summary>
        public void Open(IUI ui, UILayer layer)
        {
            if (ui == null)
            {
                Log.Warning("[UIStack] null UI 는 등록할 수 없습니다.", LogColor.Yellow);
                return;
            }

            List<IUI> stack = GetStack(layer);
            if (stack == null)
            {
                Log.Error($"[UIStack] 정의되지 않은 레이어입니다. (layer: {(int)layer})", LogColor.Red);
                return;
            }

            //== 이미 등록되어 있으면 기존 자리에서 빼고 다시 올립니다.
            bool isNew = !_layerByUI.TryGetValue(ui, out UILayer previousLayer);
            if (!isNew)
            {
                GetStack(previousLayer).Remove(ui);
            }

            stack.Add(ui);
            _layerByUI[ui] = layer;

            //== 재배치는 새로 열린 것이 아닙니다. 여기서도 발생시키면 Opened 와 Closed 의 짝이 맞지 않아
            //== 열린 UI 수를 세거나 배경을 흐리게 하는 구독자가 어긋납니다.
            if (isNew)
            {
                Raise(Opened, ui, nameof(Opened));
            }
        }

        /// <summary>
        /// UI 를 스택에서 제거합니다. 등록되어 있지 않으면 아무 일도 일어나지 않습니다.
        /// 보통 UI 의 Hide 구현에서 호출합니다.
        /// </summary>
        public void Close(IUI ui)
        {
            if (ui == null)
            {
                return;
            }

            if (!_layerByUI.TryGetValue(ui, out UILayer layer))
            {
                return;
            }

            GetStack(layer).Remove(ui);
            _layerByUI.Remove(ui);

            Raise(Closed, ui, nameof(Closed));
        }

        #endregion

        #region Public API - Back

        /// <summary>우선순위가 가장 높은 레이어의 최상단 UI 를 닫습니다.</summary>
        public BackResult Back()
        {
            return Back(out _);
        }

        /// <summary>
        /// 우선순위가 가장 높은 레이어의 최상단 UI 를 닫고 닫힌 UI 를 돌려줍니다.
        /// </summary>
        /// <param name="closed">닫힌 UI. 결과가 <see cref="BackResult.Closed"/> 가 아니면 null 입니다.</param>
        /// <remarks>
        /// 판정 순서는 전역 차단, 비어 있지 않은 최우선 레이어 탐색, 최상단 항목별 차단, 닫기입니다.
        /// 빈 레이어는 건너뛰고 모든 레이어가 비면 <see cref="BackResult.Empty"/> 입니다.
        /// </remarks>
        public BackResult Back(out IUI closed)
        {
            closed = null;

            if (IsGloballyBlocked())
            {
                return BackResult.Suppressed;
            }

            for (int i = 0; i < _priorityOrder.Length; i++)
            {
                List<IUI> stack = GetStack(_priorityOrder[i]);
                if (stack.Count == 0)
                {
                    continue;
                }

                IUI top = stack[stack.Count - 1];

                //== 최상단이 닫힘을 거부하면 하위 레이어로 내려가지 않고 여기서 멈춥니다.
                if (top is IUIBlockable blockable && !blockable.CanClose)
                {
                    return BackResult.Blocked;
                }

                closed = top;
                top.Hide();

                //== Hide 구현이 Close 를 부르는 것이 계약이지만, 지키지 않으면 스택에 남아 다음 Back 도 같은 UI 를 집습니다.
                //== 그 상태로 두면 Back 이 영영 진행되지 않으므로 여기서 직접 정리합니다.
                if (_layerByUI.ContainsKey(top))
                {
                    Log.Warning($"[UIStack] {top.GetType().Name} 의 Hide 가 Close 를 호출하지 않아 직접 제거합니다.", LogColor.Yellow);
                    Close(top);
                }

                return BackResult.Closed;
            }

            return BackResult.Empty;
        }

        #endregion

        #region Public API - Block Conditions

        /// <summary>
        /// 전역 차단 조건을 추가합니다. 등록된 조건이 하나라도 true 면 Back 이 무시됩니다.
        /// </summary>
        /// <remarks>
        /// 같은 조건이 중복 등록되지 않도록 걸러내지만, 델리게이트 비교는 대상과 메서드가 모두 같을 때만 성립합니다.
        /// 호출할 때마다 새로 만드는 람다는 서로 다른 것으로 취급되므로 제거하려면 참조를 따로 들고 있어야 합니다.
        /// </remarks>
        public void AddBlockCondition(Func<bool> condition)
        {
            if (condition == null)
            {
                Log.Error("[UIStack] null 차단 조건은 등록할 수 없습니다.", LogColor.Red);
                return;
            }

            if (_blockConditions.Contains(condition))
            {
                return;
            }

            _blockConditions.Add(condition);
        }

        /// <summary>등록한 전역 차단 조건을 제거합니다.</summary>
        /// <returns>제거되었으면 true.</returns>
        public bool RemoveBlockCondition(Func<bool> condition)
        {
            return _blockConditions.Remove(condition);
        }

        /// <summary>등록된 전역 차단 조건을 모두 제거합니다.</summary>
        public void ClearBlockConditions()
        {
            _blockConditions.Clear();
        }

        #endregion

        #region Public API - Query / Clear

        /// <summary>지정 레이어의 최상단 UI. 비어 있으면 null 입니다.</summary>
        public IUI Peek(UILayer layer)
        {
            List<IUI> stack = GetStack(layer);
            if (stack == null || stack.Count == 0)
            {
                return null;
            }

            return stack[stack.Count - 1];
        }

        /// <summary>지정 레이어에 열린 UI 수.</summary>
        public int CountOf(UILayer layer)
        {
            List<IUI> stack = GetStack(layer);
            if (stack == null)
            {
                return 0;
            }

            return stack.Count;
        }

        /// <summary>모든 레이어의 추적 정보를 비웁니다. UI 를 실제로 숨기지는 않습니다.</summary>
        public void Clear()
        {
            for (int i = 0; i < _stackByLayer.Length; i++)
            {
                _stackByLayer[i].Clear();
            }

            _layerByUI.Clear();
        }

        /// <summary>지정 레이어의 추적 정보만 비웁니다.</summary>
        public void Clear(UILayer layer)
        {
            List<IUI> stack = GetStack(layer);
            if (stack == null)
            {
                return;
            }

            for (int i = 0; i < stack.Count; i++)
            {
                _layerByUI.Remove(stack[i]);
            }

            stack.Clear();
        }

        #endregion

        #region Private Helpers

        private static UILayer[] BuildPriorityOrder()
        {
            UILayer[] values = (UILayer[])Enum.GetValues(typeof(UILayer));
            Array.Sort(values, (a, b) => ((int)b).CompareTo((int)a));

            return values;
        }

        //== 다른 정적 필드를 읽지 않습니다. 읽으면 선언 순서에 따라 초기화되지 않은 값을 보게 됩니다.
        private static int BuildLayerSlotCount()
        {
            UILayer[] values = (UILayer[])Enum.GetValues(typeof(UILayer));

            int max = 0;
            for (int i = 0; i < values.Length; i++)
            {
                int value = (int)values[i];
                if (value > max)
                {
                    max = value;
                }
            }

            return max + 1;
        }

        //== 정의되지 않은 열거값이 캐스팅으로 들어올 수 있어 범위를 확인합니다.
        private List<IUI> GetStack(UILayer layer)
        {
            int index = (int)layer;
            if (index < 0 || index >= _stackByLayer.Length)
            {
                return null;
            }

            return _stackByLayer[index];
        }

        private bool IsGloballyBlocked()
        {
            for (int i = 0; i < _blockConditions.Count; i++)
            {
                bool blocked;
                try
                {
                    blocked = _blockConditions[i].Invoke();
                }
                catch (Exception e)
                {
                    //== 조건 하나가 던진 예외로 Back 이 통째로 막히지 않도록 막지 않음으로 처리합니다.
                    Log.Error($"[UIStack] 차단 조건 평가 중 예외 발생: {e}", LogColor.Red);
                    blocked = false;
                }

                if (blocked)
                {
                    return true;
                }
            }

            return false;
        }

        //== 구독자 하나의 예외가 나머지를 막지 않도록 개별로 호출합니다.
        private static void Raise(Action<IUI> handlers, IUI ui, string eventName)
        {
            if (handlers == null)
            {
                return;
            }

            Delegate[] list = handlers.GetInvocationList();
            for (int i = 0; i < list.Length; i++)
            {
                try
                {
                    ((Action<IUI>)list[i]).Invoke(ui);
                }
                catch (Exception e)
                {
                    Log.Error($"[UIStack] {eventName} 구독자에서 예외 발생: {e}", LogColor.Red);
                }
            }
        }

        #endregion
    }
}
