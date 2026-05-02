using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace ReteCore
{
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
            set
            {
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
