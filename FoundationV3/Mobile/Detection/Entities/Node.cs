﻿/* *********************************************************************
 * This Source Code Form is copyright of 51Degrees Mobile Experts Limited. 
 * Copyright © 2014 51Degrees Mobile Experts Limited, 5 Charlotte Close,
 * Caversham, Reading, Berkshire, United Kingdom RG4 7BY
 * 
 * This Source Code Form is the subject of the following patent 
 * applications, owned by 51Degrees Mobile Experts Limited of 5 Charlotte
 * Close, Caversham, Reading, Berkshire, United Kingdom RG4 7BY: 
 * European Patent Application No. 13192291.6; and
 * United States Patent Application Nos. 14/085,223 and 14/085,301.
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0.
 * 
 * If a copy of the MPL was not distributed with this file, You can obtain
 * one at http://mozilla.org/MPL/2.0/.
 * 
 * This Source Code Form is “Incompatible With Secondary Licenses”, as
 * defined by the Mozilla Public License, v. 2.0.
 * ********************************************************************* */

using System;
using System.Linq;
using System.Text;
using System.IO;
using FiftyOne.Foundation.Mobile.Detection.Factories;
using System.Collections.Generic;

namespace FiftyOne.Foundation.Mobile.Detection.Entities
{
    /// <summary>
    /// A node in the tree of characters for each character position.
    /// </summary>
    /// <para>
    /// Every character position in the string contains a tree of nodes
    /// which are evaluated until either a complete node is found, or 
    /// no nodes are found that match at the character positon.
    /// </para>
    /// <para>
    /// The list of <see cref="Signature"/> entities is in ascending order of 
    /// the complete nodes which form the sub strings of the signature.
    /// Complete nodes are found at detection time for the target user agent
    /// and then used to search for a corresponding signature. If one does
    /// not exist then Signatures associated with the nodes that were found 
    /// are evaluated to find one that is closest to the target user agent.
    /// </para>
    /// <para>
    /// Root nodes are the first node at a character position. It's children
    /// are based on sequences of characters that if present lead to the 
    /// next node. A complete node will represent a sub string within
    /// the user agent.
    /// </para>
    /// <para>
    /// For more information see https://51degrees.com/Support/Documentation/Net
    /// </para>
    internal abstract class Node : BaseEntity, IComparable<Node>
    {
        #region Classes

        /// <summary>
        /// Upper and lower limits for numeric comparison.
        /// </summary>
        private struct Range
        {
            internal short Lower;
            internal short Upper;
        }

        /// <summary>
        /// Used to order the results of a numeric comparision of nodes.
        /// </summary>
        private class NumericNodeComparer : IComparer<KeyValuePair<NodeNumericIndex, int>>
        {
            public int Compare(KeyValuePair<NodeNumericIndex, int> x, KeyValuePair<NodeNumericIndex, int> y)
            {
                // Compare the difference between this pair and the other.
                var difference = x.Value.CompareTo(y.Value);
                if (difference == 0)
                    // They're the same do look at the numeric value.
                    difference = x.Key.Value.CompareTo(y.Key.Value);
                return difference;
            }
        }

        #endregion

        #region Static Fields
        
        /// <summary>
        /// Ranges that both target and numeric index must both share
        /// for the numeric index to be used.
        /// </summary>
        private static readonly Range[] Ranges = new Range[] {
            new Range { Lower = 0, Upper = 10 },
            new Range { Lower = 10, Upper = 100 },
            new Range { Lower = 100, Upper = 1000 },
            new Range { Lower = 1000, Upper = 10000 },
            new Range { Lower = 10000, Upper = short.MaxValue } };

        /// <summary>
        /// Used to order the results of a numeric comparision of nodes.
        /// </summary>
        private static readonly NumericNodeComparer _numericNodeComparer = new NumericNodeComparer();

        /// <summary>
        /// Used to indicate an empty array of children.
        /// </summary>
        private static readonly Node[] Empty = new Node[0];

