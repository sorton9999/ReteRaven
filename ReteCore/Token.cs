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

    /// <summary>
    /// An interface representing a fact in the Rete network. This interface allows for abstraction and 
    /// flexibility in handling facts of various types within the Rete engine, enabling the implementation 
    /// of different fact representations while maintaining a consistent interface for interaction with 
    /// the rest of the system.
    /// </summary>
    public interface IFact
    {
        Guid Id { get; }
        object UnderlyingObject { get; }
        Type DataType { get; }
    }

    /// <summary>
    /// The container for any types of data. This class implements INotifyPropertyChanged to allow observers to 
    /// be notified when the underlying fact changes. It also includes a unique identifier (Id) to ensure that 
    /// each Fact instance can be uniquely identified.
    /// </summary>
    public class Fact<T> : IFact, INotifyPropertyChanged
    {
        /// <summary>
        /// The actual data item stored in this fact. It can be of any type, as specified by the generic 
        /// parameter T. 
        /// </summary>
        private object _fact;

        /// <summary>
        /// A unique identifier
        /// </summary>
        public Guid Id { get; } = Guid.NewGuid();

        /// <summary>
        /// The templated value of this fact.
        /// </summary>
        public T Value 
        { 
            get => (T)_fact; 
            set {
                if (!Equals(_fact, value))
                {
                    _fact = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
                }
            }
        }

        /// <summary>
        /// The data stored as an object type.
        /// </summary>
        public object UnderlyingObject => Value;
        /// <summary>
        /// The type of the data stored in this fact. This property returns the Type object representing 
        /// the generic type parameter T, which indicates the actual type of the data contained in this 
        /// fact.
        /// </summary>
        public Type DataType => typeof(T);

        /// <summary>
        /// The constructor initializes a new instance of the Fact class with the specified data. It assigns
        /// the provided data to the Value property, which in turn sets the underlying fact and raises the 
        /// PropertyChanged event if necessary.
        /// </summary>
        /// <param name="data"></param>
        public Fact(T data)
        {
            Value = data;
        }

        /// <summary>
        /// A helper method that retrieves the underlying fact value as a specific type T.
        /// </summary>
        /// <typeparam name="T">The type of this object</typeparam>
        /// <returns>The templated type</returns>
        public T TValue<T>() => (T)UnderlyingObject;

        /// <summary>
        /// The PropertyChanged event is raised whenever a property value changes. Observers can subscribe to this event to
        /// be notified of changes to the properties of this class. The event handler receives the name of the property that 
        /// changed, enabling observers to react.
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// The Equals method is overridden to provide a way to compare two Fact instances for equality. 
        /// Two Fact instances are considered equal if they have the same unique identifier (Id). This
        /// means that even if two Fact instances contain the same underlying data, they will be treated as
        /// distinct unless they share the same Id.
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public override bool Equals(object? obj)
        {
            if (obj is Fact<T> other) return this.Id == other.Id;
            return false;
        }

        /// <summary>
        /// Returns a hash code for this instance, which is based on the unique identifier (Id) of the fact.
        /// </summary>
        /// <returns>The unique hash code</returns>
        public override int GetHashCode() => Id.GetHashCode();

        /// <summary>
        /// The override of the ToString method provides a string representation of the Fact instance.
        /// </summary>
        /// <returns>The string representation of the contents.</returns>
        public override string ToString() => $"Fact[{Id.ToString().Substring(0, 4)}]: {UnderlyingObject}";

        /// <summary>
        /// When a property value changes, this method is called to raise the PropertyChanged event. The CallerMemberName 
        /// attribute allows the caller to omit the property name when calling this method, as it will automatically use 
        /// the name of the calling property.
        /// </summary>
        /// <param name="propertyName"></param>
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

    }


}
