using System;
using System.Diagnostics;

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
    /// Provides support for converting byte sequences to Strings and back again.
    /// The resulting Strings preserve the original byte sequences' sort order.
    /// <p/>
    /// The Strings are constructed using a Base 8000h encoding of the original
    /// binary data - each char of an encoded String represents a 15-bit chunk
    /// from the byte sequence.  Base 8000h was chosen because it allows for all
    /// lower 15 bits of char to be used without restriction; the surrogate range
    /// [U+D8000-U+DFFF] does not represent valid chars, and would require
    /// complicated handling to avoid them and allow use of char's high bit.
    /// <p/>
    /// Although unset bits are used as padding in the final char, the original
    /// byte sequence could contain trailing bytes with no set bits (null bytes):
    /// padding is indistinguishable from valid information.  To overcome this
    /// problem, a char is appended, indicating the number of encoded bytes in the
    /// final content char.
    /// <p/>
    ///
    /// @lucene.experimental </summary>
    /// @deprecated Implement <seealso cref="ITermToBytesRefAttribute"/> and store bytes directly
    /// instead. this class will be removed in Lucene 5.0
    [Obsolete("Implement ITermToBytesRefAttribute and store bytes directly")]
    public sealed class IndexableBinaryStringTools
    {
        private static readonly CodingCase[] CODING_CASES = new CodingCase[] {
            // CodingCase(int initialShift, int finalShift)
            new CodingCase(7, 1),
            // CodingCase(int initialShift, int middleShift, int finalShift)
            new CodingCase(14, 6, 2),
            new CodingCase(13, 5, 3),
            new CodingCase(12, 4, 4),
            new CodingCase(11, 3, 5),
            new CodingCase(10, 2, 6),
            new CodingCase(9, 1, 7),
            new CodingCase(8, 0)
        };

        // Export only static methods
        private IndexableBinaryStringTools()
        {
        }

        /// <summary>
        /// Returns the number of chars required to encode the given bytes.
        /// </summary>
        /// <param name="inputArray"> byte sequence to be encoded </param>
        /// <param name="inputOffset"> initial offset into inputArray </param>
        /// <param name="inputLength"> number of bytes in inputArray </param>
        /// <returns> The number of chars required to encode the number of bytes. </returns>
        // LUCENENET specific overload for CLS compliance
        public static int GetEncodedLength(byte[] inputArray, int inputOffset, int inputLength)
        {
            // Use long for intermediaries to protect against overflow
            return (int)((8L * inputLength + 14L) / 15L) + 1;
        }

        /// <summary>
        /// Returns the number of chars required to encode the given sbytes.
        /// </summary>
        /// <param name="inputArray"> sbyte sequence to be encoded </param>
        /// <param name="inputOffset"> initial offset into inputArray </param>
        /// <param name="inputLength"> number of sbytes in inputArray </param>
        /// <returns> The number of chars required to encode the number of sbytes. </returns>
        [CLSCompliant(false)]
        public static int GetEncodedLength(sbyte[] inputArray, int inputOffset, int inputLength)
        {
            // Use long for intermediaries to protect against overflow
            return (int)((8L * inputLength + 14L) / 15L) + 1;
        }

        /// <summary>
        /// Returns the number of bytes required to decode the given char sequence.
        /// </summary>
        /// <param name="encoded"> char sequence to be decoded </param>
        /// <param name="offset"> initial offset </param>
        /// <param name="length"> number of characters </param>
        /// <returns> The number of bytes required to decode the given char sequence </returns>
        public static int GetDecodedLength(char[] encoded, int offset, int length)
        {
            int numChars = length - 1;
            if (numChars <= 0)
            {
                return 0;
            }
            else
            {
                // Use long for intermediaries to protect against overflow
                long numFullBytesInFinalChar = encoded[offset + length - 1];
                long numEncodedChars = numChars - 1;
                return (int)((numEncodedChars * 15L + 7L) / 8L + numFullBytesInFinalChar);
            }
        }

        /// <summary>
        /// Encodes the input sbyte sequence into the output char sequence.  Before
        /// calling this method, ensure that the output array has sufficient
        /// capacity by calling <seealso cref="#getEncodedLength(byte[], int, int)"/>.
        /// </summary>
        /// <param name="inputArray"> sbyte sequence to be encoded </param>
        /// <param name="inputOffset"> initial offset into inputArray </param>
        /// <param name="inputLength"> number of bytes in inputArray </param>
        /// <param name="outputArray"> char sequence to store encoded result </param>
        /// <param name="outputOffset"> initial offset into outputArray </param>
        /// <param name="outputLength"> length of output, must be getEncodedLength </param>
        // LUCENENET specific overload for CLS compliance
        public static void Encode(byte[] inputArray, int inputOffset, int inputLength, char[] outputArray, int outputOffset, int outputLength)
        {
            Encode((sbyte[])(Array)inputArray, inputOffset, inputLength, outputArray, outputOffset, outputLength);
        }

        /// <summary>
        /// Encodes the input sbyte sequence into the output char sequence.  Before
        /// calling this method, ensure that the output array has sufficient
        /// capacity by calling <seealso cref="#getEncodedLength(byte[], int, int)"/>.
        /// </summary>
        /// <param name="inputArray"> sbyte sequence to be encoded </param>
        /// <param name="inputOffset"> initial offset into inputArray </param>
        /// <param name="inputLength"> number of bytes in inputArray </param>
        /// <param name="outputArray"> char sequence to store encoded result </param>
        /// <param name="outputOffset"> initial offset into outputArray </param>
        /// <param name="outputLength"> length of output, must be getEncodedLength </param>
        [CLSCompliant(false)]
        public static void Encode(sbyte[] inputArray, int inputOffset, int inputLength, char[] outputArray, int outputOffset, int outputLength)
        {
            Debug.Assert(outputLength == GetEncodedLength(inputArray, inputOffset, inputLength));
            if (inputLength > 0)
            {
                int inputByteNum = inputOffset;
                int caseNum = 0;
                int outputCharNum = outputOffset;
                CodingCase codingCase;
                for (; inputByteNum + CODING_CASES[caseNum].numBytes <= inputLength; ++outputCharNum)
                {
                    codingCase = CODING_CASES[caseNum];
                    if (2 == codingCase.numBytes)
                    {
                        outputArray[outputCharNum] = (char)(((inputArray[inputByteNum] & 0xFF) << codingCase.initialShift) + (((int)((uint)(inputArray[inputByteNum + 1] & 0xFF) >> codingCase.finalShift)) & codingCase.finalMask) & (short)0x7FFF);
                    } // numBytes is 3
                    else
                    {
                        outputArray[outputCharNum] = (char)(((inputArray[inputByteNum] & 0xFF) << codingCase.initialShift) + ((inputArray[inputByteNum + 1] & 0xFF) << codingCase.middleShift) + (((int)((uint)(inputArray[inputByteNum + 2] & 0xFF) >> codingCase.finalShift)) & codingCase.finalMask) & (short)0x7FFF);
                    }
                    inputByteNum += codingCase.advanceBytes;
                    if (++caseNum == CODING_CASES.Length)
                    {
                        caseNum = 0;
                    }
                }
                // Produce final char (if any) and trailing count chars.
                codingCase = CODING_CASES[caseNum];

                if (inputByteNum + 1 < inputLength) // codingCase.numBytes must be 3
                {
                    outputArray[outputCharNum++] = (char)((((inputArray[inputByteNum] & 0xFF) << codingCase.initialShift) + ((inputArray[inputByteNum + 1] & 0xFF) << codingCase.middleShift)) & (short)0x7FFF);
                    // Add trailing char containing the number of full bytes in final char
                    outputArray[outputCharNum++] = (char)1;
                }
                else if (inputByteNum < inputLength)
                {
                    outputArray[outputCharNum++] = (char)(((inputArray[inputByteNum] & 0xFF) << codingCase.initialShift) & (short)0x7FFF);
                    // Add trailing char containing the number of full bytes in final char
                    outputArray[outputCharNum++] = caseNum == 0 ? (char)1 : (char)0;
                } // No left over bits - last char is completely filled.
                else
                {
                    // Add trailing char containing the number of full bytes in final char
                    outputArray[outputCharNum++] = (char)1;
                }
            }
        }

        /// <summary>
        /// Decodes the input char sequence into the output byte sequence. Before
        /// calling this method, ensure that the output array has sufficient capacity
        /// by calling <seealso cref="#getDecodedLength(char[], int, int)"/>.
        /// </summary>
        /// <param name="inputArray"> char sequence to be decoded </param>
        /// <param name="inputOffset"> initial offset into inputArray </param>
        /// <param name="inputLength"> number of chars in inputArray </param>
        /// <param name="outputArray"> byte sequence to store encoded result </param>
        /// <param name="outputOffset"> initial offset into outputArray </param>
        /// <param name="outputLength"> length of output, must be
        ///        getDecodedLength(inputArray, inputOffset, inputLength) </param>
        // LUCENENET specific overload for CLS compliance
        public static void Decode(char[] inputArray, int inputOffset, int inputLength, byte[] outputArray, int outputOffset, int outputLength)
        {
            Decode(inputArray, inputOffset, inputLength, (sbyte[])(Array)outputArray, outputOffset, outputLength);
        }

        /// <summary>
        /// Decodes the input char sequence into the output sbyte sequence. Before
        /// calling this method, ensure that the output array has sufficient capacity
        /// by calling <seealso cref="#getDecodedLength(char[], int, int)"/>.
        /// </summary>
        /// <param name="inputArray"> char sequence to be decoded </param>
        /// <param name="inputOffset"> initial offset into inputArray </param>
        /// <param name="inputLength"> number of chars in inputArray </param>
        /// <param name="outputArray"> byte sequence to store encoded result </param>
        /// <param name="outputOffset"> initial offset into outputArray </param>
        /// <param name="outputLength"> length of output, must be
        ///        getDecodedLength(inputArray, inputOffset, inputLength) </param>
        [CLSCompliant(false)]
        public static void Decode(char[] inputArray, int inputOffset, int inputLength, sbyte[] outputArray, int outputOffset, int outputLength)
        {
            Debug.Assert(outputLength == GetDecodedLength(inputArray, inputOffset, inputLength));
            int numInputChars = inputLength - 1;
            int numOutputBytes = outputLength;

            if (numOutputBytes > 0)
            {
                int caseNum = 0;
                int outputByteNum = outputOffset;
                int inputCharNum = inputOffset;
                short inputChar;
                CodingCase codingCase;
                for (; inputCharNum < numInputChars - 1; ++inputCharNum)
                {
                    codingCase = CODING_CASES[caseNum];
                    inputChar = (short)inputArray[inputCharNum];
                    if (2 == codingCase.numBytes)
                    {
                        if (0 == caseNum)
                        {
                            outputArray[outputByteNum] = (sbyte)((short)((ushort)inputChar >> codingCase.initialShift));
                        }
                        else
                        {
                            outputArray[outputByteNum] += (sbyte)((short)((ushort)inputChar >> codingCase.initialShift));
                        }
                        outputArray[outputByteNum + 1] = (sbyte)((inputChar & codingCase.finalMask) << codingCase.finalShift);
                    } // numBytes is 3
                    else
                    {
                        outputArray[outputByteNum] += (sbyte)((short)((ushort)inputChar >> codingCase.initialShift));
                        outputArray[outputByteNum + 1] = (sbyte)((int)((uint)(inputChar & codingCase.middleMask) >> codingCase.middleShift));
                        outputArray[outputByteNum + 2] = (sbyte)((inputChar & codingCase.finalMask) << codingCase.finalShift);
                    }
                    outputByteNum += codingCase.advanceBytes;
                    if (++caseNum == CODING_CASES.Length)
                    {
                        caseNum = 0;
                    }
                }
                // Handle final char
                inputChar = (short)inputArray[inputCharNum];
                codingCase = CODING_CASES[caseNum];
                if (0 == caseNum)
                {
                    outputArray[outputByteNum] = 0;
                }
                outputArray[outputByteNum] += (sbyte)((short)((ushort)inputChar >> codingCase.initialShift));
                int bytesLeft = numOutputBytes - outputByteNum;
                if (bytesLeft > 1)
                {
                    if (2 == codingCase.numBytes)
                    {
                        outputArray[outputByteNum + 1] = (sbyte)((int)((uint)(inputChar & codingCase.finalMask) >> codingCase.finalShift));
                    } // numBytes is 3
                    else
                    {
                        outputArray[outputByteNum + 1] = (sbyte)((int)((uint)(inputChar & codingCase.middleMask) >> codingCase.middleShift));
                        if (bytesLeft > 2)
                        {
                            outputArray[outputByteNum + 2] = (sbyte)((inputChar & codingCase.finalMask) << codingCase.finalShift);
                        }
                    }
                }
            }
        }

        internal class CodingCase
        {
            internal int numBytes, initialShift, middleShift, finalShift, advanceBytes = 2;
            internal short middleMask, finalMask;

            internal CodingCase(int initialShift, int middleShift, int finalShift)
            {
                this.numBytes = 3;
                this.initialShift = initialShift;
                this.middleShift = middleShift;
                this.finalShift = finalShift;
                this.finalMask = (short)((int)((uint)(short)0xFF >> finalShift));
                this.middleMask = (short)((short)0xFF << middleShift);
            }

            internal CodingCase(int initialShift, int finalShift)
            {
                this.numBytes = 2;
                this.initialShift = initialShift;
                this.finalShift = finalShift;
                this.finalMask = (short)((int)((uint)(short)0xFF >> finalShift));
                if (finalShift != 0)
                {
                    advanceBytes = 1;
                }
            }
        }
    }
}