        /// <summary>
        /// Used when there is no numeric value to the node.
        /// </summary>
        private static readonly byte[] Zero = new byte[0];

        #endregion

        #region Fields

        /// <summary>
        /// Number of numeric children associated with the node.
        /// </summary>
        protected short _numericChildrenCount;

        /// <summary>
        /// Number of ranked signatures associated with the node.
        /// </summary>
        protected int _rankedSignatureCount;

        /// <summary>
        /// The next character position to the left of this node
        /// or a negative number if this is not a complete node.
        /// </summary>
        internal readonly short NextCharacterPosition;

        /// <summary>
        /// A list of all the child node indexes.
        /// </summary>
        internal protected readonly NodeIndex[] Children = null;

        /// <summary>
        /// The parent index for this node.
        /// </summary>
        private readonly int _parentOffset;
                
        /// <summary>
        /// The position of the first character the node represents
        /// in the signature or target user agent.
        /// </summary>
        internal readonly short Position;
        
        /// <summary>
        /// The offset in the strings data structure to the string
        /// that contains all the characters of the node. Or -1 if
        /// the node is not complete and no characters are available.
        /// </summary>
        internal readonly int CharacterStringOffset;

        #endregion

        #region Properties

        /// <summary>
        /// Returns the root node for this node.
        /// </summary>
        internal Node Root
        {
            get 
            {
                if (_root == null)
                {
                    lock (this)
                    {
                        if (_root == null)
                        {
                            _root = Parent == null ? this : Parent.Root;
                        }
                    }
                }
                return _root;
            }
        }
        private Node _root;

        /// <summary>
        /// Returns the parent node for this node.
        /// </summary>
        internal Node Parent
        {
            get
            {
                if (_parentOffset >= 0 &&
                    _parent == null)
                {
                    lock (this)
                    {
                        if (_parent == null)
                        {
                            _parent = DataSet.Nodes[_parentOffset];
                        }
                    }
                }
                return _parent;
            }
        }
        private Node _parent = null;

        /// <summary>
        /// Returns true if this node represents a completed sub string and 
        /// the next character position is set.
        /// </summary>
        internal bool IsComplete
        {
            get { return CharacterStringOffset >= 0; }
        }
        
        /// <summary>
        /// Returns the number of characters in the node tree.
        /// </summary>
        internal int Length
        {
            get
            {
                return Root.Position - Position;
            }
        }

        /// <summary>
        /// The characters that make up the node if it's a complete node
        /// or null if it's incomplete.
        /// </summary>
        internal byte[] Characters
        {
            get
            {
                if (IsComplete &&
                    _characters == null)
                {
                    lock (this)
                    {
                        if (_characters == null)
                        {
                            _characters = GetCharacters();
                        }
                    }
                }
                return _characters;
            }
        }
        private byte[] _characters;
        
        #endregion

        #region Abstract Properties

        /// <summary>
        /// An array of all the numeric children.
        /// </summary>
        internal protected abstract NodeNumericIndex[] NumericChildren { get; }

        /// <summary>
        /// A list of all the signature indexes that relate to this node.
        /// </summary>
        internal abstract int[] RankedSignatureIndexes { get; }

        #endregion

        #region Constructors

        /// <summary>
        /// Constructs a new instance of <see cref="Node"/>
        /// </summary>
        /// <param name="dataSet">
        /// The data set the node is contained within
        /// </param>
        /// <param name="offset">
        /// The offset in the data structure to the node
        /// </param>
        /// <param name="reader">
        /// Reader connected to the source data structure and positioned to start reading
        /// </param>
        internal Node(
            DataSet dataSet,
            int offset,
            BinaryReader reader) : base (dataSet, offset)
        {
            Position = reader.ReadInt16();
            NextCharacterPosition = reader.ReadInt16();
            _parentOffset = reader.ReadInt32();
            CharacterStringOffset = reader.ReadInt32();
            var childrenCount = reader.ReadInt16();
            _numericChildrenCount = reader.ReadInt16();
            _rankedSignatureCount = reader.ReadInt32();
            Children = ReadNodeIndexes(dataSet, reader, offset + NodeFactory.MinLength, childrenCount);
        }

