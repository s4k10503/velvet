#nullable enable
using System;

namespace Velvet
{
    // V8's Array.prototype.sort, the TimSort of its array-sort.tq, step for step. React Router ranks route
    // branches with a comparator that is not a strict weak ordering -- it orders siblings by index and calls
    // every other pair of equal score equal -- so which order comes out depends on the algorithm and not only
    // on the comparator, and a sort that merely agrees on consistent comparators would rank such a tree
    // differently.
    internal static class JsArraySort
    {
        private const int MinGallopWins = 7;

        internal static void Sort<T>(T[] array, Comparison<T> compare)
        {
            if (array.Length < 2)
            {
                return;
            }
            new State<T>(array, compare).Run();
        }

        private sealed class State<T>
        {
            private readonly T[] _work;
            private readonly Comparison<T> _compare;
            private readonly int[] _runBase = new int[85];
            private readonly int[] _runLength = new int[85];
            private int _runs;
            private int _minGallop = MinGallopWins;
            private T[] _temp = Array.Empty<T>();

            internal State(T[] work, Comparison<T> compare)
            {
                _work = work;
                _compare = compare;
            }

            internal void Run()
            {
                var remaining = _work.Length;
                var low = 0;
                var minRunLength = MinRunLength(remaining);
                while (remaining != 0)
                {
                    var currentRunLength = CountAndMakeRun(low, low + remaining);
                    if (currentRunLength < minRunLength)
                    {
                        var forcedRunLength = Math.Min(minRunLength, remaining);
                        BinaryInsertionSort(low, low + currentRunLength, low + forcedRunLength);
                        currentRunLength = forcedRunLength;
                    }
                    _runBase[_runs] = low;
                    _runLength[_runs] = currentRunLength;
                    _runs++;
                    MergeCollapse();
                    low += currentRunLength;
                    remaining -= currentRunLength;
                }
                MergeForceCollapse();
            }

            private static int MinRunLength(int n)
            {
                var r = 0;
                while (n >= 64)
                {
                    r |= n & 1;
                    n >>= 1;
                }
                return n + r;
            }

            private int CountAndMakeRun(int lowArg, int high)
            {
                var low = lowArg + 1;
                if (low == high)
                {
                    return 1;
                }
                var runLength = 2;
                var isDescending = _compare(_work[low], _work[low - 1]) < 0;
                var previous = _work[low];
                for (var index = low + 1; index < high; index++)
                {
                    var current = _work[index];
                    var order = _compare(current, previous);
                    if (isDescending ? order >= 0 : order < 0)
                    {
                        break;
                    }
                    previous = current;
                    runLength++;
                }
                if (isDescending)
                {
                    Array.Reverse(_work, lowArg, runLength);
                }
                return runLength;
            }

            private void BinaryInsertionSort(int low, int startArg, int high)
            {
                for (var start = low == startArg ? startArg + 1 : startArg; start < high; start++)
                {
                    var left = low;
                    var right = start;
                    var pivot = _work[start];
                    while (left < right)
                    {
                        var mid = left + ((right - left) >> 1);
                        if (_compare(pivot, _work[mid]) < 0)
                        {
                            right = mid;
                        }
                        else
                        {
                            left = mid + 1;
                        }
                    }
                    Array.Copy(_work, left, _work, left + 1, start - left);
                    _work[left] = pivot;
                }
            }

            private bool RunInvariantEstablished(int n)
                => n < 2 || _runLength[n - 2] > _runLength[n - 1] + _runLength[n];

            private void MergeCollapse()
            {
                while (_runs > 1)
                {
                    var n = _runs - 2;
                    if (!RunInvariantEstablished(n + 1) || !RunInvariantEstablished(n))
                    {
                        if (_runLength[n - 1] < _runLength[n + 1])
                        {
                            n--;
                        }
                        MergeAt(n);
                    }
                    else if (_runLength[n] <= _runLength[n + 1])
                    {
                        MergeAt(n);
                    }
                    else
                    {
                        break;
                    }
                }
            }

            private void MergeForceCollapse()
            {
                while (_runs > 1)
                {
                    var n = _runs - 2;
                    if (n > 0 && _runLength[n - 1] < _runLength[n + 1])
                    {
                        n--;
                    }
                    MergeAt(n);
                }
            }

            private void MergeAt(int i)
            {
                var baseA = _runBase[i];
                var lengthA = _runLength[i];
                var baseB = _runBase[i + 1];
                var lengthB = _runLength[i + 1];
                _runLength[i] = lengthA + lengthB;
                if (i == _runs - 3)
                {
                    _runBase[i + 1] = _runBase[i + 2];
                    _runLength[i + 1] = _runLength[i + 2];
                }
                _runs--;

                var k = GallopRight(_work, _work[baseB], baseA, lengthA, 0);
                baseA += k;
                lengthA -= k;
                if (lengthA == 0)
                {
                    return;
                }
                lengthB = GallopLeft(_work, _work[baseA + lengthA - 1], baseB, lengthB, lengthB - 1);
                if (lengthB == 0)
                {
                    return;
                }
                if (lengthA <= lengthB)
                {
                    MergeLow(baseA, lengthA, baseB, lengthB);
                }
                else
                {
                    MergeHigh(baseA, lengthA, baseB, lengthB);
                }
            }

