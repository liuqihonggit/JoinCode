// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using static Structura.Collections.SwissTableHelper;

namespace Structura.Collections
{
    /// <summary>
    /// 高性能可变哈希表，基于开放寻址法与 SIMD 批量探测实现，类似 Rust hashbrown。
    /// 使用控制字节（control bytes）标记桶状态，并通过 SIMD 指令一次性探测整组桶以加速查找。
    /// </summary>
    /// <typeparam name="TKey">键类型，不可为 null。</typeparam>
    /// <typeparam name="TValue">值类型。</typeparam>
    [DebuggerDisplay("Count = {Count}")]
    public partial class SwissTable<TKey, TValue> : IDictionary<TKey, TValue>, IDictionary, IReadOnlyDictionary<TKey, TValue> where TKey : notnull
    {
        // Term define:
        //   Capacity: The maximum number of non-empty items that a hash table can hold before scaling. (come from `EnsureCapacity`)
        //   count: the real count of stored items
        //   growth_left: due to the existing of tombstone, non-empty does not mean there is indeed a value.
        //   bucket: `Entry` in the code, which could hold a key-value pair

        // capacity = count + grow_left
        // entries.Length = capacity + tombstone + left_by_load_factor

        // this contains all meaningfull data but _version and _comparer
        // Why comparer is not in inner table:
        // When resize(which is more common), we always need to allocate everything but comparer.
        // Only when construct from another collection, user could assign a new comparer, we decide to treat this situation as a edge case.
        internal struct RawTableInner
        {
            // Number of elements that can be inserted before we need to grow the table
            // This need to be calculated individually, for the tombstone("DELETE")
            internal int _growth_left;
            // TODO: If we could make _controls memory aligned(explicit memory layout or _dummy variable?), we could use load_align rather than load to speed up
            // TODO: maybe _controls could be Span and allocate memory from unmanaged memory?
            internal byte[] _controls;
            internal Entry[]? _entries;
            // number of real values stored in the map
            // `items` in rust
            internal int _count;
            internal int _bucket_mask;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal void set_ctrl_h2(int index, int hash)
            {
                set_ctrl(index, h2(hash));
            }

            /// Sets a control byte, and possibly also the replicated control byte at
            /// the end of the array.
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal void set_ctrl(int index, byte ctrl)
            {
                // Replicate the first Group::WIDTH control bytes at the end of
                // the array without using a branch:
                // - If index >= Group::WIDTH then index == index2.
                // - Otherwise index2 == self.bucket_mask + 1 + index.
                //
                // The very last replicated control byte is never actually read because
                // we mask the initial index for unaligned loads, but we write it
                // anyways because it makes the set_ctrl implementation simpler.
                //
                // If there are fewer buckets than Group::WIDTH then this code will
                // replicate the buckets at the end of the trailing group. For example
                // with 2 buckets and a group size of 4, the control bytes will look
                // like this:
                //
                //     Real    |             Replicated
                // ---------------------------------------------
                // | [A] | [B] | [EMPTY] | [EMPTY] | [A] | [B] |
                // ---------------------------------------------
                var index2 = ((index - GROUP_WIDTH) & _bucket_mask) + GROUP_WIDTH;
                _controls[index] = ctrl;
                _controls[index2] = ctrl;
            }

            // always insert a new one
            // not check replace, caller should make sure
            internal int find_insert_slot(int hash)
            {
                return DispatchFindInsertSlot(hash, _controls, _bucket_mask);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal void record_item_insert_at(int index, byte old_ctrl, int hash)
            {
                _growth_left -= special_is_empty_with_int_return(old_ctrl);
                set_ctrl_h2(index, hash);
                _count += 1;
            }
        }

        internal IEqualityComparer<TKey>? _comparer;

        // Add/扩容/Clear 递增，Enumerator 检测到此变化立即抛 InvalidOperationException。
        internal int _version;

        // Remove 递增（不递增 _version）。Enumerator 检测到此变化时不抛异常，而是用 And 重算 BitMask 容忍并发删除。
        // 设计理由：Remove 不改变表结构（只设 tombstone），枚举器可通过 BitMask.And(新 BitMask) 安全地跳过已删条目。
        internal int _tolerantVersion;

        internal RawTableInner rawTable;

        /// <summary>
        /// 获取哈希表中实际存储的键值对数量。
        /// </summary>
        public int Count => this.rawTable._count;

        // The real space that the swisstable allocated
        // always be the power of 2
        // This means the upper bound is not Int32.MaxValue(0x7FFFF_FFFF), but 0x4000_0000
        // TODO: Should we throw an expection if user try to grow again when it has been largest?
        // This is hard(impossible?) to be larger, for the length of array is limited to 0x7FFFF_FFFF
        internal int _buckets => this.rawTable._bucket_mask + 1;

        /// <summary>
        /// 初始化一个空的 <see cref="SwissTable{TKey, TValue}"/> 实例，使用默认容量和默认相等比较器。
        /// </summary>
        public SwissTable() : this(0, null) { }

        /// <summary>
        /// 初始化一个具有指定初始容量的空 <see cref="SwissTable{TKey, TValue}"/> 实例，使用默认相等比较器。
        /// </summary>
        /// <param name="capacity">初始容量，哈希表在扩容前最多可容纳该数量的元素。</param>
        public SwissTable(int capacity) : this(capacity, null) { }

        /// <summary>
        /// 初始化一个空的 <see cref="SwissTable{TKey, TValue}"/> 实例，使用指定的相等比较器。
        /// </summary>
        /// <param name="comparer">用于键比较的相等比较器，为 null 时使用默认比较器。</param>
        public SwissTable(IEqualityComparer<TKey>? comparer) : this(0, comparer) { }

        /// <summary>
        /// 初始化一个具有指定初始容量和相等比较器的空 <see cref="SwissTable{TKey, TValue}"/> 实例。
        /// </summary>
        /// <param name="capacity">初始容量，哈希表在扩容前最多可容纳该数量的元素。</param>
        /// <param name="comparer">用于键比较的相等比较器，为 null 时使用默认比较器。</param>
        public SwissTable(int capacity, IEqualityComparer<TKey>? comparer)
        {
            InitializeInnerTable(capacity);

            InitializeComparer(comparer);
        }

        private void InitializeComparer(IEqualityComparer<TKey>? comparer)
        {
            if (comparer is not null && comparer != EqualityComparer<TKey>.Default)
            {
                this._comparer = comparer;
            }
        }

        /// <summary>
        /// 初始化一个新 <see cref="SwissTable{TKey, TValue}"/> 实例，包含从指定字典复制的所有键值对，使用默认相等比较器。
        /// </summary>
        /// <param name="dictionary">源字典，其元素将被复制到新实例中。</param>
        public SwissTable(IDictionary<TKey, TValue> dictionary) : this(dictionary, null) { }