        /// <summary>
        /// Used by the constructor to read the variable length list of child
        /// indexes that contain numeric values.
        /// </summary>
        /// <param name="dataSet">
        /// The data set the node is contained within
        /// </param>
        /// <param name="reader">
        /// Reader connected to the source data structure and positioned to start reading
        /// </param>
        /// <param name="count">
        /// The number of node indexes that need to be read.
        /// </param>
        /// <returns></returns>
        protected NodeNumericIndex[] ReadNodeNumericIndexes(Entities.DataSet dataSet, BinaryReader reader, short count)
        {
            var array = new NodeNumericIndex[count];
            for (int i = 0; i < array.Length; i++)
                array[i] = new NodeNumericIndex(dataSet, reader.ReadInt16(), reader.ReadInt32());
            return array;
        }

        /// <summary>
        /// Used by the constructor to read the variable length list of child
        /// node indexes associated with the node.
        /// </summary>
        /// <param name="dataSet">
        /// The data set the node is contained within
        /// </param>
        /// <param name="reader">
        /// Reader connected to the source data structure and positioned to start reading
        /// </param>
        /// <param name="offset">
        /// The offset in the data structure to the node
        /// </param>
        /// <param name="count">
        /// The number of node indexes that need to be read.
        /// </param>
        /// <returns>An array of child node indexes for the node</returns>
        private static NodeIndex[] ReadNodeIndexes(DataSet dataSet, BinaryReader reader, int offset, int count)
        {
            var array = new NodeIndex[count];
            offset += sizeof(short);
            for (int i = 0; i < array.Length; i++)
            {
                var isString = reader.ReadBoolean();
                array[i] = new NodeIndex(
                    dataSet,
                    offset,
                    isString,
                    ReadValue(reader, isString),
                    reader.ReadInt32());
                offset += NodeFactory.NodeIndexLength;
            }
            return array;
        }

        /// <summary>
        /// Reads the value and removes any zero characters if it's a string.
        /// </summary>
        /// <param name="reader"></param>
        /// <param name="isString">True if the value is a string in the strings list</param>
        /// <returns></returns>
        private static byte[] ReadValue(BinaryReader reader, bool isString)
        {
            var value = reader.ReadBytes(sizeof(int));
            if (isString == false)
            {
                int i;
                for (i = 0; i < value.Length; i++)
                    if (value[i] == 0) break;
                Array.Resize<byte>(ref value, i);
            }
            return value;
        }

        #endregion
        
        #region Methods

        /// <summary>
        /// Called after the entire data set has been loaded to ensure 
        /// any further initialisation steps that require other items in
        /// the data set can be completed.
        /// </summary>
        internal void Init()
        {
            if (IsComplete && _characters == null)
                _characters = GetCharacters();
            if (_parent == null && _parentOffset >= 0)
                _parent = DataSet.Nodes[_parentOffset];
            if (_root == null)
                _root = Parent == null ? this : Parent.Root;
            foreach (var child in Children)
                child.Init();
        }
        
        /// <summary>
        /// Returns the characters that represent the node.
        /// </summary>
        /// <returns></returns>
        private byte[] GetCharacters()
        {
            if (CharacterStringOffset >= 0)
                return DataSet.Strings[CharacterStringOffset].Value;
            return null;
        }
        
        /// <summary>
        /// Returns the node position as a number.
        /// </summary>
        /// <param name="match">Match results including the target user agent</param>
        /// <returns>
        /// -1 if there is no numeric characters, otherwise the characters 
        /// as an integer
        /// </returns>
        internal int GetCurrentPositionAsNumeric(Match match)
        {
            // Find the left most numeric character from the current position.
            int i = Position;
            while (i >= 0 &&
                match.TargetUserAgentArray[i] >= (byte)'0' &&
                match.TargetUserAgentArray[i] <= (byte)'9')
                i--;

            // If numeric characters were found then return the number.
            if (i < Position)
                return GetNumber(
                    match.TargetUserAgentArray,
                    i + 1,
                    Position - i);
            return -1;
        }
        