            private int GallopLeft(T[] array, T key, int @base, int length, int hint)
            {
                var lastOfs = 0;
                var offset = 1;
                if (_compare(array[@base + hint], key) < 0)
                {
                    var maxOfs = length - hint;
                    while (offset < maxOfs && _compare(array[@base + hint + offset], key) < 0)
                    {
                        lastOfs = offset;
                        offset = (offset << 1) + 1;
                    }
                    offset = Math.Min(offset, maxOfs);
                    lastOfs += hint;
                    offset += hint;
                }
                else
                {
                    var maxOfs = hint + 1;
                    while (offset < maxOfs && _compare(array[@base + hint - offset], key) >= 0)
                    {
                        lastOfs = offset;
                        offset = (offset << 1) + 1;
                    }
                    offset = Math.Min(offset, maxOfs);
                    var tmp = lastOfs;
                    lastOfs = hint - offset;
                    offset = hint - tmp;
                }
                lastOfs++;
                while (lastOfs < offset)
                {
                    var m = lastOfs + ((offset - lastOfs) >> 1);
                    if (_compare(array[@base + m], key) < 0)
                    {
                        lastOfs = m + 1;
                    }
                    else
                    {
                        offset = m;
                    }
                }
                return offset;
            }

            private int GallopRight(T[] array, T key, int @base, int length, int hint)
            {
                var lastOfs = 0;
                var offset = 1;
                if (_compare(key, array[@base + hint]) < 0)
                {
                    var maxOfs = hint + 1;
                    while (offset < maxOfs && _compare(key, array[@base + hint - offset]) < 0)
                    {
                        lastOfs = offset;
                        offset = (offset << 1) + 1;
                    }
                    offset = Math.Min(offset, maxOfs);
                    var tmp = lastOfs;
                    lastOfs = hint - offset;
                    offset = hint - tmp;
                }
                else
                {
                    var maxOfs = length - hint;
                    while (offset < maxOfs && _compare(key, array[@base + hint + offset]) >= 0)
                    {
                        lastOfs = offset;
                        offset = (offset << 1) + 1;
                    }
                    offset = Math.Min(offset, maxOfs);
                    lastOfs += hint;
                    offset += hint;
                }
                lastOfs++;
                while (lastOfs < offset)
                {
                    var m = lastOfs + ((offset - lastOfs) >> 1);
                    if (_compare(key, array[@base + m]) < 0)
                    {
                        offset = m;
                    }
                    else
                    {
                        lastOfs = m + 1;
                    }
                }
                return offset;
            }

            private T[] Temp(int length)
            {
                if (_temp.Length < length)
                {
                    _temp = new T[length];
                }
                return _temp;
            }

            private void MergeLow(int baseA, int lengthA, int baseB, int lengthB)
            {
                var temp = Temp(lengthA);
                Array.Copy(_work, baseA, temp, 0, lengthA);
                var dest = baseA;
                var cursorTemp = 0;
                var cursorB = baseB;
                _work[dest++] = _work[cursorB++];
                if (--lengthB == 0)
                {
                    Array.Copy(temp, cursorTemp, _work, dest, lengthA);
                    return;
                }
                if (lengthA == 1)
                {
                    Array.Copy(_work, cursorB, _work, dest, lengthB);
                    _work[dest + lengthB] = temp[cursorTemp];
                    return;
                }
                var minGallop = _minGallop;
                while (true)
                {
                    var winsA = 0;
                    var winsB = 0;
                    while (true)
                    {
                        if (_compare(_work[cursorB], temp[cursorTemp]) < 0)
                        {
                            _work[dest++] = _work[cursorB++];
                            winsB++;
                            lengthB--;
                            winsA = 0;
                            if (lengthB == 0)
                            {
                                goto Succeed;
                            }
                            if (winsB >= minGallop)
                            {
                                break;
                            }
                        }
                        else
                        {
                            _work[dest++] = temp[cursorTemp++];
                            winsA++;
                            lengthA--;
                            winsB = 0;
                            if (lengthA == 1)
                            {
                                goto CopyB;
                            }
                            if (winsA >= minGallop)
                            {
                                break;
                            }
                        }
                    }
                    minGallop++;
                    var firstIteration = true;
                    while (winsA >= MinGallopWins || winsB >= MinGallopWins || firstIteration)
                    {
                        firstIteration = false;
                        minGallop = Math.Max(1, minGallop - 1);
                        _minGallop = minGallop;
                        winsA = GallopRight(temp, _work[cursorB], cursorTemp, lengthA, 0);
                        if (winsA > 0)
                        {
                            Array.Copy(temp, cursorTemp, _work, dest, winsA);
                            dest += winsA;
                            cursorTemp += winsA;
                            lengthA -= winsA;
                            if (lengthA == 1)
                            {
                                goto CopyB;
                            }
                            if (lengthA == 0)
                            {
                                goto Succeed;
                            }
                        }
                        _work[dest++] = _work[cursorB++];
                        if (--lengthB == 0)
                        {
                            goto Succeed;
                        }
                        winsB = GallopLeft(_work, temp[cursorTemp], cursorB, lengthB, 0);
                        if (winsB > 0)
                        {
                            Array.Copy(_work, cursorB, _work, dest, winsB);
                            dest += winsB;
                            cursorB += winsB;
                            lengthB -= winsB;
                            if (lengthB == 0)
                            {
                                goto Succeed;
                            }
                        }
                        _work[dest++] = temp[cursorTemp++];
                        if (--lengthA == 1)
                        {
                            goto CopyB;
                        }
                    }
                    minGallop++;
                    _minGallop = minGallop;
                }

                Succeed:
                if (lengthA > 0)
                {
                    Array.Copy(temp, cursorTemp, _work, dest, lengthA);
                }
                return;

                CopyB:
                Array.Copy(_work, cursorB, _work, dest, lengthB);
                _work[dest + lengthB] = temp[cursorTemp];
            }