        /// <summary>
        /// 初始化一个新 <see cref="SwissTable{TKey, TValue}"/> 实例，包含从指定字典复制的所有键值对，并使用指定的相等比较器。
        /// </summary>
        /// <param name="dictionary">源字典，其元素将被复制到新实例中。</param>
        /// <param name="comparer">用于键比较的相等比较器，为 null 时使用默认比较器。</param>
        public SwissTable(IDictionary<TKey, TValue> dictionary, IEqualityComparer<TKey>? comparer)
        {
            InitializeComparer(comparer);
            if (dictionary == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.dictionary);
            }

            CloneFromCollection(dictionary);
        }

        /// <summary>
        /// 初始化一个新 <see cref="SwissTable{TKey, TValue}"/> 实例，包含从指定集合复制的所有键值对，使用默认相等比较器。
        /// </summary>
        /// <param name="collection">源键值对集合，其元素将被复制到新实例中。</param>
        public SwissTable(IEnumerable<KeyValuePair<TKey, TValue>> collection) : this(collection, null) { }

        /// <summary>
        /// 初始化一个新 <see cref="SwissTable{TKey, TValue}"/> 实例，包含从指定集合复制的所有键值对，并使用指定的相等比较器。
        /// </summary>
        /// <param name="collection">源键值对集合，其元素将被复制到新实例中。</param>
        /// <param name="comparer">用于键比较的相等比较器，为 null 时使用默认比较器。</param>
        public SwissTable(IEnumerable<KeyValuePair<TKey, TValue>> collection, IEqualityComparer<TKey>? comparer)
        {
            InitializeComparer(comparer);
            if (collection == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.collection);
            }

            CloneFromCollection(collection);
        }

        /// <summary>
        /// 获取用于确定键相等性的 <see cref="IEqualityComparer{TKey}"/>。
        /// 若构造时未指定比较器，则返回 <see cref="EqualityComparer{T}"/> 的默认实例。
        /// </summary>
        public IEqualityComparer<TKey> Comparer
        {
            get
            {
                return _comparer ?? EqualityComparer<TKey>.Default;
            }
        }

        private void CloneFromCollection(IEnumerable<KeyValuePair<TKey, TValue>> collection)
        {
            // Initialize could be specified for Dicitonary, defer initialize

            // It is likely that the passed-in dictionary is SwissTable<TKey,TValue>. When this is the case,
            // avoid the enumerator allocation and overhead by looping through the entries array directly.
            // We only do this when dictionary is SwissTable<TKey,TValue> and not a subclass, to maintain
            // back-compat with subclasses that may have overridden the enumerator behavior.
            if (collection.GetType() == typeof(SwissTable<TKey, TValue>))
            {
                SwissTable<TKey, TValue> source = (SwissTable<TKey, TValue>)collection;
                CloneFromDictionary(source);
                return;
            }

            InitializeInnerTable((collection as ICollection<KeyValuePair<TKey, TValue>>)?.Count ?? 0);
            // Fallback path for IEnumerable that isn't a non-subclassed SwissTable<TKey,TValue>.
            foreach (KeyValuePair<TKey, TValue> pair in collection)
            {
                Add(pair.Key, pair.Value);
            }
        }

        private void CloneFromDictionary(SwissTable<TKey, TValue> source)
        {
            if (source.Count == 0)
            {
                // TODO: maybe just InitializeInnerTable(0)? could CLR optimsie this?
                this.rawTable = NewEmptyInnerTable();
                return;
            }

            var oldEntries = source.rawTable._entries;
            byte[] oldCtrls = source.rawTable._controls;

            Debug.Assert(oldEntries != null);

            this.rawTable = NewInnerTableWithControlUninitialized(source._buckets);

            if (this._comparer == source._comparer)
            {
                // here is why we initial non-empty innter-table with uninitialized array.
                Array.Copy(source.rawTable._controls, this.rawTable._controls, oldCtrls.Length);
                Debug.Assert(this.rawTable._entries != null);
                var newEntries = this.rawTable._entries;
                for (int i = 0; i < oldEntries.Length; i++)
                {
                    if (is_full(this.rawTable._controls[i]))
                    {
                        newEntries[i] = oldEntries[i];
                    }
                }
                this.rawTable._growth_left = bucket_mask_to_capacity(this.rawTable._bucket_mask) - source.rawTable._count;
                this.rawTable._count = source.rawTable._count;
                return;
            }

            Array.Fill(this.rawTable._controls, EMPTY);
            // TODO: Maybe we could use IBITMASK to accllerate
            for (int i = 0; i < oldEntries.Length; i++)
            {
                if (is_full(oldCtrls[i]))
                {
                    Add(oldEntries[i].Key, oldEntries[i].Value);
                }
            }
            return;
        }

        /// <summary>
        /// 将指定的键值对添加到哈希表中。若键已存在，则抛出异常。
        /// </summary>
        /// <param name="key">要添加的键，不可为 null。</param>
        /// <param name="value">要添加的值。</param>
        public void Add(TKey key, TValue value)
        {
            bool modified = TryInsert(key, value, InsertionBehavior.ThrowOnExisting);
            Debug.Assert(modified); // If there was an existing key and the Add failed, an exception will already have been thrown.
        }

        /// <summary>
        /// 确定哈希表是否包含指定的键。
        /// </summary>
        /// <param name="key">要查找的键，不可为 null。</param>
        /// <returns>若哈希表包含该键则返回 true，否则返回 false。</returns>
        public bool ContainsKey(TKey key) =>
            !Unsafe.IsNullRef(ref FindBucket(key));