        /// <summary>
        /// Gets a complete node, or if one isn't available exactly the closest
        /// numeric one to the target user agent at the current position.
        /// </summary>
        /// <param name="match">Match results including the target user agent</param>
        /// <returns></returns>
        internal Node GetCompleteNumericNode(Match match)
        {
            Node node = null;

            // Check to see if there's a next node which matches
            // exactly.
            var nextNode = GetNextNode(match);
            if (nextNode != null)
                node = nextNode.GetCompleteNumericNode(match);
                
            if (node == null && NumericChildren.Length > 0)
            {
                // No. So try each of the numeric matches in ascending order of
                // difference.
                var target = GetCurrentPositionAsNumeric(match);
                if (target >= 0)
                {
                    var enumerator = GetNumericNodeEnumerator(match, target);
                    while (enumerator.MoveNext())
                    {
                        node = enumerator.Current.Node.GetCompleteNumericNode(match);
                        if (node != null)
                        {
                            var difference = Math.Abs(target - enumerator.Current.Value);
                            match.LowestScore += difference;
                            break;
                        }
                    }
                }
            }

            if (node == null && IsComplete)
                node = this;

            return node;
        }

        /// <summary>
        /// Returns a complete node for the match object provided.
        /// </summary>
        /// <param name="match">Match results including the target user agent</param>
        /// <returns>The next child node, or null if there isn't one</returns>
        internal Node GetCompleteNode(Match match)
        {
            Node node = null;
            var nextNode = GetNextNode(match);
            if (nextNode != null)
                node = nextNode.GetCompleteNode(match);
            if (node == null && IsComplete)
                node = this;
            return node;
        }

        /// <summary>
        /// Provides the next node, if any, from this node for the
        /// target user agent.
        /// </summary>
        /// <param name="match">Match results including the target user agent</param>
        /// <returns>Null if there is no next node, or the node if there is one</returns>
        private Node GetNextNode(Match match)
        {
            var upper = Children.Length - 1;

            if (upper >= 0)
            {
                var lower = 0;
                var middle = lower + (upper - lower) / 2;
                var length = Children[middle].Characters.Length;
                var startIndex = Position - length + 1;

                while (lower <= upper)
                {
                    middle = lower + (upper - lower) / 2;

                    // Increase the number of strings checked.
                    if (Children[middle].IsString)
                        match._stringsRead++;

                    // Increase the number of nodes checked.
                    match._nodesEvaluated++;

                    var comparisonResult = Children[middle].CompareTo(
                        match.TargetUserAgentArray,
                        startIndex);
                    if (comparisonResult == 0)
                        return Children[middle].Node;
                    else if (comparisonResult > 0)
                        upper = middle - 1;
                    else
                        lower = middle + 1;
                }
            }
            return null;
        }

        private bool IsNumeric(byte[] array, int startIndex, int length)
        {
            for (int i = startIndex; i < startIndex + length; i++)
                if (GetIsNumeric(array[i]) == false)
                    return false;
            return true;
        }

        /// <summary>
        /// Finds the range of lower and upper values that we
        /// can compare against.
        /// </summary>
        /// <param name="target">Numeric value of the sub string</param>
        /// <returns></returns>
        private Range GetRange(int target)
        {
            foreach (var range in Ranges)
            {
                if (target >= range.Lower && target < range.Upper)
                    return range;
            }
            return Ranges[Ranges.Length - 1];
        }

