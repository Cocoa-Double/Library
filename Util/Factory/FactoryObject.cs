using UnityEngine;
using Cocoa.Lib.Common;

namespace Cocoa.Lib.Util
{
    public class FactoryObject : MonoBehaviour, IInitialize
    {
        public string ID = string.Empty;
        public virtual void Initialize(object param) { }


        public bool IsReturned;
        public delegate void OnReturnHandler(FactoryObject obj);
        public OnReturnHandler OnReturn;

        public virtual void Return()
        {
            OnReturn?.Invoke(this);
        }

        //== Helper Function
#if UNITY_EDITOR
        protected virtual void OnValidate()
        {
            if (ID.CompareTo(string.Empty) == 0)
            {
                ID = GetType().ToString();
            }
        }
#endif
    }
}
