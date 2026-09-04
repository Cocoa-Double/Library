using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Cocoa.Lib.Collection
{
    /// <summary>
    /// Unity Inspector에서 직렬화 가능한 2차원 배열.
    /// 행(row, y) × 열(column, x) 구조이며, [y, x] 또는 [y][x] 접근을 모두 지원합니다.
    /// 일반 T[,]와 달리 Unity가 직렬화할 수 있어 Inspector에서 값을 확인/수정할 수 있습니다.
    /// </summary>
    [Serializable]
    public class UniArray2D<T> : IEnumerable<T>
    {
        #region Nested Types

        /// <summary>한 행을 나타냅니다. 외부에서 직접 생성할 수 없습니다.</summary>
        [Serializable]
        public sealed class Row
        {
            #region Fields

            [SerializeField] private T[] _slots;

            #endregion

            #region Properties

            public int Length
            {
                get { return _slots.Length; }
            }

            internal T[] RawSlots
            {
                get { return _slots; }
            }

            #endregion

            #region Indexers

            public T this[int x]
            {
                get
                {
                    if (x < 0 || x >= _slots.Length)
                    {
                        throw new IndexOutOfRangeException(
                            $"Column index {x} is out of range [0, {_slots.Length}).");
                    }
                    return _slots[x];
                }
                set
                {
                    if (x < 0 || x >= _slots.Length)
                    {
                        throw new IndexOutOfRangeException(
                            $"Column index {x} is out of range [0, {_slots.Length}).");
                    }
                    _slots[x] = value;
                }
            }

            #endregion

            #region Constructors

            internal Row(int columns)
            {
                _slots = new T[columns];
            }

            internal Row(int columns, T initial)
            {
                _slots = new T[columns];
                for (int i = 0; i < columns; i++)
                {
                    _slots[i] = initial;
                }
            }

            #endregion
        }

        #endregion

        #region Fields

        [SerializeField] private Row[] _rows;
        [SerializeField] private int _columns;

        #endregion

        #region Properties

        /// <summary>행(row) 개수 — y축 길이.</summary>
        public int Rows
        {
            get { return _rows.Length; }
        }

        /// <summary>열(column) 개수 — x축 길이.</summary>
        public int Columns
        {
            get { return _columns; }
        }

        /// <summary>전체 셀 개수 (Rows × Columns).</summary>
        public int Count
        {
            get { return _rows.Length * _columns; }
        }

        #endregion

        #region Constructors

        public UniArray2D(int rows, int columns)
        {
            if (rows < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(rows), "Rows must be non-negative.");
            }
            if (columns < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(columns), "Columns must be non-negative.");
            }
            _columns = columns;
            _rows = new Row[rows];
            for (int y = 0; y < rows; y++)
            {
                _rows[y] = new Row(columns);
            }
        }

        public UniArray2D(int rows, int columns, T initial)
        {
            if (rows < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(rows), "Rows must be non-negative.");
            }
            if (columns < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(columns), "Columns must be non-negative.");
            }
            _columns = columns;
            _rows = new Row[rows];
            for (int y = 0; y < rows; y++)
            {
                _rows[y] = new Row(columns, initial);
            }
        }

        /// <summary>기존 T[,] 배열로부터 생성합니다.</summary>
        public UniArray2D(T[,] source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            int rows = source.GetLength(0);
            int columns = source.GetLength(1);
            _columns = columns;
            _rows = new Row[rows];
            for (int y = 0; y < rows; y++)
            {
                _rows[y] = new Row(columns);
                for (int x = 0; x < columns; x++)
                {
                    _rows[y][x] = source[y, x];
                }
            }
        }

        #endregion

        #region Indexers

        /// <summary>[y, x] 형태로 셀에 접근합니다.</summary>
        public T this[int y, int x]
        {
            get
            {
                ValidateRow(y);
                return _rows[y][x];
            }
            set
            {
                ValidateRow(y);
                _rows[y][x] = value;
            }
        }

        /// <summary>행 단위로 접근합니다. arr[y][x] 형태로 사용할 수 있습니다.</summary>
        public Row this[int y]
        {
            get
            {
                ValidateRow(y);
                return _rows[y];
            }
        }

        #endregion

        #region Bounds & Safe Access

        /// <summary>지정한 좌표가 배열 범위 안에 있는지 확인합니다.</summary>
        public bool IsInBounds(int y, int x)
        {
            return y >= 0 && y < _rows.Length && x >= 0 && x < _columns;
        }

        /// <summary>경계 안의 좌표일 때만 값을 가져옵니다. 실패 시 default(T) 반환.</summary>
        public bool TryGet(int y, int x, out T value)
        {
            if (IsInBounds(y, x))
            {
                value = _rows[y][x];
                return true;
            }
            value = default;
            return false;
        }

        /// <summary>경계 안의 좌표일 때만 값을 설정합니다.</summary>
        public bool TrySet(int y, int x, T value)
        {
            if (IsInBounds(y, x))
            {
                _rows[y][x] = value;
                return true;
            }
            return false;
        }

        #endregion

        #region Bulk Operations

        /// <summary>모든 셀을 동일 값으로 채웁니다.</summary>
        public void Fill(T value)
        {
            for (int y = 0; y < _rows.Length; y++)
            {
                var row = _rows[y].RawSlots;
                for (int x = 0; x < row.Length; x++)
                {
                    row[x] = value;
                }
            }
        }

        /// <summary>좌표 기반 팩토리로 모든 셀을 초기화합니다. factory(y, x) → T.</summary>
        public void Populate(Func<int, int, T> factory)
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }
            for (int y = 0; y < _rows.Length; y++)
            {
                var row = _rows[y].RawSlots;
                for (int x = 0; x < row.Length; x++)
                {
                    row[x] = factory(y, x);
                }
            }
        }

        /// <summary>모든 셀에 대해 action(y, x, value)를 실행합니다.</summary>
        public void ForEach(Action<int, int, T> action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }
            for (int y = 0; y < _rows.Length; y++)
            {
                var row = _rows[y].RawSlots;
                for (int x = 0; x < row.Length; x++)
                {
                    action(y, x, row[x]);
                }
            }
        }

        /// <summary>각 셀에 함수를 적용하여 제자리에서 값을 변경합니다. mutator(y, x, oldValue) → newValue.</summary>
        public void Apply(Func<int, int, T, T> mutator)
        {
            if (mutator == null)
            {
                throw new ArgumentNullException(nameof(mutator));
            }
            for (int y = 0; y < _rows.Length; y++)
            {
                var row = _rows[y].RawSlots;
                for (int x = 0; x < row.Length; x++)
                {
                    row[x] = mutator(y, x, row[x]);
                }
            }
        }

        /// <summary>각 셀에 함수를 적용한 결과로 새 UniArray2D를 만들어 반환합니다. 원본은 변경되지 않습니다.</summary>
        public UniArray2D<TResult> Select<TResult>(Func<int, int, T, TResult> selector)
        {
            if (selector == null)
            {
                throw new ArgumentNullException(nameof(selector));
            }
            var result = new UniArray2D<TResult>(_rows.Length, _columns);
            for (int y = 0; y < _rows.Length; y++)
            {
                var sourceRow = _rows[y].RawSlots;
                for (int x = 0; x < sourceRow.Length; x++)
                {
                    result[y, x] = selector(y, x, sourceRow[x]);
                }
            }
            return result;
        }

        #endregion

        #region Conversion & Copy

        /// <summary>표준 T[,] 배열로 변환합니다.</summary>
        public T[,] ToArray()
        {
            var result = new T[_rows.Length, _columns];
            for (int y = 0; y < _rows.Length; y++)
            {
                var row = _rows[y].RawSlots;
                for (int x = 0; x < row.Length; x++)
                {
                    result[y, x] = row[x];
                }
            }
            return result;
        }

        /// <summary>특정 행을 새 배열로 복사하여 반환합니다.</summary>
        public T[] CopyRow(int y)
        {
            ValidateRow(y);
            var source = _rows[y].RawSlots;
            var copy = new T[source.Length];
            Array.Copy(source, copy, source.Length);
            return copy;
        }

        /// <summary>특정 열을 새 배열로 복사하여 반환합니다.</summary>
        public T[] CopyColumn(int x)
        {
            if (x < 0 || x >= _columns)
            {
                throw new IndexOutOfRangeException(
                    $"Column index {x} is out of range [0, {_columns}).");
            }
            var copy = new T[_rows.Length];
            for (int y = 0; y < _rows.Length; y++)
            {
                copy[y] = _rows[y][x];
            }
            return copy;
        }

        #endregion

        #region Enumeration

        /// <summary>특정 행의 요소들을 순차적으로 열거합니다 (할당 없음).</summary>
        public IEnumerable<T> EnumerateRow(int y)
        {
            ValidateRow(y);
            var row = _rows[y].RawSlots;
            for (int x = 0; x < row.Length; x++)
            {
                yield return row[x];
            }
        }

        /// <summary>특정 열의 요소들을 순차적으로 열거합니다 (할당 없음).</summary>
        public IEnumerable<T> EnumerateColumn(int x)
        {
            if (x < 0 || x >= _columns)
            {
                throw new IndexOutOfRangeException(
                    $"Column index {x} is out of range [0, {_columns}).");
            }
            for (int y = 0; y < _rows.Length; y++)
            {
                yield return _rows[y][x];
            }
        }

        /// <summary>모든 셀을 행 우선(row-major) 순서로 열거합니다.</summary>
        public IEnumerator<T> GetEnumerator()
        {
            for (int y = 0; y < _rows.Length; y++)
            {
                var row = _rows[y].RawSlots;
                for (int x = 0; x < row.Length; x++)
                {
                    yield return row[x];
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        #endregion

        #region Private Helpers

        private void ValidateRow(int y)
        {
            if (y < 0 || y >= _rows.Length)
            {
                throw new IndexOutOfRangeException(
                    $"Row index {y} is out of range [0, {_rows.Length}).");
            }
        }

        #endregion
    }
}