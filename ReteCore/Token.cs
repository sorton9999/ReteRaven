using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace ReteCore
{
    public class Token : IEquatable<Token>
    {
        // Stores facts by their assigned names
        public Dictionary<string, object> NamedFacts { get; } = new Dictionary<string, object>();

        // Initial token for the start of a rule
        public Token(string name, object initialFact)
        {
            Parent = null;
            NamedFacts[name] = initialFact;
        }

        // Creates a new token by extending a parent with a new named fact
        public Token(Token parent, string nextName, object newFact)
        {
            Parent = parent;
            foreach (var facts in parent.NamedFacts)
            {
                NamedFacts[facts.Key] = facts.Value;
            }
            NamedFacts[nextName] = newFact;
        }

        // Store its parent token
        public Token Parent { get; set; }

        // Type-safe accessor by name
        public T Get<T>(string name)
        {
            if (NamedFacts.TryGetValue(name, out var fact) && fact is T typedFact)
            {
                return typedFact;
            }
            throw new KeyNotFoundException($"Fact named '{name}' of type {typeof(T).Name} was not found.");
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
        public static bool operator ==(Token? left, Token? right) => Equals(left, right);
        public static bool operator !=(Token? left, Token? right) => !Equals(left, right);

    }

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
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public event PropertyChangedEventHandler PropertyChanged;
        public override string ToString() => $"[ID:{Id}, Val:{Value}]";
    }

}
