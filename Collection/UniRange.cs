using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cocoa.Lib.Collection
{
    [System.Serializable]
    public class UniRange<T>
    {
        public T start;
        public T end;

        public UniRange(T start, T end)
        {
            this.start = start;
            this.end = end;
        }

        public UniRange(UniRange<T> range)
        {
            this.start = range.start;
            this.end = range.end;
        }

        public void Deconstruct(out T start, out T end)
        {
            start = this.start;
            end = this.end;
        }
    }
}