        /// <summary>
        /// 确定哈希表是否包含指定的值。
        /// </summary>
        /// <param name="value">要查找的值。</param>
        /// <returns>若哈希表包含该值则返回 true，否则返回 false。</returns>
        public bool ContainsValue(TValue value)
        {
            // TODO: "inline" to get better performance
            foreach (var item in new ValueCollection(this))
            {
                if (EqualityComparer<TValue>.Default.Equals(item, value))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 从哈希表中移除具有指定键的元素。
        /// </summary>
        /// <param name="key">要移除的元素的键，不可为 null。</param>
        /// <returns>若成功移除元素则返回 true；若键不存在则返回 false。</returns>
        public bool Remove(TKey key)
        {
            // TODO: maybe need to duplicate most of code with `Remove(TKey key, out TValue value)` for performance issue, see C# old implementation
            if (key == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.key);
            }
            if (this.rawTable._entries != null)
            {
                var index = this.FindBucketIndex(key);
                if (index >= 0)
                {
                    this.erase(index);
                    _tolerantVersion++;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 从哈希表中移除具有指定键的元素，并通过输出参数返回被移除元素的值。
        /// </summary>
        /// <param name="key">要移除的元素的键，不可为 null。</param>
        /// <param name="value">当成功移除时，包含被移除元素的值；否则包含该类型的默认值。</param>
        /// <returns>若成功移除元素则返回 true；若键不存在则返回 false。</returns>
        public bool Remove(TKey key, [MaybeNullWhen(false)] out TValue value)
        {
            // TODO: maybe need to duplicate most of code with `Remove(TKey key, out TValue value)` for performance issue, see C# old implementation
            if (key == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.key);
            }
            if (this.rawTable._entries != null)
            {
                var index = this.FindBucketIndex(key);
                if (index >= 0)
                {
                    value = this.erase(index);
                    _tolerantVersion++;
                    return true;
                }
            }
            value = default;
            return false;
        }

        /// <summary>
        /// 尝试将指定的键值对添加到哈希表中。若键已存在则不修改现有元素。
        /// </summary>
        /// <param name="key">要添加的键，不可为 null。</param>
        /// <param name="value">要添加的值。</param>
        /// <returns>若键不存在且成功添加则返回 true；若键已存在则返回 false。</returns>
        public bool TryAdd(TKey key, TValue value) =>
            TryInsert(key, value, InsertionBehavior.None);

        private KeyCollection? _keys;
        /// <summary>
        /// 获取包含哈希表中所有键的 <see cref="KeyCollection"/>。
        /// </summary>
        public KeyCollection Keys => _keys ??= new KeyCollection(this);

        private ValueCollection? _values;
        /// <summary>
        /// 获取包含哈希表中所有值的 <see cref="ValueCollection"/>。
        /// </summary>
        public ValueCollection Values => _values ??= new ValueCollection(this);

        /// <summary>
        /// 获取或设置与指定键关联的值。获取时若键不存在则抛出 <see cref="KeyNotFoundException"/>；设置时若键已存在则覆盖原值，否则添加新键值对。
        /// </summary>
        /// <param name="key">要获取或设置的键，不可为 null。</param>
        /// <returns>与指定键关联的值。</returns>
        public TValue this[TKey key]
        {
            get
            {
                ref Entry entry = ref FindBucket(key);
                if (!Unsafe.IsNullRef(ref entry))
                {
                    return entry.Value;
                }

                ThrowHelper.ThrowKeyNotFoundException(key);
                return default;
            }
            set
            {
                bool modified = TryInsert(key, value, InsertionBehavior.OverwriteExisting);
                Debug.Assert(modified);
            }
        }

        /// <summary>
        /// 尝试获取与指定键关联的值。
        /// </summary>
        /// <param name="key">要查找的键，不可为 null。</param>
        /// <param name="value">当找到键时，包含关联的值；否则包含该类型的默认值。</param>
        /// <returns>若找到键则返回 true；否则返回 false。</returns>
        public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
        {
            ref Entry entry = ref FindBucket(key);
            if (!Unsafe.IsNullRef(ref entry))
            {
                value = entry.Value;
                return true;
            }

            value = default;
            return false;
        }

        /// <summary>
        /// 从哈希表中移除所有键值对，将控制字节重置为空状态并清空条目数组。
        /// </summary>
        public void Clear()
        {
            int count = rawTable._count;
            if (count > 0)
            {
                Debug.Assert(rawTable._entries != null, "_entries should be non-null");

                Array.Fill(rawTable._controls, EMPTY);
                rawTable._count = 0;
                rawTable._growth_left = SwissTable<TKey, TValue>.bucket_mask_to_capacity(rawTable._bucket_mask);
                if (RuntimeHelpers.IsReferenceOrContainsReferences<TValue>()
                    || RuntimeHelpers.IsReferenceOrContainsReferences<TKey>())
                {
                    Array.Clear(rawTable._entries);
                }
                _version++;
                _tolerantVersion++;
            }
        }

        private static bool IsCompatibleKey(object key)
        {
            if (key == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.key);
            }
            return key is TKey;
        }

        private void CopyTo(KeyValuePair<TKey, TValue>[] array, int index)
        {
            if (array == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.array);
            }

            if ((uint)index > (uint)array.Length)
            {
                ThrowHelper.ThrowIndexArgumentOutOfRange_NeedNonNegNumException();
            }

            if (array.Length - index < Count)
            {
                ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_ArrayPlusOffTooSmall);
            }

            CopyToWorker(array, index);
        }

        // This method not check whether array and index, maybe the name should be CopyToUnsafe?
        private void CopyToWorker(KeyValuePair<TKey, TValue>[] array, int index)
        {
            // TODO: maybe we could fix the array then it might be safe to use load_align
            DispatchCopyToArrayFromDictionaryWorker(this, array, index);
        }

        #region IDictionary
        bool ICollection.IsSynchronized => false;

        object ICollection.SyncRoot => this;

        bool IDictionary.IsFixedSize => false;

        bool IDictionary.IsReadOnly => false;

        ICollection IDictionary.Keys => Keys;

        ICollection IDictionary.Values => Values;


        object? IDictionary.this[object key]
        {
            get
            {
                if (IsCompatibleKey(key))
                {
                    ref Entry entry = ref FindBucket((TKey)key);
                    if (!Unsafe.IsNullRef(ref entry))
                    {
                        return entry.Value;
                    }
                }

                return null;
            }
            set
            {
                if (key == null)
                {
                    ThrowHelper.ThrowArgumentNullException(ExceptionArgument.key);
                }
                ThrowHelper.IfNullAndNullsAreIllegalThenThrow<TValue>(value, ExceptionArgument.value);

                try
                {
                    TKey tempKey = (TKey)key;
                    try
                    {
                        this[tempKey] = (TValue)value!;
                    }
                    catch (InvalidCastException)
                    {
                        ThrowHelper.ThrowWrongValueTypeArgumentException(value, typeof(TValue));
                    }
                }
                catch (InvalidCastException)
                {
                    ThrowHelper.ThrowWrongKeyTypeArgumentException(key, typeof(TKey));
                }
            }
        }

        void IDictionary.Add(object key, object? value)
        {
            if (key == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.key);
            }
            ThrowHelper.IfNullAndNullsAreIllegalThenThrow<TValue>(value, ExceptionArgument.value);

