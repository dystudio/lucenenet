/**
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

namespace Lucene.Net.Codecs.Bloom
{

    using System;
    using System.Diagnostics;
    using System.Linq;
    using Store;
    using Util;

    /// <summary>
    /// A class used to represent a set of many, potentially large, values (e.g. many
    /// long strings such as URLs), using a significantly smaller amount of memory.
    ///
    /// The set is "lossy" in that it cannot definitively state that is does contain
    /// a value but it <em>can</em> definitively say if a value is <em>not</em> in
    /// the set. It can therefore be used as a Bloom Filter.
    /// 
    /// Another application of the set is that it can be used to perform fuzzy counting because
    /// it can estimate reasonably accurately how many unique values are contained in the set. 
    ///
    /// This class is NOT threadsafe.
    ///
    /// Internally a Bitset is used to record values and once a client has finished recording
    /// a stream of values the {@link #downsize(float)} method can be used to create a suitably smaller set that
    /// is sized appropriately for the number of values recorded and desired saturation levels. 
    /// 
    /// @lucene.experimental
    /// </summary>
    public class FuzzySet
    {
        public static readonly int VERSION_SPI = 1; // HashFunction used to be loaded through a SPI
        public static readonly int VERSION_START = VERSION_SPI;
        public static readonly int VERSION_CURRENT = 2;

        public static HashFunction HashFunctionForVersion(int version)
        {
            if (version < VERSION_START)
                throw new ArgumentException("Version " + version + " is too old, expected at least " +
                                                   VERSION_START);
            
            if (version > VERSION_CURRENT)
                throw new ArgumentException("Version " + version + " is too new, expected at most " +
                                                   VERSION_CURRENT);
            
            return MurmurHash2.INSTANCE;
        }

        /// <remarks>
        /// Result from {@link FuzzySet#contains(BytesRef)}:
        /// can never return definitively YES (always MAYBE), 
        /// but can sometimes definitely return NO.
        /// </remarks>
        public enum ContainsResult
        {
            Maybe, // LUCENENET TODO: Change to MAYBE, NO
            No
        };

        private readonly HashFunction _hashFunction;
        private readonly FixedBitSet _filter;
        private readonly int _bloomSize;

        //The sizes of BitSet used are all numbers that, when expressed in binary form,
        //are all ones. This is to enable fast downsizing from one bitset to another
        //by simply ANDing each set index in one bitset with the size of the target bitset
        // - this provides a fast modulo of the number. Values previously accumulated in
        // a large bitset and then mapped to a smaller set can be looked up using a single
        // AND operation of the query term's hash rather than needing to perform a 2-step
        // translation of the query term that mirrors the stored content's reprojections.
        private static int[] _usableBitSetSizes;


        static FuzzySet()
        {
            _usableBitSetSizes = new int[30];
            const int mask = 1;
            var size = mask;
            for (var i = 0; i < _usableBitSetSizes.Length; i++)
            {
                size = (size << 1) | mask;
                _usableBitSetSizes[i] = size;
            }
        }

        /// <summary>
        /// Rounds down required maxNumberOfBits to the nearest number that is made up
        /// of all ones as a binary number.  
        /// Use this method where controlling memory use is paramount.
        /// </summary>
        public static int GetNearestSetSize(int maxNumberOfBits)
        {
            var result = _usableBitSetSizes[0];
            foreach (var t in _usableBitSetSizes.Where(t => t <= maxNumberOfBits))
            {
                result = t;
            }
            return result;
        }

        /// <summary>
        /// Use this method to choose a set size where accuracy (low content saturation) is more important
        /// than deciding how much memory to throw at the problem.
        /// </summary>
        /// <param name="maxNumberOfValuesExpected"></param>
        /// <param name="desiredSaturation">A number between 0 and 1 expressing the % of bits set once all values have been recorded</param>
        /// <returns>The size of the set nearest to the required size</returns>
        public static int GetNearestSetSize(int maxNumberOfValuesExpected,
            float desiredSaturation)
        {
            // Iterate around the various scales of bitset from smallest to largest looking for the first that
            // satisfies value volumes at the chosen saturation level
            foreach (var t in from t in _usableBitSetSizes
                              let numSetBitsAtDesiredSaturation = (int) (t*desiredSaturation)
                              let estimatedNumUniqueValues = GetEstimatedNumberUniqueValuesAllowingForCollisions(
                t, numSetBitsAtDesiredSaturation) where estimatedNumUniqueValues > maxNumberOfValuesExpected select t)
            {
                return t;
            }
            return -1;
        }

        public static FuzzySet CreateSetBasedOnMaxMemory(int maxNumBytes)
        {
            var setSize = GetNearestSetSize(maxNumBytes);
            return new FuzzySet(new FixedBitSet(setSize + 1), setSize, HashFunctionForVersion(VERSION_CURRENT));
        }

        public static FuzzySet CreateSetBasedOnQuality(int maxNumUniqueValues, float desiredMaxSaturation)
        {
            var setSize = GetNearestSetSize(maxNumUniqueValues, desiredMaxSaturation);
            return new FuzzySet(new FixedBitSet(setSize + 1), setSize, HashFunctionForVersion(VERSION_CURRENT));
        }

        private FuzzySet(FixedBitSet filter, int bloomSize, HashFunction hashFunction)
        {
            _filter = filter;
            _bloomSize = bloomSize;
            _hashFunction = hashFunction;
        }

        /// <summary>
        /// The main method required for a Bloom filter which, given a value determines set membership.
        /// Unlike a conventional set, the fuzzy set returns NO or MAYBE rather than true or false.
        /// </summary>
        /// <returns>NO or MAYBE</returns>
        public virtual ContainsResult Contains(BytesRef value)
        {
            var hash = _hashFunction.Hash(value);
            if (hash < 0)
            {
                hash = hash*-1;
            }
            return MayContainValue(hash);
        }

        /// <summary>
        ///  Serializes the data set to file using the following format:
        ///  <ul>
        ///   <li>FuzzySet --&gt;FuzzySetVersion,HashFunctionName,BloomSize,
        ///  NumBitSetWords,BitSetWord<sup>NumBitSetWords</sup></li> 
        ///  <li>HashFunctionName --&gt; {@link DataOutput#writeString(String) String} The
        ///  name of a ServiceProvider registered {@link HashFunction}</li>
        ///  <li>FuzzySetVersion --&gt; {@link DataOutput#writeInt Uint32} The version number of the {@link FuzzySet} class</li>
        ///  <li>BloomSize --&gt; {@link DataOutput#writeInt Uint32} The modulo value used
        ///  to project hashes into the field's Bitset</li>
        ///  <li>NumBitSetWords --&gt; {@link DataOutput#writeInt Uint32} The number of
        ///  longs (as returned from {@link FixedBitSet#getBits})</li>
        ///  <li>BitSetWord --&gt; {@link DataOutput#writeLong Long} A long from the array
        ///  returned by {@link FixedBitSet#getBits}</li>
        ///  </ul>
        ///  @param out Data output stream
        ///  @ If there is a low-level I/O error
        /// </summary>
        public virtual void Serialize(DataOutput output)
        {
            output.WriteInt(VERSION_CURRENT);
            output.WriteInt(_bloomSize);
            var bits = _filter.GetBits();
            output.WriteInt(bits.Length);
            foreach (var t in bits)
            {
                // Can't used VLong encoding because cant cope with negative numbers
                // output by FixedBitSet
                output.WriteLong(t);
            }
        }

        public static FuzzySet Deserialize(DataInput input)
        {
            var version = input.ReadInt();
            if (version == VERSION_SPI)
                input.ReadString();
           
            var hashFunction = HashFunctionForVersion(version);
            var bloomSize = input.ReadInt();
            var numLongs = input.ReadInt();
            var longs = new long[numLongs];
            for (var i = 0; i < numLongs; i++)
            {
                longs[i] = input.ReadLong();
            }
            var bits = new FixedBitSet(longs, bloomSize + 1);
            return new FuzzySet(bits, bloomSize, hashFunction);
        }

        private ContainsResult MayContainValue(int positiveHash)
        {
            Debug.Assert((positiveHash >= 0));

            // Bloom sizes are always base 2 and so can be ANDed for a fast modulo
            var pos = positiveHash & _bloomSize;
            return _filter.Get(pos) ? ContainsResult.Maybe : ContainsResult.No;
        }

        /// <summary>
        /// Records a value in the set. The referenced bytes are hashed and then modulo n'd where n is the
        /// chosen size of the internal bitset.
        /// </summary>
        /// <param name="value">The Key value to be hashed</param>
        public virtual void AddValue(BytesRef value)
        {
            var hash = _hashFunction.Hash(value);
            if (hash < 0)
            {
                hash = hash*-1;
            }
            // Bitmasking using bloomSize is effectively a modulo operation.
            var bloomPos = hash & _bloomSize;
            _filter.Set(bloomPos);
        }

        /// <param name="targetMaxSaturation">
        /// A number between 0 and 1 describing the % of bits that would ideally be set in the result. 
        /// Lower values have better accuracy but require more space.
        /// </param>
        /// <return>A smaller FuzzySet or null if the current set is already over-saturated</return>
        public virtual FuzzySet Downsize(float targetMaxSaturation)
        {
            var numBitsSet = _filter.Cardinality();
            FixedBitSet rightSizedBitSet;
            var rightSizedBitSetSize = _bloomSize;
            //Hopefully find a smaller size bitset into which we can project accumulated values while maintaining desired saturation level
            foreach (var candidateBitsetSize in from candidateBitsetSize in _usableBitSetSizes
                                                let candidateSaturation = numBitsSet /(float) candidateBitsetSize
                                                where candidateSaturation <= targetMaxSaturation
                                                select candidateBitsetSize)
            {
                rightSizedBitSetSize = candidateBitsetSize;
                break;
            }
            // Re-project the numbers to a smaller space if necessary
            if (rightSizedBitSetSize < _bloomSize)
            {
                // Reset the choice of bitset to the smaller version
                rightSizedBitSet = new FixedBitSet(rightSizedBitSetSize + 1);
                // Map across the bits from the large set to the smaller one
                var bitIndex = 0;
                do
                {
                    bitIndex = _filter.NextSetBit(bitIndex);
                    if (bitIndex < 0) continue;

                    // Project the larger number into a smaller one effectively
                    // modulo-ing by using the target bitset size as a mask
                    var downSizedBitIndex = bitIndex & rightSizedBitSetSize;
                    rightSizedBitSet.Set(downSizedBitIndex);
                    bitIndex++;
                } while ((bitIndex >= 0) && (bitIndex <= _bloomSize));
            }
            else
            {
                return null;
            }
            return new FuzzySet(rightSizedBitSet, rightSizedBitSetSize, _hashFunction);
        }

        public virtual int GetEstimatedUniqueValues()
        {
            return GetEstimatedNumberUniqueValuesAllowingForCollisions(_bloomSize, _filter.Cardinality());
        }

        // Given a set size and a the number of set bits, produces an estimate of the number of unique values recorded
        public static int GetEstimatedNumberUniqueValuesAllowingForCollisions(
            int setSize, int numRecordedBits)
        {
            double setSizeAsDouble = setSize;
            double numRecordedBitsAsDouble = numRecordedBits;
            var saturation = numRecordedBitsAsDouble/setSizeAsDouble;
            var logInverseSaturation = Math.Log(1 - saturation)*-1;
            return (int) (setSizeAsDouble*logInverseSaturation);
        }

        public virtual float GetSaturation()
        {
            var numBitsSet = _filter.Cardinality();
            return numBitsSet/(float) _bloomSize;
        }

        public virtual long RamBytesUsed()
        {
            return RamUsageEstimator.SizeOf(_filter.GetBits());
        }
    }
}
