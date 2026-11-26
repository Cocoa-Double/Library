using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using UnityEngine;

using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Cocoa.Lib.Save
{
    /// <summary>
    /// Newtonsoft 가 Unity 직렬화 규칙을 따르게 하는 ContractResolver 입니다.
    /// JsonUtility 로 저장하던 타입을 Newtonsoft 로 바꿔도 같은 필드가 같은 이름으로 기록됩니다.
    /// </summary>
    /// <remarks>
    /// 직렬화 대상은 public 필드와 [SerializeField] 필드이며, 프로퍼티는 제외합니다.
    /// Newtonsoft 기본 규칙대로 프로퍼티까지 읽으면 Vector3.normalized 처럼 자기 타입을 돌려주는 프로퍼티에서 순환 참조 예외가 납니다.
    /// ISerializationCallbackReceiver 를 구현한 타입은 컬렉션이 아니라 객체로 다루고 직렬화 콜백을 불러,
    /// UniDictionary 같은 컨테이너가 JsonUtility 와 같은 방식으로 저장되고 복원됩니다.
    /// </remarks>
    public sealed class UnitySerializationContractResolver : DefaultContractResolver
    {
        #region Static Properties

        /// <summary>
        /// 공용 인스턴스. 계약은 resolver 인스턴스마다 캐시되므로 인스턴스를 나눠 만들면 같은 타입을 반복해서 분석합니다.
        /// </summary>
        public static UnitySerializationContractResolver Instance { get; } = new UnitySerializationContractResolver();

        #endregion

        #region Protected API

        protected override JsonContract CreateContract(Type objectType)
        {
            //== IList 나 IDictionary 를 함께 구현한 컨테이너가 컬렉션 계약으로 잡히면 백킹 필드 대신 내부 상태가 기록되고 콜백도 불리지 않습니다.
            if (typeof(ISerializationCallbackReceiver).IsAssignableFrom(objectType))
            {
                JsonObjectContract contract = CreateObjectContract(objectType);
                contract.OnSerializingCallbacks.Add(InvokeBeforeSerialize);
                contract.OnDeserializedCallbacks.Add(InvokeAfterDeserialize);
                return contract;
            }

            return base.CreateContract(objectType);
        }

        protected override List<MemberInfo> GetSerializableMembers(Type objectType)
        {
            List<MemberInfo> members = new List<MemberInfo>();
            HashSet<string> seen = new HashSet<string>();

            //== DeclaredOnly 로 타입마다 따로 모아야 베이스의 private [SerializeField] 필드까지 빠짐없이 잡힙니다.
            for (Type current = objectType; current != null && current != typeof(object); current = current.BaseType)
            {
                FieldInfo[] fields = current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                for (int i = 0; i < fields.Length; i++)
                {
                    FieldInfo field = fields[i];
                    if (field.IsDefined(typeof(NonSerializedAttribute), false))
                    {
                        continue;
                    }

                    if (!field.IsPublic && !field.IsDefined(typeof(SerializeField), false))
                    {
                        continue;
                    }

                    //== 파생 타입이 같은 이름을 다시 선언했으면 파생 쪽을 씁니다.
                    if (seen.Add(field.Name))
                    {
                        members.Add(field);
                    }
                }
            }

            return members;
        }

        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            JsonProperty property = base.CreateProperty(member, memberSerialization);

            //== private 필드는 기본적으로 읽기/쓰기 대상이 아니므로 직렬화 대상으로 고른 필드는 열어 줍니다.
            if (member is FieldInfo)
            {
                property.Readable = true;
                property.Writable = true;
            }

            return property;
        }

        #endregion

        #region Private Helpers

        private static void InvokeBeforeSerialize(object target, StreamingContext context)
        {
            ISerializationCallbackReceiver receiver = target as ISerializationCallbackReceiver;
            if (receiver != null)
            {
                receiver.OnBeforeSerialize();
            }
        }

        private static void InvokeAfterDeserialize(object target, StreamingContext context)
        {
            ISerializationCallbackReceiver receiver = target as ISerializationCallbackReceiver;
            if (receiver != null)
            {
                receiver.OnAfterDeserialize();
            }
        }

        #endregion
    }
}
