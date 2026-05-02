//-----------------------------------------------------------------------
// <copyright file="Token.cs">
//     Copyright (c) Steven Orton. All rights reserved.
//     Licensed under the GNU Lesser General Public License v2.1.
//     See LICENSE file in the ReteRaven project root for full license
//     information.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace ReteCore
{
    /// <summary>
    /// Represents a chain of named facts used to track the state and ancestry of rule evaluation in a rule engine.
    /// </summary>
    /// <remarks>A Token encapsulates a set of named facts and maintains a reference to its parent token,
    /// forming a linked structure that represents the progression of matched facts in a rule evaluation process. Tokens
    /// are typically used to accumulate and access facts as rules are matched and extended. Instances are considered
    /// equal if their facts and parent tokens are equal. This class is not thread-safe.</remarks>
    public class Token : IEquatable<Token>
    {
        /// <summary>
        /// The current fact associated with this token. This is the most recently added fact in the 
        /// chain of facts represented by this token.
        /// </summary>
        private object _fact;
        /// <summary>
        /// Gets a collection of named facts associated with the current instance.
        /// </summary>
        /// <remarks>The dictionary maps fact names to their corresponding values. The collection is
        /// read-only; to add or modify facts, use the provided methods or constructors of the containing class, if
        /// available.</remarks>
        public Dictionary<string, object> NamedFacts { get; } = new Dictionary<string, object>();

        /// <summary>
        /// Initializes a new instance of the Token class with the specified name and initial fact.
        /// </summary>
        /// <param name="name">The name to associate with the initial fact. Cannot be null.</param>
        /// <param name="initialFact">The initial fact to store in the token. Can be any object.</param>
        public Token(string name, object initialFact)
        {
            Parent = null;
            _fact = initialFact;
            NamedFacts[name] = initialFact;
        }

        /// <summary>
        /// Initializes a new instance of the Token class by extending the specified parent token with an additional
        /// named fact.
        /// </summary>
        /// <remarks>This constructor creates a new Token that inherits all named facts from the parent
        /// token and adds a new fact under the specified name. The resulting Token will contain all facts from the
        /// parent, plus the new fact.</remarks>
        /// <param name="parent">The parent Token whose named facts are to be copied and extended. Cannot be null.</param>
        /// <param name="nextName">The name to associate with the new fact being added. Cannot be null or empty.</param>
        /// <param name="newFact">The fact object to associate with the specified name. Can be any object.</param>
        public Token(Token parent, string nextName, object newFact)
        {
            Parent = parent;
            _fact = newFact;
            foreach (var facts in parent.NamedFacts)
            {
                NamedFacts[facts.Key] = facts.Value;
            }
            NamedFacts[nextName] = newFact;
        }

        /// <summary>
        /// Gets or sets the parent token of this token.
        /// </summary>
        public Token Parent { get; set; }
        /// <summary>
        /// Gets the fact associated with this instance.
        /// </summary>
        public object Fact { get { return _fact; } }

        /// <summary>
        /// Retrieves a fact by name and type from the collection.
        /// </summary>
        /// <remarks>If the fact exists but is not of type T, this method treats it as not found and
        /// throws a KeyNotFoundException.</remarks>
        /// <typeparam name="T">The expected type of the fact to retrieve.</typeparam>
        /// <param name="name">The name of the fact to retrieve. Cannot be null.</param>
        /// <returns>The fact associated with the specified name, cast to type T.</returns>
        /// <exception cref="KeyNotFoundException">Thrown if a fact with the specified name and type T does not exist in the collection.</exception>
        public T Get<T>(string name)
        {
            if (NamedFacts.TryGetValue(name, out var fact) && fact is T typedFact)
            {
                return typedFact;
            }
            throw new KeyNotFoundException($"Fact named '{name}' of type {typeof(T).Name} was not found.");
        }

        public override string ToString()
        {
            var factDescriptions = NamedFacts.Select(kv => $"{kv.Key}:{kv.Value}");
            return $"Token({string.Join(", ", factDescriptions)})";
        }

        #region IEquatable overrides
        public bool Equals(Token? other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Equals(this, other) && Equals(Parent, other.Parent);
        }

        public override bool Equals(object? obj)
        {
            return base.Equals(obj);
        }
        #endregion

        public override int GetHashCode()
        {
            int parentHash = Parent?.GetHashCode() ?? 0;
            int factHash = _fact?.GetHashCode() ?? 0;
            //Console.WriteLine($"Calculating hash for Token: ParentHash={parentHash}, FactHash={factHash}");
            return HashCode.Combine(parentHash, factHash);
        }
        public static bool operator ==(Token? left, Token? right) => Equals(left, right);
        public static bool operator !=(Token? left, Token? right) => !Equals(left, right);

    }

    /// <summary>
    /// Represents a data cell with an identifier and a value, supporting property change notification.
    /// </summary>
    /// <remarks>The Cell class implements INotifyPropertyChanged to support data binding scenarios, such as
    /// those found in UI frameworks. PropertyChanged is raised when the Value property changes, allowing observers to
    /// react to updates. Equality and hash code operations are based on both the Id and Value properties.</remarks>
    public class Cell : INotifyPropertyChanged
    {
        private int _value;
        public String Id { get; set; }
        public int Value 
        {
            get { return _value; }
            set
            {
                if (_value != value)
                {
                    _value = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
                }
            }
        }
        /// <summary>
        /// When a property value changes, this method is called to raise the PropertyChanged event. The CallerMemberName attribute allows the caller to omit the property name when calling this method, as it will automatically use the name of the calling property. This simplifies the code and reduces the likelihood of errors when raising property change notifications. Observers can subscribe to the PropertyChanged event to be notified when a property value changes, enabling features like data binding in UI frameworks.
        /// </summary>
        /// <param name="propertyName"></param>
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// The PropertyChanged event is raised whenever a property value changes. Observers can subscribe to this event to be notified of changes to the properties of this class, allowing for responsive updates in scenarios such as data binding in user interfaces. The event handler receives the name of the property that changed, enabling observers to react specifically to changes in certain properties if needed.
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        public override bool Equals(object? obj)
        {
            return obj is Cell other && Id == other.Id && Value == other.Value;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Id, Value);
        }

        public override string ToString() => $"[ID:{Id}, Val:{Value}]";
    }

}