            try
            {
                TKey tempKey = (TKey)key;

                try
                {
                    Add(tempKey, (TValue)value!);
                }
                catch (InvalidCastException)
                {
                    ThrowHelper.ThrowWrongValueTypeArgumentException(value, typeof(TValue));
                }
            }
            catch (InvalidCastException)
            {
                ThrowHelper.ThrowWrongKeyTypeArgumentException(key, typeof(TKey));
            }
        }

        bool IDictionary.Contains(object key)
        {
            if (IsCompatibleKey(key))
            {
                return ContainsKey((TKey)key);
            }

            return false;
        }

        IDictionaryEnumerator IDictionary.GetEnumerator() => new Enumerator(this, Enumerator.DictEntry);

        void IDictionary.Remove(object key)
        {
            if (IsCompatibleKey(key))
            {
                Remove((TKey)key);
            }
        }

        void ICollection.CopyTo(Array array, int index)
        {
            if (array == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.array);
            }

            if (array.Rank != 1)
            {
                ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_RankMultiDimNotSupported);
            }

            if (array.GetLowerBound(0) != 0)
            {
                ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_NonZeroLowerBound);
            }

            if ((uint)index > (uint)array.Length)
            {
                ThrowHelper.ThrowIndexArgumentOutOfRange_NeedNonNegNumException();
            }

            if (array.Length - index < Count)
            {
                ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_ArrayPlusOffTooSmall);
            }

            if (array is KeyValuePair<TKey, TValue>[] pairs)
            {
                CopyToWorker(pairs, index);
            }
            else if (array is DictionaryEntry[] dictEntryArray)
            {
                foreach (var item in this)
                {
                    dictEntryArray[index++] = new DictionaryEntry(item.Key, item.Value);
                }
            }
            else
            {
                object[]? objects = array as object[];

                if (objects == null)
                {
                    ThrowHelper.ThrowArgumentException_Argument_InvalidArrayType();
                }

                try
                {
                    foreach (var item in this)
                    {
                        objects[index++] = new KeyValuePair<TKey, TValue>(item.Key, item.Value);
                    }
                }
                catch (ArrayTypeMismatchException)
                {
                    ThrowHelper.ThrowArgumentException_Argument_InvalidArrayType();
                }
            }
        }
        #endregion

        #region IReadOnlyDictionary<TKey, TValue>
        IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => Keys;

        IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => Values;
        #endregion

        #region IDictionary<TKey, TValue>
        ICollection<TKey> IDictionary<TKey, TValue>.Keys => Keys;
        ICollection<TValue> IDictionary<TKey, TValue>.Values => Values;
        #endregion

        #region ICollection<KeyValuePair<TKey, TValue>>
        bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => false;

        void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> keyValuePair) =>
            Add(keyValuePair.Key, keyValuePair.Value);

        bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> keyValuePair)
        {
            ref Entry bucket = ref FindBucket(keyValuePair.Key);
            if (!Unsafe.IsNullRef(ref bucket) && EqualityComparer<TValue>.Default.Equals(bucket.Value, keyValuePair.Value))
            {
                return true;
            }

            return false;
        }

        void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int index) =>
            CopyTo(array, index);


        bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> keyValuePair)
        {
            ref Entry bucket = ref FindBucket(keyValuePair.Key);
            if (!Unsafe.IsNullRef(ref bucket) && EqualityComparer<TValue>.Default.Equals(bucket.Value, keyValuePair.Value))
            {
                Remove(keyValuePair.Key);
                return true;
            }

            return false;
        }
        #endregion

        #region IEnumerable<KeyValuePair<TKey, TValue>>
        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() =>
            new Enumerator(this, Enumerator.KeyValuePair);
        #endregion

        #region IEnumerable
        IEnumerator IEnumerable.GetEnumerator() => new Enumerator(this, Enumerator.KeyValuePair);
        #endregion

        /// <summary>
        /// 返回一个遍历哈希表中所有键值对的 <see cref="Enumerator"/>。
        /// </summary>
        /// <returns>可用于遍历哈希表的枚举器。</returns>
        public Enumerator GetEnumerator() => new Enumerator(this, Enumerator.KeyValuePair);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetHashCodeOfKey(TKey key)
        {
            return (this._comparer == null) ? key.GetHashCode() : this._comparer.GetHashCode(key);
        }

        /// <summary>
        /// The real implementation of any public insert behavior
        /// Do some extra work other than insert
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="behavior"></param>
        /// <returns></returns>
        private bool TryInsert(TKey key, TValue value, InsertionBehavior behavior)
        {
            // NOTE: this method is mirrored in CollectionsMarshal.GetValueRefOrAddDefault below.
            // If you make any changes here, make sure to keep that version in sync as well.
            if (key == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.key);
            }
            var hashOfKey = this.GetHashCodeOfKey(key);
            ref var bucket = ref this.FindBucket(key, hashOfKey);
            // replace
            if (!Unsafe.IsNullRef(ref bucket))
            {
                if (behavior == InsertionBehavior.OverwriteExisting)
                {
                    bucket.Key = key;
                    bucket.Value = value;
                    return true;
                }
                if (behavior == InsertionBehavior.ThrowOnExisting)
                {
                    ThrowHelper.ThrowAddingDuplicateWithKeyArgumentException(key);
                }
                // InsertionBehavior.None
                return false;
            }
            // insert new
            // We can avoid growing the table once we have reached our load
            // factor if we are replacing a tombstone(Delete). This works since the
            // number of EMPTY slots does not change in this case.
            var index = this.rawTable.find_insert_slot(hashOfKey);
            var old_ctrl = rawTable._controls[index];
            if (this.rawTable._growth_left == 0 && special_is_empty(old_ctrl))
            {
                this.EnsureCapacityWorker(this.rawTable._count + 1);
                index = this.rawTable.find_insert_slot(hashOfKey);
            }
            Debug.Assert(rawTable._entries != null);
            this.rawTable.record_item_insert_at(index, old_ctrl, hashOfKey);
            ref var targetEntry = ref this.rawTable._entries[index];
            targetEntry.Key = key;
            targetEntry.Value = value;
            _version++;
            return true;
        }

        /// <summary>
        /// A helper class containing APIs exposed through <c>CollectionsMarshal</c>.
        /// These methods are relatively niche and only used in specific scenarios, so adding them in a separate type avoids
        /// the additional overhead on each <see cref="SwissTable{TKey, TValue}"/> instantiation, especially in AOT scenarios.
        /// </summary>
        public static class CollectionsMarshalHelper
        {
            /// <summary>
            /// 获取指定键对应值的引用，若键不存在则添加默认值并返回该引用。
            /// 镜像 <c>CollectionsMarshal.GetValueRefOrAddDefault</c> 的语义。
            /// </summary>
            /// <param name="dictionary">目标字典。</param>
            /// <param name="key">要查找或添加的键。</param>
            /// <param name="exists">键已存在时输出 <c>true</c>；新添加时输出 <c>false</c>。</param>
            /// <returns>键对应值的引用。</returns>
            public static ref TValue? GetValueRefOrAddDefault(SwissTable<TKey, TValue> dictionary, TKey key, out bool exists)
            {
                // NOTE: this method is mirrored by SwissTable<TKey, TValue>.TryInsert above.
                // If you make any changes here, make sure to keep that version in sync as well.

                ref var bucket = ref dictionary.FindBucket(key);
                // replace
                if (!Unsafe.IsNullRef(ref bucket))
                {
                    exists = true;
                    return ref bucket.Value!;
                }
                // insert new
                var hashCode = dictionary.GetHashCodeOfKey(key);
                // We can avoid growing the table once we have reached our load
                // factor if we are replacing a tombstone(Delete). This works since the
                // number of EMPTY slots does not change in this case.
                var index = dictionary.rawTable.find_insert_slot(hashCode);
                var old_ctrl = dictionary.rawTable._controls[index];
                if (dictionary.rawTable._growth_left == 0 && special_is_empty(old_ctrl))
                {
                    dictionary.EnsureCapacityWorker(dictionary.rawTable._count + 1);
                    index = dictionary.rawTable.find_insert_slot(hashCode);
                }
                Debug.Assert(dictionary.rawTable._entries != null);
                dictionary.rawTable.record_item_insert_at(index, old_ctrl, hashCode);
                dictionary.rawTable._entries[index].Key = key;
                dictionary.rawTable._entries[index].Value = default!;
                dictionary._version++;
                exists = false;
                return ref dictionary.rawTable._entries[index].Value!;
            }
        }

        internal struct Entry
        {
            internal TKey Key;
            internal TValue Value;
        }

        // allocate and initialize when with real capacity
        // Note that the _entries might still not allocated
        // this means we do not want to use any existing data, including resize or use dictionary initialize
        // `realisticCapacity` is any positive number
        [SkipLocalsInit]
        private void InitializeInnerTable(int realisticCapacity)
        {
            if (realisticCapacity < 0)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.capacity);
            }

            if (realisticCapacity == 0)
            {
                this.rawTable = NewEmptyInnerTable();
                return;
            }
            var idealCapacity = capacity_to_buckets(realisticCapacity);
            this.rawTable = NewInnerTableWithControlUninitialized(idealCapacity);
            Array.Fill(this.rawTable._controls, EMPTY);
        }

        // TODO: check whether we need NonRandomizedStringEqualityComparer
        // If we need rehash, we should learn from rust.
        // resize to 0 capaciry is a special simple case, Which is not handled here.
        private void Grow(int realisticCapacity)
        {
            Debug.Assert(this.rawTable._entries != null);
            var idealCapacity = capacity_to_buckets(realisticCapacity);
            GrowWorker(idealCapacity);
        }

        [SkipLocalsInit]
        private void GrowWorker(int idealEntryLength)
        {
            Debug.Assert(idealEntryLength >= rawTable._count);

            var newTable = NewInnerTableWithControlUninitialized(idealEntryLength);
            Array.Fill(newTable._controls, EMPTY);

            Debug.Assert(rawTable._entries != null);
            Debug.Assert(newTable._entries != null);
            Debug.Assert(newTable._count == 0);
            Debug.Assert(newTable._growth_left >= rawTable._count);

            // We can use a simple version of insert() here since:
            // - there are no DELETED entries.
            // - we know there is the same enough space in the table.
            byte[] oldCtrls = rawTable._controls;
            Entry[] oldEntries = rawTable._entries;
            Entry[] newEntries = newTable._entries;
            int length = rawTable._entries.Length;
            // TODO: Maybe we could use IBITMASK to accllerate
            for (int i = 0; i < length; i++)
            {
                if (is_full(oldCtrls[i]))
                {
                    var key = oldEntries[i].Key;
                    var hash = GetHashCodeOfKey(key);
                    var index = newTable.find_insert_slot(hash);
                    newTable.set_ctrl_h2(index, hash);
                    newEntries[index] = oldEntries[i];
                }
            }
            newTable._growth_left -= rawTable._count;
            newTable._count = rawTable._count;

            this.rawTable = newTable;
        }

        /// <summary>
        /// Ensures that the dictionary can hold up to 'capacity' entries without any further expansion of its backing storage
        /// </summary>
        public int EnsureCapacity(int capacity)
        {
            // "capacity" is `this._count + this._growth_left` in the new implementation
            if (capacity < 0)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.capacity);
            }
            int currentCapacity = this.rawTable._count + this.rawTable._growth_left;
            if (currentCapacity >= capacity)
            {
                return currentCapacity;
            }

            EnsureCapacityWorker(capacity);

            return this.rawTable._count + this.rawTable._growth_left;
        }

        private void EnsureCapacityWorker(int capacity)
        {
            _version++;

            if (this.rawTable._entries == null)
            {
                this.InitializeInnerTable(capacity);
            }
            else
            {
                this.Grow(capacity);
            }
        }
        /// <summary>
        /// Sets the capacity of this dictionary to what it would be if it had been originally initialized with all its entries
        /// </summary>
        /// <remarks>
        /// This method can be used to minimize the memory overhead
        /// once it is known that no new elements will be added.
        ///
        /// To allocate minimum size storage array, execute the following statements:
        ///
        /// dictionary.Clear();
        /// dictionary.TrimExcess();
        /// </remarks>
        public void TrimExcess() => TrimExcess(Count);

        /// <summary>
        /// Sets the capacity of this dictionary to hold up 'capacity' entries without any further expansion of its backing storage
        /// </summary>
        /// <remarks>
        /// This method can be used to minimize the memory overhead
        /// once it is known that no new elements will be added.
        /// </remarks>
        public void TrimExcess(int capacity)
        {
            if (capacity < Count)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.capacity);
            }

            if (capacity == 0)
            {
                _version++;
                // TODO: No need to initialize if _entry is null.
                this.InitializeInnerTable(capacity);
                return;
            }

            var idealBuckets = capacity_to_buckets(capacity);

            _version++;
            this.GrowWorker(idealBuckets);
        }

        /// <summary>
        /// Creates a new empty hash table without allocating any memory, using the
        /// given allocator.
        ///
        /// In effect this returns a table with exactly 1 bucket. However we can
        /// leave the data pointer dangling since that bucket is never written to
        /// due to our load factor forcing us to always have at least 1 free bucket.
        /// </summary>
        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static RawTableInner NewEmptyInnerTable()
        {
            return new RawTableInner
            {
                _bucket_mask = 0,
                _controls = DispatchGetEmptyControls(),
                _entries = null,
                _growth_left = 0,
                _count = 0
            };
        }

        /// <summary>
        /// Allocates a new hash table with the given number of buckets.
        ///
        /// The control bytes are initialized with EMPTY.
        /// </summary>
        // unlike rust, we never cares about out of memory
        // TODO: Maybe ref to improve performance?
        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static RawTableInner NewInnerTableWithControlUninitialized(int buckets)
        {
            Debug.Assert(BitOperations.IsPow2(buckets));
            return new RawTableInner
            {
                _bucket_mask = buckets - 1,
                _controls = new byte[buckets + GROUP_WIDTH],
                _entries = new Entry[buckets],
                _growth_left = bucket_mask_to_capacity(buckets - 1),
                _count = 0
            };
        }

        internal ref TValue FindValue(TKey key)
        {
            // TODO: We might choose to dulpcate here too just like FindBucketIndex, but not now.
            ref Entry bucket = ref FindBucket(key);
            if (Unsafe.IsNullRef(ref bucket))
            {
                return ref Unsafe.NullRef<TValue>();
            }
            return ref bucket.Value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ref Entry FindBucket(TKey key)
        {
            if (key == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.key);
            }
            var hash = GetHashCodeOfKey(key);
            return ref DispatchFindBucketOfDictionary(this, key, hash);
        }

        // Sometimes we need to reuse hash, do not calcualte it twice
        // the caller should check key is not null, for it should get hash first.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ref Entry FindBucket(TKey key, int hashOfKey)
        {
            return ref DispatchFindBucketOfDictionary(this, key, hashOfKey);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int FindBucketIndex(TKey key)
        {
            if (key == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.key);
            }
            return DispatchFindBucketIndexOfDictionary(this, key);
        }

        private TValue erase(int index)
        {
            // Attention, we could not just only set mark to `Deleted` to assume it is deleted, the reference is still here, and GC would not collect it.
            Debug.Assert(is_full(rawTable._controls[index]));
            Debug.Assert(rawTable._entries != null, "entries should be non-null");
            var isEraseSafeToSetEmptyControlFlag = DispatchIsEraseSafeToSetEmptyControlFlag(rawTable._bucket_mask, rawTable._controls, index);
            TValue res;
            byte ctrl;
            if (isEraseSafeToSetEmptyControlFlag)
            {
                ctrl = EMPTY;
                rawTable._growth_left += 1;
            }
            else
            {
                ctrl = DELETED;
            }

            this.rawTable.set_ctrl(index, ctrl);
            rawTable._count -= 1;

            res = this.rawTable._entries[index].Value;

            // TODO: maybe we could remove this branch to improve perf. Or maybe CLR has optimised this.
            if (RuntimeHelpers.IsReferenceOrContainsReferences<TKey>())
            {
                this.rawTable._entries[index].Key = default!;
            }

            if (RuntimeHelpers.IsReferenceOrContainsReferences<TValue>())
            {
                this.rawTable._entries[index].Value = default!;
            }

            return res;
        }

        /// Returns the number of buckets needed to hold the given number of items,
        /// taking the maximum load factor into account.
        private static int capacity_to_buckets(int cap)
        {
            Debug.Assert(cap > 0);
            var capacity = (uint)cap;

            // For small tables we require at least 1 empty bucket so that lookups are
            // guaranteed to terminate if an element doesn't exist in the table.
            if (capacity < 8)
            {
                // We don't bother with a table size of 2 buckets since that can only
                // hold a single element. Instead we skip directly to a 4 bucket table
                // which can hold 3 elements.
                return (capacity < 4 ? 4 : 8);
            }

            // Otherwise require 1/8 buckets to be empty (87.5% load)
            //
            // Be careful when modifying this, calculate_layout relies on the
            // overflow check here.
            uint adjusted_capacity;
            switch (capacity)
            {
                // 0x01FFFFFF is the max value that would not overflow when *8
                case <= 0x01FFFFFF:
                    adjusted_capacity = unchecked(capacity * 8 / 7);
                    break;
                // 0x37FFFFFF is the max value that smaller than 0x0400_0000 after *8/7
                case <= 0x37FFFFFF:
                    return 0x4000_0000;
                default:
                    throw new Exception("capacity overflow");
            }

            // Any overflows will have been caught by the checked_mul. Also, any
            // rounding errors from the division above will be cleaned up by
            // next_power_of_two (which can't overflow because of the previous divison).
            return nextPowerOfTwo(adjusted_capacity);

            static int nextPowerOfTwo(uint num)
            {
                return (int)(0x01u << (32 - BitOperations.LeadingZeroCount(num)));
            }
        }

        /// Returns the maximum effective capacity for the given bucket mask, taking
        /// the maximum load factor into account.
        private static int bucket_mask_to_capacity(int bucket_mask)
        {
            if (bucket_mask < 8)
            {
                // For tables with 1/2/4/8 buckets, we always reserve one empty slot.
                // Keep in mind that the bucket mask is one less than the bucket count.
                return bucket_mask;
            }
            else
            {
                // For larger tables we reserve 12.5% of the slots as empty.
                return ((bucket_mask + 1) >> 3) * 7; // bucket_mask / 8 * 7, but it will generate a bit more complex for int, maybe we should use uint?
            }
        }

        /// <summary>
        /// 遍历 <see cref="SwissTable{TKey, TValue}"/> 中所有键值对的枚举器结构。
        /// 通过控制字节位掩码与 SIMD 批量探测加速遍历过程。
        /// </summary>
        public struct Enumerator : IEnumerator<KeyValuePair<TKey, TValue>>, IDictionaryEnumerator
        {
            private readonly SwissTable<TKey, TValue> _dictionary;
            private readonly int _version;
            private readonly int _tolerantVersion;
            private KeyValuePair<TKey, TValue> _current;
            private BitMaskUnion _currentBitMask;
            internal int _currentCtrlOffset;
            private readonly int _getEnumeratorRetType;
            private bool _isValid; // valid when _current has correct value.
            internal const int DictEntry = 1;
            internal const int KeyValuePair = 2;

            internal Enumerator(SwissTable<TKey, TValue> dictionary, int getEnumeratorRetType)
            {
                _dictionary = dictionary;
                _version = dictionary._version;
                _tolerantVersion = dictionary._tolerantVersion;
                _getEnumeratorRetType = getEnumeratorRetType;
                _current = default;
                _currentCtrlOffset = 0;
                _isValid = false;
                _currentBitMask = DispatchGetMatchFullBitMask(_dictionary.rawTable._controls, 0);
            }

            #region IDictionaryEnumerator
            DictionaryEntry IDictionaryEnumerator.Entry
            {
                get
                {
                    if (!_isValid)
                    {
                        ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumOpCantHappen();
                    }
                    Debug.Assert(_current.Key != null);
                    return new DictionaryEntry(_current.Key, _current.Value);
                }
            }

            object IDictionaryEnumerator.Key
            {
                get
                {
                    if (!_isValid)
                    {
                        ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumOpCantHappen();
                    }
                    Debug.Assert(_current.Key != null);
                    return _current.Key;
                }
            }

            object? IDictionaryEnumerator.Value
            {
                get
                {
                    if (!_isValid)
                    {
                        ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumOpCantHappen();
                    }
                    return _current.Value;
                }
            }
            #endregion

            #region IEnumerator<KeyValuePair<TKey, TValue>>
            /// <summary>
            /// 获取枚举器当前指向的键值对。
            /// </summary>
            public KeyValuePair<TKey, TValue> Current => this._current;

            object? IEnumerator.Current
            {
                get
                {
                    if (!_isValid)
                    {
                        ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumOpCantHappen();
                    }

                    Debug.Assert(_current.Key != null);

                    if (_getEnumeratorRetType == DictEntry)
                    {
                        return new DictionaryEntry(_current.Key, _current.Value);
                    }

                    return new KeyValuePair<TKey, TValue>(_current.Key, _current.Value);
                }
            }

            /// <summary>
            /// 释放枚举器占用的资源。本枚举器无托管或非托管资源需要释放，方法体为空。
            /// </summary>
            public void Dispose() { }

            /// <summary>
            /// 将枚举器推进到哈希表的下一个元素。
            /// </summary>
            /// <returns>若成功推进到下一个元素则返回 true；若已遍历到末尾则返回 false。</returns>
            public bool MoveNext()
            {
                ref var entry = ref DispatchMoveNextDictionary(_version, _tolerantVersion, _dictionary, ref _currentCtrlOffset, ref _currentBitMask);
                if (!Unsafe.IsNullRef(ref entry))
                {
                    _isValid = true;
                    _current = new KeyValuePair<TKey, TValue>(entry.Key, entry.Value);
                    return true;
                }
                else
                {
                    this._current = default;
                    _isValid = false;
                    return false;
                }
            }
            void IEnumerator.Reset()
            {
                if (_version != _dictionary._version)
                {
                    ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumFailedVersion();
                }
                _current = default;
                _currentCtrlOffset = 0;
                _isValid = false;
                _currentBitMask = DispatchGetMatchFullBitMask(_dictionary.rawTable._controls, 0);
            }
            #endregion
        }

        /// <summary>
        /// 表示 <see cref="SwissTable{TKey, TValue}"/> 中键的集合，不包含重复元素。
        /// 本集合为关联哈希表的实时视图，对哈希表的修改会立即反映到本集合中。
        /// </summary>
        [DebuggerDisplay("Count = {Count}")]
        public sealed class KeyCollection : ICollection<TKey>, ICollection, IReadOnlyCollection<TKey>
        {
            private readonly SwissTable<TKey, TValue> _dictionary;

            /// <summary>
            /// 初始化 <see cref="KeyCollection"/> 的新实例，作为指定哈希表的键视图。
            /// </summary>
            /// <param name="dictionary">关联的哈希表，不可为 null。</param>
            public KeyCollection(SwissTable<TKey, TValue> dictionary)
            {
                if (dictionary == null)
                {
                    ThrowHelper.ThrowArgumentNullException(ExceptionArgument.dictionary);
                }

                _dictionary = dictionary;
            }

            /// <summary>
            /// 返回一个遍历键集合的 <see cref="Enumerator"/>。
            /// </summary>
            /// <returns>可用于遍历键集合的枚举器。</returns>
            public Enumerator GetEnumerator() => new Enumerator(_dictionary);

            /// <summary>
            /// 从指定索引开始，将键集合的元素复制到目标数组。
            /// </summary>
            /// <param name="array">目标数组，不可为 null。</param>
            /// <param name="index">目标数组中开始写入的零起始索引。</param>
            public void CopyTo(TKey[] array, int index)
            {
                if (array == null)
                {
                    ThrowHelper.ThrowArgumentNullException(ExceptionArgument.array);
                }

                Debug.Assert(array != null);

                if (index < 0 || index > array.Length)
                {
                    ThrowHelper.ThrowIndexArgumentOutOfRange_NeedNonNegNumException();
                }

                if (array.Length - index < _dictionary.Count)
                {
                    ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_ArrayPlusOffTooSmall);
                }
                // TODO: we might also use SIMD to pass through the control bytes, which would provide better performance for spare situation.
                foreach (var item in this)
                {
                    array[index++] = item;
                }
            }

            /// <summary>
            /// 获取键集合中包含的键数量。
            /// </summary>
            public int Count => _dictionary.Count;

            bool ICollection<TKey>.IsReadOnly => true;

            void ICollection<TKey>.Add(TKey item) =>
                ThrowHelper.ThrowNotSupportedException(ExceptionResource.NotSupported_KeyCollectionSet);

            void ICollection<TKey>.Clear() =>
                ThrowHelper.ThrowNotSupportedException(ExceptionResource.NotSupported_KeyCollectionSet);

            bool ICollection<TKey>.Contains(TKey item) =>
                _dictionary.ContainsKey(item);

            bool ICollection<TKey>.Remove(TKey item)
            {
                ThrowHelper.ThrowNotSupportedException(ExceptionResource.NotSupported_KeyCollectionSet);
                return false;
            }

            IEnumerator<TKey> IEnumerable<TKey>.GetEnumerator() => new Enumerator(_dictionary);

            IEnumerator IEnumerable.GetEnumerator() => new Enumerator(_dictionary);

            void ICollection.CopyTo(Array array, int index)
            {
                if (array == null)
                {
                    ThrowHelper.ThrowArgumentNullException(ExceptionArgument.array);
                }

                if (array.Rank != 1)
                {
                    ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_RankMultiDimNotSupported);
                }

                if (array.GetLowerBound(0) != 0)
                {
                    ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_NonZeroLowerBound);
                }

                if ((uint)index > (uint)array.Length)
                {
                    ThrowHelper.ThrowIndexArgumentOutOfRange_NeedNonNegNumException();
                }

                if (array.Length - index < _dictionary.Count)
                {
                    ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_ArrayPlusOffTooSmall);
                }

                if (array is TKey[] keys)
                {
                    CopyTo(keys, index);
                }
                else
                {
                    object[]? objects = array as object[];

                    if (objects == null)
                    {
                        ThrowHelper.ThrowArgumentException_Argument_InvalidArrayType();
                    }

                    try
                    {
                        foreach (var item in this)
                        {
                            objects[index++] = item;
                        }
                    }
                    catch (ArrayTypeMismatchException)
                    {
                        ThrowHelper.ThrowArgumentException_Argument_InvalidArrayType();
                    }
                }
            }

            bool ICollection.IsSynchronized => false;

            object ICollection.SyncRoot => ((ICollection)_dictionary).SyncRoot;

            /// <summary>
            /// 遍历 <see cref="KeyCollection"/> 中所有键的枚举器结构。
            /// 通过控制字节位掩码与 SIMD 批量探测加速遍历过程。
            /// </summary>
            public struct Enumerator : IEnumerator<TKey>, IEnumerator
            {
                private readonly SwissTable<TKey, TValue> _dictionary;
                private readonly int _version;
                private readonly int _tolerantVersion;
                private BitMaskUnion _currentBitMask;
                internal int _currentCtrlOffset;
                private bool _isValid; // valid when _current has correct value.
                private TKey? _current;

                internal Enumerator(SwissTable<TKey, TValue> dictionary)
                {
                    _dictionary = dictionary;
                    _version = dictionary._version;
                    _tolerantVersion = dictionary._tolerantVersion;
                    _current = default;
                    _currentCtrlOffset = 0;
                    _isValid = false;
                    _currentBitMask = DispatchGetMatchFullBitMask(_dictionary.rawTable._controls, 0);
                }

                /// <summary>
                /// 释放枚举器占用的资源。本枚举器无托管或非托管资源需要释放，方法体为空。
                /// </summary>
                public void Dispose() { }

                /// <summary>
                /// 将枚举器推进到键集合的下一个元素。
                /// </summary>
                /// <returns>若成功推进到下一个元素则返回 true；若已遍历到末尾则返回 false。</returns>
                public bool MoveNext()
                {
                    ref var entry = ref DispatchMoveNextDictionary(_version, _tolerantVersion, _dictionary, ref _currentCtrlOffset, ref _currentBitMask);
                    if (!Unsafe.IsNullRef(ref entry))
                    {
                        _isValid = true;
                        _current = entry.Key;
                        return true;
                    }
                    else
                    {
                        this._current = default;
                        _isValid = false;
                        return false;
                    }
                }

                /// <summary>
                /// 获取枚举器当前指向的键。
                /// </summary>
                public TKey Current => _current!;

                object? IEnumerator.Current
                {
                    get
                    {
                        if (!_isValid)
                        {
                            ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumOpCantHappen();
                        }

                        return _current;
                    }
                }

                void IEnumerator.Reset()
                {
                    if (_version != _dictionary._version)
                    {
                        ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumFailedVersion();
                    }

                    _current = default;
                    _currentCtrlOffset = 0;
                    _isValid = false;
                    _currentBitMask = DispatchGetMatchFullBitMask(_dictionary.rawTable._controls, 0);
                }
            }
        }

        /// <summary>
        /// 表示 <see cref="SwissTable{TKey, TValue}"/> 中值的集合，可能包含重复元素。
        /// 本集合为关联哈希表的实时视图，对哈希表的修改会立即反映到本集合中。
        /// </summary>
        [DebuggerDisplay("Count = {Count}")]
        public sealed class ValueCollection : ICollection<TValue>, ICollection, IReadOnlyCollection<TValue>
        {
            private readonly SwissTable<TKey, TValue> _dictionary;

            /// <summary>
            /// 初始化 <see cref="ValueCollection"/> 的新实例，作为指定哈希表的值视图。
            /// </summary>
            /// <param name="dictionary">关联的哈希表，不可为 null。</param>
            public ValueCollection(SwissTable<TKey, TValue> dictionary)
            {
                if (dictionary == null)
                {
                    ThrowHelper.ThrowArgumentNullException(ExceptionArgument.dictionary);
                }

                _dictionary = dictionary;
            }

            /// <summary>
            /// 返回一个遍历值集合的 <see cref="Enumerator"/>。
            /// </summary>
            /// <returns>可用于遍历值集合的枚举器。</returns>
            public Enumerator GetEnumerator() => new Enumerator(_dictionary);

            /// <summary>
            /// 从指定索引开始，将值集合的元素复制到目标数组。
            /// </summary>
            /// <param name="array">目标数组，不可为 null。</param>
            /// <param name="index">目标数组中开始写入的零起始索引。</param>
            public void CopyTo(TValue[] array, int index)
            {
                if (array == null)
                {
                    ThrowHelper.ThrowArgumentNullException(ExceptionArgument.array);
                }

                if ((uint)index > array.Length)
                {
                    ThrowHelper.ThrowIndexArgumentOutOfRange_NeedNonNegNumException();
                }

                if (array.Length - index < _dictionary.Count)
                {
                    ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_ArrayPlusOffTooSmall);
                }

                foreach (var item in this)
                {
                    array[index++] = item;
                }
            }

            /// <summary>
            /// 获取值集合中包含的值数量。
            /// </summary>
            public int Count => _dictionary.Count;

            bool ICollection<TValue>.IsReadOnly => true;

            void ICollection<TValue>.Add(TValue item) =>
                ThrowHelper.ThrowNotSupportedException(ExceptionResource.NotSupported_ValueCollectionSet);

            bool ICollection<TValue>.Remove(TValue item)
            {
                ThrowHelper.ThrowNotSupportedException(ExceptionResource.NotSupported_ValueCollectionSet);
                return false;
            }

            void ICollection<TValue>.Clear() =>
                ThrowHelper.ThrowNotSupportedException(ExceptionResource.NotSupported_ValueCollectionSet);

            bool ICollection<TValue>.Contains(TValue item) => _dictionary.ContainsValue(item);

            IEnumerator<TValue> IEnumerable<TValue>.GetEnumerator() => new Enumerator(_dictionary);

            IEnumerator IEnumerable.GetEnumerator() => new Enumerator(_dictionary);

            void ICollection.CopyTo(Array array, int index)
            {
                if (array == null)
                {
                    ThrowHelper.ThrowArgumentNullException(ExceptionArgument.array);
                }

                if (array.Rank != 1)
                {
                    ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_RankMultiDimNotSupported);
                }

                if (array.GetLowerBound(0) != 0)
                {
                    ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_NonZeroLowerBound);
                }

                if ((uint)index > (uint)array.Length)
                {
                    ThrowHelper.ThrowIndexArgumentOutOfRange_NeedNonNegNumException();
                }

                if (array.Length - index < _dictionary.Count)
                {
                    ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_ArrayPlusOffTooSmall);
                }

                if (array is TValue[] values)
                {
                    CopyTo(values, index);
                }
                else
                {
                    object[]? objects = array as object[];

                    if (objects == null)
                    {
                        ThrowHelper.ThrowArgumentException_Argument_InvalidArrayType();
                    }

                    try
                    {
                        foreach (var item in this)
                        {
                            objects[index++] = item!;
                        }
                    }
                    catch (ArrayTypeMismatchException)
                    {
                        ThrowHelper.ThrowArgumentException_Argument_InvalidArrayType();
                    }
                }
            }

            bool ICollection.IsSynchronized => false;

            object ICollection.SyncRoot => ((ICollection)_dictionary).SyncRoot;

            /// <summary>
            /// 遍历 <see cref="ValueCollection"/> 中所有值的枚举器结构。
            /// 通过控制字节位掩码与 SIMD 批量探测加速遍历过程。
            /// </summary>
            public struct Enumerator : IEnumerator<TValue>, IEnumerator
            {
                private readonly SwissTable<TKey, TValue> _dictionary;
                private readonly int _version;
                private readonly int _tolerantVersion;
                private BitMaskUnion _currentBitMask;
                internal int _currentCtrlOffset;
                private bool _isValid; // valid when _current has correct value.
                private TValue? _current;

                internal Enumerator(SwissTable<TKey, TValue> dictionary)
                {
                    _dictionary = dictionary;
                    _version = dictionary._version;
                    _tolerantVersion = dictionary._tolerantVersion;
                    _current = default;
                    _currentCtrlOffset = 0;
                    _isValid = false;
                    _currentBitMask = DispatchGetMatchFullBitMask(_dictionary.rawTable._controls, 0);
                }

                /// <summary>
                /// 释放枚举器占用的资源。本枚举器无托管或非托管资源需要释放，方法体为空。
                /// </summary>
                public void Dispose() { }

                /// <summary>
                /// 将枚举器推进到值集合的下一个元素。
                /// </summary>
                /// <returns>若成功推进到下一个元素则返回 true；若已遍历到末尾则返回 false。</returns>
                public bool MoveNext()
                {
                    ref var entry = ref DispatchMoveNextDictionary(_version, _tolerantVersion, _dictionary, ref _currentCtrlOffset, ref _currentBitMask);
                    if (!Unsafe.IsNullRef(ref entry))
                    {
                        _isValid = true;
                        _current = entry.Value;
                        return true;
                    }
                    else
                    {
                        this._current = default;
                        _isValid = false;
                        return false;
                    }
                }

                /// <summary>
                /// 获取枚举器当前指向的值。
                /// </summary>
                public TValue Current => _current!;

                object? IEnumerator.Current
                {
                    get
                    {
                        if (!_isValid)
                        {
                            ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumOpCantHappen();
                        }

                        return _current;
                    }
                }

                void IEnumerator.Reset()
                {
                    if (_version != _dictionary._version)
                    {
                        ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumFailedVersion();
                    }

                    _current = default;
                    _currentCtrlOffset = 0;
                    _isValid = false;
                    _currentBitMask = DispatchGetMatchFullBitMask(_dictionary.rawTable._controls, 0);
                }
            }
        }
    }
}