        /// <summary>
        /// Returns an enumerator for the numeric children of the node 
        /// compared to the target value.
        /// </summary>
        /// <param name="match">Match results including the target user agent</param>
        /// <param name="target">Numeric value of the sub string</param>
        /// <returns></returns>
        private IEnumerator<NodeNumericIndex> GetNumericNodeEnumerator(Match match, int target)
        {
            if (target >= 0 && target <= short.MaxValue)
            {
                // Get the range in which the comparision values need to fall.
                var range = GetRange(target);

                // Get the index in the ordered list to start at.
                var startIndex = BinarySearch(NumericChildren, target);
                if (startIndex < 0)
                    startIndex = ~startIndex - 1;
                int lowIndex = startIndex, highIndex = startIndex + 1;
                
                // Determine if the low and high indexes are in range.
                var lowInRange = lowIndex >= 0 && lowIndex < NumericChildren.Length &&
                    NumericChildren[lowIndex].Value >= range.Lower && 
                    NumericChildren[lowIndex].Value < range.Upper;
                var highInRange = highIndex < NumericChildren.Length && highIndex >= 0 &&
                    NumericChildren[highIndex].Value >= range.Lower &&
                    NumericChildren[highIndex].Value < range.Upper;

                while (lowInRange || highInRange)
                {
                    if (lowInRange && highInRange)
                    {
                        // Get the differences between the two values.
                        var lowDifference = Math.Abs(NumericChildren[lowIndex].Value - target);
                        var highDifference = Math.Abs(NumericChildren[highIndex].Value - target);

                        // Favour the lowest value where the differences are equal.
                        if (lowDifference <= highDifference)
                        {
                            yield return NumericChildren[lowIndex];

                            // Move to the next low index.
                            lowIndex--;
                            lowInRange = lowIndex >= 0 &&
                                NumericChildren[lowIndex].Value >= range.Lower &&
                                NumericChildren[lowIndex].Value < range.Upper;
                        }
                        else
                        {
                            yield return NumericChildren[highIndex];

                            // Move to the next high index.
                            highIndex++;
                            highInRange = highIndex < NumericChildren.Length &&
                                NumericChildren[highIndex].Value >= range.Lower &&
                                NumericChildren[highIndex].Value < range.Upper;
                        }
                    }
                    else if (lowInRange)
                    {
                        yield return NumericChildren[lowIndex];

                        // Move to the next low index.
                        lowIndex--;
                        lowInRange = lowIndex >= 0 &&
                            NumericChildren[lowIndex].Value >= range.Lower &&
                            NumericChildren[lowIndex].Value < range.Upper;
                    }
                    else
                    {
                        yield return NumericChildren[highIndex];

                        // Move to the next high index.
                        highIndex++;
                        highInRange = highIndex < NumericChildren.Length &&
                            NumericChildren[highIndex].Value >= range.Lower &&
                            NumericChildren[highIndex].Value < range.Upper;
                    }
                }
            }
        }

        /// <summary>
        /// Adds the characters for this node to the values array.
        /// </summary>
        /// <param name="values"></param>
        internal void AddCharacters(byte[] values)
        {
            var characters = Characters == null ? GetCharacters() : Characters;
            for (int i = 0; i < Length; i++)
                values[Position + i + 1] = characters[i];
        }

        /// <summary>
        /// Returns true if the node overlaps with this one.
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        private bool GetIsOverlap(Node node)
        {
            var lower = node.Position < Position ? node : this;
            var higher = lower == this ? node : this;
            return lower.Position == higher.Position ||
                   lower.Root.Position > higher.Position;
        }

        /// <summary>
        /// Returns true if any of the nodes in the match have overlapping characters
        /// with this one.
        /// </summary>
        /// <param name="match"></param>
        /// <returns></returns>
        internal bool GetIsOverlap(Match match)
        {
            return match.Nodes.Any(i => GetIsOverlap(i));
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Returns a string of spaces with the characters relating to this
        /// node populated.
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            var values = new byte[DataSet.MaxUserAgentLength];
            AddCharacters(values);
            for (int i = 0; i < values.Length; i++)
                if (values[i] == 0)
                    values[i] = (byte)' ';
            return Encoding.ASCII.GetString(values);
        }

        /// <summary>
        /// Compares one node to another for the purposes of determining
        /// the signature the node relates to.
        /// </summary>
        /// <param name="other"></param>
        /// <returns></returns>
        public int CompareTo(Node other)
        {
            return Index.CompareTo(other.Index);
        }

        #endregion
    }
}
