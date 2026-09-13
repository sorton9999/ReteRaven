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
    /// Token identity is stable (GUID-based) so it can be used safely as dictionary/set keys even if underlying facts mutate.
    /// </summary>
    public class Token : IEquatable<Token>
    {
        /// <summary>
        /// A unique identifier for this token instaance.
        /// </summary>
        private readonly Guid _id = Guid.NewGuid(); // stable identity
        /// <summary>
        /// The name associated with this token.
        /// </summary>
        private string _name;
        /// <summary>
        /// The current fact associated with this token. This is the most recently added fact in the 
        /// chain of facts represented by this token.
        /// </summary>
        private object _fact;
        /// <summary>
        /// Gets a collection of named facts associated with the current instance.
        /// </summary>
        public Dictionary<string, object> NamedFacts { get; } = new Dictionary<string, object>();

        /// <summary>
        /// Initializes a new instance of the Token class with the specified name and initial fact.
        /// </summary>
        /// <param name="name">The name to associate with the initial fact. Cannot be null.</param>
        /// <param name="initialFact">The initial fact to store in the token. Can be any object.</param>
        public Token(string name, object initialFact)
        {
            _name = name;
            Parent = null;
            _fact = initialFact;
            NamedFacts[name] = initialFact;
        }

        /// <summary>
        /// Initializes a new instance of the Token class by extending the specified parent token with an additional
        /// named fact.
        /// </summary>
        /// <param name="parent">The parent Token whose named facts are to be copied and extended. Cannot be null.</param>
        /// <param name="nextName">The name to associate with the new fact being added. Cannot be null or empty.</param>
        /// <param name="newFact">The fact object to associate with the specified name. Can be any object.</param>
        public Token(Token parent, string nextName, object newFact)
        {
            Parent = parent;
            _fact = newFact;
            _name = nextName;
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
        /// Retrieves a fact by name and type from the collection, searching through the token chain if not found locally.
        /// This method first attempts to find the fact in the current token's NamedFacts dictionary. If not found,
        /// it walks up the parent chain to search for the fact in ancestor tokens. This is especially useful when
        /// working with reused beta memories where tokens may have been constructed through different paths.
        /// </summary>
        /// <param name="name">The name of the fact to retrieve. Cannot be null.</param>
        /// <returns>The fact associated with the specified name, cast to type T.</returns>
        /// <exception cref="KeyNotFoundException">Thrown if a fact with the specified name and type T does not exist 
        /// in the current token or any ancestor in the token chain.</exception>
        public T Get<T>(string name)
        {
            // Try lookup by explicit name if provided
            if (name != null && NamedFacts.TryGetValue(name, out var fact) && fact is T typedFact)
            {
                return typedFact;
            }

            // Walk up the parent chain: try named lookup first, then type-based fallback
            Token? current = Parent;
            while (current != null)
            {
                if (name != null && current.NamedFacts.TryGetValue(name, out var parentFact) && parentFact is T parentTypedFact)
                {
                    return parentTypedFact;
                }
                
                current = current.Parent;
            }

            throw new KeyNotFoundException($"Fact named '{name}' of type {typeof(T).Name} was not found in the token or its parent chain.");
        }

        /// <summary>
        /// The overridden string output method
        /// </summary>
        /// <returns>The string representation of the object</returns>
        public override string ToString()
        {
            var factDescriptions = NamedFacts.Select(kv => $"{kv.Key}:{kv.Value}");
            return $"Token({_id} : {string.Join(", ", factDescriptions)})";
        }

        #region IEquatable overrides
        public bool Equals(Token? other)
        {
            if (other == null) { return false; }
            if (!ReferenceEquals(Fact, other.Fact) || _name != other._name)
            {
                return false;
            }

            if (Parent == null && other.Parent == null) { return true; }
            if (Parent != null && other.Parent != null) { return Parent.Equals(other.Parent); }

            return false;
        }

        public override bool Equals(object? obj)
        {
            return obj is Token token && Equals(token);
        }
        #endregion

        public override int GetHashCode()
        {
            int parentHash = Parent?.GetHashCode() ?? 0;
            int factHash = Fact != null ? EqualityComparer<object>.Default.GetHashCode(Fact) : 0;
            return HashCode.Combine(parentHash, _name, factHash);
        }

        public static bool operator ==(Token? left, Token? right) => Equals(left, right);
        public static bool operator !=(Token? left, Token? right) => !Equals(left, right);

    }


    /// <summary>
    /// Represents a data cell with an identifier and a value, supporting property change notification.
    /// </summary>
    /// <remarks>The Fact class implements INotifyPropertyChanged to support data binding scenarios, such as
    /// those found in UI frameworks. PropertyChanged is raised when the Value property changes, allowing observers to
    /// react to updates. Equality and hash code operations are based on both the Id and Value properties.</remarks>
    public class Fact : INotifyPropertyChanged
    {
        /// <summary>
        /// The value stored in this cell. When the value is set, if it differs from the current value, the PropertyChanged 
        /// event is raised to notify observers of the change. 
        /// </summary>
        private object? _value;
        /// <summary>
        /// The unique identifier for this cell. The Id is typically assigned when the cell is created and should remain 
        /// constant for the lifetime of the cell to ensure consistent behavior.
        /// </summary>
        public Guid Id { get; set; }
        /// <summary>
        /// A identifying name for this cell, which can be used for display purposes or to associate the cell with a specific 
        /// role or meaning in the context of its use.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The accessor for the value stored in this cell. When setting the Value, if the new value is different from the 
        /// current value, the PropertyChanged event is raised to notify any observers that the value has changed. The Value 
        /// property can hold any object.
        /// </summary>
        public object? Value
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
        /// When a property value changes, this method is called to raise the PropertyChanged event. The CallerMemberName 
        /// attribute allows the caller to omit the property name when calling this method, as it will automatically use the 
        /// name of the calling property. Observers can subscribe to the PropertyChanged event to be notified when a property 
        /// value changes.
        /// </summary>
        /// <param name="propertyName"></param>
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// The PropertyChanged event is raised whenever a property value changes. Observers can subscribe to this event to be 
        /// notified of changes to the properties of this class. The event handler receives the name of the property that 
        /// changed.
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// The Equals method is overridden to provide a way to compare two Fact instances for equality.
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public override bool Equals(object? obj)
        {
            return obj is Fact other && Id == other.Id && Value == other.Value;
        }

        /// <summary>
        /// The GetHashCode method is overridden to provide a hash code that is consistent with the Equals method.
        /// </summary>
        /// <returns></returns>
        public override int GetHashCode()
        {
            return HashCode.Combine(Id, Value);
        }

        /// <summary>
        /// The ToString method is overridden to provide a string representation of the Fact instance, which includes the 
        /// Id and Value for easy identification when debugging or logging.
        /// </summary>
        /// <returns></returns>
        public override string ToString() => $"[ID:{Id}, Val:{Value}]";
    }


}
