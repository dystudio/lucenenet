using Lucene.Net.Support;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Lucene.Net.Util
{
    /*
     * Licensed to the Apache Software Foundation (ASF) under one or more
     * contributor license agreements.  See the NOTICE file distributed with
     * this work for additional information regarding copyright ownership.
     * The ASF licenses this file to You under the Apache License, Version 2.0
     * (the "License"); you may not use this file except in compliance with
     * the License.  You may obtain a copy of the License at
     *
     *     http://www.apache.org/licenses/LICENSE-2.0
     *
     * Unless required by applicable law or agreed to in writing, software
     * distributed under the License is distributed on an "AS IS" BASIS,
     * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
     * See the License for the specific language governing permissions and
     * limitations under the License.
     */

    /// <summary>
    /// A pool for int blocks similar to <seealso cref="ByteBlockPool"/>.
    /// <para/>
    /// NOTE: This was IntBlockPool in Lucene
    /// @lucene.internal
    /// </summary>
    public sealed class Int32BlockPool
    {
        /// <summary>
        /// NOTE: This was INT_BLOCK_SHIFT in Lucene
        /// </summary>
        public static readonly int INT32_BLOCK_SHIFT = 13;

        /// <summary>
        /// NOTE: This was INT_BLOCK_SIZE in Lucene
        /// </summary>
        public static readonly int INT32_BLOCK_SIZE = 1 << INT32_BLOCK_SHIFT;

        /// <summary>
        /// NOTE: This was INT_BLOCK_MASK in Lucene
        /// </summary>
        public static readonly int INT32_BLOCK_MASK = INT32_BLOCK_SIZE - 1;

        /// <summary>
        /// Abstract class for allocating and freeing int
        /// blocks.
        /// </summary>
        public abstract class Allocator
        {
            protected readonly int m_blockSize;

            public Allocator(int blockSize)
            {
                this.m_blockSize = blockSize;
            }

            /// <summary>
            /// NOTE: This was recycleIntBlocks() in Lucene
            /// </summary>
            public abstract void RecycleInt32Blocks(int[][] blocks, int start, int end);

            /// <summary>
            /// NOTE: This was getIntBlock() in Lucene
            /// </summary>
            public virtual int[] GetInt32Block()
            {
                return new int[m_blockSize];
            }
        }

        /// <summary>
        /// A simple <seealso cref="Allocator"/> that never recycles. </summary>
        public sealed class DirectAllocator : Allocator
        {
            /// <summary>
            /// Creates a new <seealso cref="DirectAllocator"/> with a default block size
            /// </summary>
            public DirectAllocator()
                : base(INT32_BLOCK_SIZE)
            {
            }

            /// <summary>
            /// NOTE: This was recycleIntBlocks() in Lucene
            /// </summary>
            public override void RecycleInt32Blocks(int[][] blocks, int start, int end)
            {
            }
        }

        /// <summary>
        /// array of buffers currently used in the pool. Buffers are allocated if needed don't modify this outside of this class </summary>
        [WritableArray]
        [SuppressMessage("Microsoft.Performance", "CA1819", Justification = "Lucene's design requires some writable array properties")]
        public int[][] Buffers
        {
            get { return buffers; }
            set { buffers = value; }
        }
        private int[][] buffers = new int[10][];

        /// <summary>
        /// index into the buffers array pointing to the current buffer used as the head </summary>
        private int bufferUpto = -1;

        /// <summary>
        /// Pointer to the current position in head buffer
        /// <para/>
        /// NOTE: This was intUpto in Lucene
        /// </summary>
        public int Int32Upto { get; set; }

        /// <summary>
        /// Current head buffer </summary>
        [WritableArray]
        [SuppressMessage("Microsoft.Performance", "CA1819", Justification = "Lucene's design requires some writable array properties")]
        public int[] Buffer
        {
            get { return buffer; }
            set { buffer = value; }
        }
        private int[] buffer;

        /// <summary>
        /// Current head offset 
        /// <para/>
        /// NOTE: This was intOffset in Lucene
        /// </summary>
        public int Int32Offset { get; set; }

        private readonly Allocator allocator;

        /// <summary>
        /// Creates a new <seealso cref="Int32BlockPool"/> with a default <seealso cref="Allocator"/>. </summary>
        /// <seealso cref= Int32BlockPool#nextBuffer() </seealso>
        public Int32BlockPool()
            : this(new DirectAllocator())
        {
        }

        /// <summary>
        /// Creates a new <seealso cref="Int32BlockPool"/> with the given <seealso cref="Allocator"/>. </summary>
        /// <seealso cref= Int32BlockPool#nextBuffer() </seealso>
        public Int32BlockPool(Allocator allocator)
        {
            // set defaults
            Int32Upto = INT32_BLOCK_SIZE;
            Int32Offset = -INT32_BLOCK_SIZE;

            this.allocator = allocator;
        }

        /// <summary>
        /// Resets the pool to its initial state reusing the first buffer. Calling
        /// <seealso cref="Int32BlockPool#nextBuffer()"/> is not needed after reset.
        /// </summary>
        public void Reset()
        {
            this.Reset(true, true);
        }

        /// <summary>
        /// Expert: Resets the pool to its initial state reusing the first buffer. </summary>
        /// <param name="zeroFillBuffers"> if <code>true</code> the buffers are filled with <tt>0</tt>.
        ///        this should be set to <code>true</code> if this pool is used with
        ///        <seealso cref="SliceWriter"/>. </param>
        /// <param name="reuseFirst"> if <code>true</code> the first buffer will be reused and calling
        ///        <seealso cref="Int32BlockPool#nextBuffer()"/> is not needed after reset iff the
        ///        block pool was used before ie. <seealso cref="Int32BlockPool#nextBuffer()"/> was called before. </param>
        public void Reset(bool zeroFillBuffers, bool reuseFirst)
        {
            if (bufferUpto != -1)
            {
                // We allocated at least one buffer

                if (zeroFillBuffers)
                {
                    for (int i = 0; i < bufferUpto; i++)
                    {
                        // Fully zero fill buffers that we fully used
                        Arrays.Fill(buffers[i], 0);
                    }
                    // Partial zero fill the final buffer
                    Arrays.Fill(buffers[bufferUpto], 0, Int32Upto, 0);
                }

                if (bufferUpto > 0 || !reuseFirst)
                {
                    int offset = reuseFirst ? 1 : 0;
                    // Recycle all but the first buffer
                    allocator.RecycleInt32Blocks(buffers, offset, 1 + bufferUpto);
                    Arrays.Fill(buffers, offset, bufferUpto + 1, null);
                }
                if (reuseFirst)
                {
                    // Re-use the first buffer
                    bufferUpto = 0;
                    Int32Upto = 0;
                    Int32Offset = 0;
                    buffer = buffers[0];
                }
                else
                {
                    bufferUpto = -1;
                    Int32Upto = INT32_BLOCK_SIZE;
                    Int32Offset = -INT32_BLOCK_SIZE;
                    buffer = null;
                }
            }
        }

        /// <summary>
        /// Advances the pool to its next buffer. this method should be called once
        /// after the constructor to initialize the pool. In contrast to the
        /// constructor a <seealso cref="Int32BlockPool#reset()"/> call will advance the pool to
        /// its first buffer immediately.
        /// </summary>
        public void NextBuffer()
        {
            if (1 + bufferUpto == buffers.Length)
            {
                int[][] newBuffers = new int[(int)(buffers.Length * 1.5)][];
                Array.Copy(buffers, 0, newBuffers, 0, buffers.Length);
                buffers = newBuffers;
            }
            buffer = buffers[1 + bufferUpto] = allocator.GetInt32Block();
            bufferUpto++;

            Int32Upto = 0;
            Int32Offset += INT32_BLOCK_SIZE;
        }

        /// <summary>
        /// Creates a new int slice with the given starting size and returns the slices offset in the pool. </summary>
        /// <seealso cref= SliceReader </seealso>
        private int NewSlice(int size)
        {
            if (Int32Upto > INT32_BLOCK_SIZE - size)
            {
                NextBuffer();
                Debug.Assert(AssertSliceBuffer(buffer));
            }

            int upto = Int32Upto;
            Int32Upto += size;
            buffer[Int32Upto - 1] = 1;
            return upto;
        }

        private static bool AssertSliceBuffer(int[] buffer)
        {
            int count = 0;
            for (int i = 0; i < buffer.Length; i++)
            {
                count += buffer[i]; // for slices the buffer must only have 0 values
            }
            return count == 0;
        }

        // no need to make this public unless we support different sizes
        // TODO make the levels and the sizes configurable
        /// <summary>
        /// An array holding the offset into the <seealso cref="Int32BlockPool#LEVEL_SIZE_ARRAY"/>
        /// to quickly navigate to the next slice level.
        /// </summary>
        private static readonly int[] NEXT_LEVEL_ARRAY = new int[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 9 };

        /// <summary>
        /// An array holding the level sizes for int slices.
        /// </summary>
        private static readonly int[] LEVEL_SIZE_ARRAY = new int[] { 2, 4, 8, 16, 32, 64, 128, 256, 512, 1024 };

        /// <summary>
        /// The first level size for new slices
        /// </summary>
        private static readonly int FIRST_LEVEL_SIZE = LEVEL_SIZE_ARRAY[0];

        /// <summary>
        /// Allocates a new slice from the given offset
        /// </summary>
        private int AllocSlice(int[] slice, int sliceOffset)
        {
            int level = slice[sliceOffset];
            int newLevel = NEXT_LEVEL_ARRAY[level - 1];
            int newSize = LEVEL_SIZE_ARRAY[newLevel];
            // Maybe allocate another block
            if (Int32Upto > INT32_BLOCK_SIZE - newSize)
            {
                NextBuffer();
                Debug.Assert(AssertSliceBuffer(buffer));
            }

            int newUpto = Int32Upto;
            int offset = newUpto + Int32Offset;
            Int32Upto += newSize;
            // Write forwarding address at end of last slice:
            slice[sliceOffset] = offset;

            // Write new level:
            buffer[Int32Upto - 1] = newLevel;

            return newUpto;
        }

        /// <summary>
        /// A <seealso cref="SliceWriter"/> that allows to write multiple integer slices into a given <seealso cref="Int32BlockPool"/>.
        /// </summary>
        ///  <seealso cref= SliceReader
        ///  @lucene.internal </seealso>
        public class SliceWriter
        {
            private int offset;
            private readonly Int32BlockPool pool;

            public SliceWriter(Int32BlockPool pool)
            {
                this.pool = pool;
            }

            ///
            public virtual void Reset(int sliceOffset)
            {
                this.offset = sliceOffset;
            }

            /// <summary>
            /// Writes the given value into the slice and resizes the slice if needed
            /// <para/>
            /// NOTE: This was writeInt() in Lucene
            /// </summary>
            public virtual void WriteInt32(int value)
            {
                int[] ints = pool.buffers[offset >> INT32_BLOCK_SHIFT];
                Debug.Assert(ints != null);
                int relativeOffset = offset & INT32_BLOCK_MASK;
                if (ints[relativeOffset] != 0)
                {
                    // End of slice; allocate a new one
                    relativeOffset = pool.AllocSlice(ints, relativeOffset);
                    ints = pool.buffer;
                    offset = relativeOffset + pool.Int32Offset;
                }
                ints[relativeOffset] = value;
                offset++;
            }

            /// <summary>
            /// starts a new slice and returns the start offset. The returned value
            /// should be used as the start offset to initialize a <seealso cref="SliceReader"/>.
            /// </summary>
            public virtual int StartNewSlice()
            {
                return offset = pool.NewSlice(FIRST_LEVEL_SIZE) + pool.Int32Offset;
            }

            /// <summary>
            /// Returns the offset of the currently written slice. The returned value
            /// should be used as the end offset to initialize a <seealso cref="SliceReader"/> once
            /// this slice is fully written or to reset the this writer if another slice
            /// needs to be written.
            /// </summary>
            public virtual int CurrentOffset
            {
                get
                {
                    return offset;
                }
            }
        }

        /// <summary>
        /// A <seealso cref="SliceReader"/> that can read int slices written by a <seealso cref="SliceWriter"/>
        /// @lucene.internal
        /// </summary>
        public sealed class SliceReader
        {
            private readonly Int32BlockPool pool;
            private int upto;
            private int bufferUpto;
            private int bufferOffset;
            private int[] buffer;
            private int limit;
            private int level;
            private int end;

            /// <summary>
            /// Creates a new <seealso cref="SliceReader"/> on the given pool
            /// </summary>
            public SliceReader(Int32BlockPool pool)
            {
                this.pool = pool;
            }

            /// <summary>
            /// Resets the reader to a slice give the slices absolute start and end offset in the pool
            /// </summary>
            public void Reset(int startOffset, int endOffset)
            {
                bufferUpto = startOffset / INT32_BLOCK_SIZE;
                bufferOffset = bufferUpto * INT32_BLOCK_SIZE;
                this.end = endOffset;
                upto = startOffset;
                level = 1;

                buffer = pool.buffers[bufferUpto];
                upto = startOffset & INT32_BLOCK_MASK;

                int firstSize = Int32BlockPool.LEVEL_SIZE_ARRAY[0];
                if (startOffset + firstSize >= endOffset)
                {
                    // There is only this one slice to read
                    limit = endOffset & INT32_BLOCK_MASK;
                }
                else
                {
                    limit = upto + firstSize - 1;
                }
            }

            /// <summary>
            /// Returns <code>true</code> iff the current slice is fully read. If this
            /// method returns <code>true</code> <seealso cref="SliceReader#readInt()"/> should not
            /// be called again on this slice.
            /// </summary>
            public bool IsEndOfSlice
            {
                get
                {
                    Debug.Assert(upto + bufferOffset <= end);
                    return upto + bufferOffset == end;
                }
            }

            /// <summary>
            /// Reads the next int from the current slice and returns it. 
            /// <para/>
            /// NOTE: This was readInt() in Lucene
            /// </summary>
            /// <seealso cref= SliceReader#endOfSlice() </seealso>
            public int ReadInt32()
            {
                Debug.Assert(!IsEndOfSlice);
                Debug.Assert(upto <= limit);
                if (upto == limit)
                {
                    NextSlice();
                }
                return buffer[upto++];
            }

            private void NextSlice()
            {
                // Skip to our next slice
                int nextIndex = buffer[limit];
                level = NEXT_LEVEL_ARRAY[level - 1];
                int newSize = LEVEL_SIZE_ARRAY[level];

                bufferUpto = nextIndex / INT32_BLOCK_SIZE;
                bufferOffset = bufferUpto * INT32_BLOCK_SIZE;

                buffer = pool.Buffers[bufferUpto];
                upto = nextIndex & INT32_BLOCK_MASK;

                if (nextIndex + newSize >= end)
                {
                    // We are advancing to the final slice
                    Debug.Assert(end - nextIndex > 0);
                    limit = end - bufferOffset;
                }
                else
                {
                    // this is not the final slice (subtract 4 for the
                    // forwarding address at the end of this new slice)
                    limit = upto + newSize - 1;
                }
            }
        }
    }
}