            private void MergeHigh(int baseA, int lengthA, int baseB, int lengthB)
            {
                var temp = Temp(lengthB);
                Array.Copy(_work, baseB, temp, 0, lengthB);
                var dest = baseB + lengthB - 1;
                var cursorTemp = lengthB - 1;
                var cursorA = baseA + lengthA - 1;
                _work[dest--] = _work[cursorA--];
                if (--lengthA == 0)
                {
                    goto Succeed;
                }
                if (lengthB == 1)
                {
                    goto CopyA;
                }
                var minGallop = _minGallop;
                while (true)
                {
                    var winsA = 0;
                    var winsB = 0;
                    while (true)
                    {
                        if (_compare(temp[cursorTemp], _work[cursorA]) < 0)
                        {
                            _work[dest--] = _work[cursorA--];
                            winsA++;
                            lengthA--;
                            winsB = 0;
                            if (lengthA == 0)
                            {
                                goto Succeed;
                            }
                            if (winsA >= minGallop)
                            {
                                break;
                            }
                        }
                        else
                        {
                            _work[dest--] = temp[cursorTemp--];
                            winsB++;
                            lengthB--;
                            winsA = 0;
                            if (lengthB == 1)
                            {
                                goto CopyA;
                            }
                            if (winsB >= minGallop)
                            {
                                break;
                            }
                        }
                    }
                    minGallop++;
                    var firstIteration = true;
                    while (winsA >= MinGallopWins || winsB >= MinGallopWins || firstIteration)
                    {
                        firstIteration = false;
                        minGallop = Math.Max(1, minGallop - 1);
                        _minGallop = minGallop;
                        var k = GallopRight(_work, temp[cursorTemp], baseA, lengthA, lengthA - 1);
                        winsA = lengthA - k;
                        if (winsA > 0)
                        {
                            dest -= winsA;
                            cursorA -= winsA;
                            Array.Copy(_work, cursorA + 1, _work, dest + 1, winsA);
                            lengthA -= winsA;
                            if (lengthA == 0)
                            {
                                goto Succeed;
                            }
                        }
                        _work[dest--] = temp[cursorTemp--];
                        if (--lengthB == 1)
                        {
                            goto CopyA;
                        }
                        k = GallopLeft(temp, _work[cursorA], 0, lengthB, lengthB - 1);
                        winsB = lengthB - k;
                        if (winsB > 0)
                        {
                            dest -= winsB;
                            cursorTemp -= winsB;
                            Array.Copy(temp, cursorTemp + 1, _work, dest + 1, winsB);
                            lengthB -= winsB;
                            if (lengthB == 1)
                            {
                                goto CopyA;
                            }
                            if (lengthB == 0)
                            {
                                goto Succeed;
                            }
                        }
                        _work[dest--] = _work[cursorA--];
                        if (--lengthA == 0)
                        {
                            goto Succeed;
                        }
                    }
                    minGallop++;
                    _minGallop = minGallop;
                }

                Succeed:
                if (lengthB > 0)
                {
                    Array.Copy(temp, 0, _work, dest - (lengthB - 1), lengthB);
                }
                return;

                CopyA:
                dest -= lengthA;
                cursorA -= lengthA;
                Array.Copy(_work, cursorA + 1, _work, dest + 1, lengthA);
                _work[dest] = temp[cursorTemp];
            }
        }
    }
}
