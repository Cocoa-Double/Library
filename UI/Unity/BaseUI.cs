using UnityEngine;

namespace Cocoa.Lib.UI
{
    /// <summary>
    /// 표시와 숨김에 맞춰 <see cref="UIStack"/> 에 자동으로 등록되고 해제되는 MonoBehaviour UI 베이스입니다.
    /// GameObject 활성 여부만 다루므로 uGUI 와 UI Toolkit 어느 쪽과도 함께 쓸 수 있습니다.
    /// </summary>
    /// <remarks>
    /// 파생 클래스가 Show 나 Hide 를 override 할 때는 반드시 base 를 호출해야 스택 등록과 해제가 유지됩니다.
    /// UIStack 의 Back 이 Hide 를 부르는 것으로 닫기를 위임하기 때문입니다.
    /// </remarks>
    public class BaseUI : MonoBehaviour, IUI, IUIBlockable
    {
        #region Fields

        [SerializeField] private UILayer _layer = UILayer.Popup;

        //== false 면 Back 으로 닫히지 않습니다.
        [SerializeField] private bool _canClose = true;

        #endregion

        #region Properties

        /// <summary>이 UI 가 속한 레이어.</summary>
        public virtual UILayer Layer
        {
            get { return _layer; }
        }

        /// <summary>Back 으로 닫힐 수 있는지 여부.</summary>
        public virtual bool CanClose
        {
            get { return _canClose; }
        }

        /// <summary>현재 숨김 상태인지 여부.</summary>
        public virtual bool IsHidden
        {
            get { return !gameObject.activeSelf; }
        }

        /// <summary>등록 대상 스택. 기본은 <see cref="UIStack.Default"/> 이며 필요하면 override 합니다.</summary>
        protected virtual UIStack Stack
        {
            get { return UIStack.Default; }
        }

        #endregion

        #region Public API - Show / Hide

        /// <summary>UI 를 표시하고 스택 최상단으로 등록합니다.</summary>
        public virtual void Show()
        {
            gameObject.SetActive(true);
            Stack.Open(this, Layer);
        }

        /// <summary>UI 를 숨기고 스택에서 제거합니다.</summary>
        public virtual void Hide()
        {
            gameObject.SetActive(false);
            Stack.Close(this);
        }

        #endregion

        #region Unity Messages

        protected virtual void OnDestroy()
        {
            //== Hide 없이 파괴되면 스택에 죽은 참조가 남습니다. 그 상태로 Back 이 오면 파괴된 오브젝트의 Hide 를 부르게 됩니다.
            Stack.Close(this);
        }

        #endregion
    }
}
