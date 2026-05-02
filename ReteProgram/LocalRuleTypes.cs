using ReteCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReteProgram
{
    // Test classes
    public class SystemStatus
    {
        public string Name { get; set; }
        public bool IsActive { get; set; }
    };

    public class Sensor
    {
        public string Name { get; set; }
        public Guid Id { get; set; }
        public string Type { get; set; }
        public bool IsTriggered { get; set; }
    };

    public class CriticalCell : Cell
    {
        string _status = String.Empty;
        public string Status
        {
            get { return _status; }
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged(nameof(Status));
                }
            }
        }

        public override bool Equals(object? obj)
        {
            return obj is CriticalCell cell && Id == cell.Id && Value == cell.Value && Status == cell.Status;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Id, Value, Status);
        }
    }

    public class Product : Cell
    {
        private string _name;
        private string _category;
        private int _productId;
        private int _price;

        public string Name
        {
            get { return _name; }
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged(nameof(Name));
                }
            }
        }
        public string Category
        {
            get { return _category; }
            set
            {
                if (_category != value)
                {
                    _category = value;
                    OnPropertyChanged(nameof(Category));
                }
            }
        }

        public int ProductId
        {
            get { return _productId; }
            set
            {
                if (_productId != value)
                {
                    _productId = value;
                    OnPropertyChanged(nameof(ProductId));
                }
            }
        }

        public int Price
        {
            get { return _price; }
            set
            {
                if (_price != value)
                {
                    _price = value;
                    OnPropertyChanged(nameof(Price));
                }
            }
        }

        public override bool Equals(object? obj)
        {
            return obj is Product product && Id == product.Id && ProductId == product.ProductId && Price == product.Price && Value == product.Value && Name == product.Name && Category == product.Category;
        }
        public override int GetHashCode()
        {
            return HashCode.Combine(Id, Value, Name, Category, Price, ProductId);
        }
    }

    public class Inventory : Cell
    {
        private int _quantity;
        private int _productId;
        private string _location;
        private int _count;
        public int Quantity
        {
            get { return _quantity; }
            set
            {
                if (_quantity != value)
                {
                    _quantity = value;
                    OnPropertyChanged(nameof(Quantity));
                }
            }
        }
        public int ProductId
        {
            get { return _productId; }
            set
            {
                if (_productId != value)
                {
                    _productId = value;
                    OnPropertyChanged(nameof(ProductId));
                }
            }
        }
        public string WarehouseLocation
        {
            get { return _location; }
            set
            {
                if (_location != value)
                {
                    _location = value;
                    OnPropertyChanged(nameof(WarehouseLocation));
                }
            }
        }

        public int Count
        {
            get { return _count; }
            set
            {
                if (_count != value)
                {
                    _count = value;
                    OnPropertyChanged(nameof(Count));
                }
            }
        }

        public override bool Equals(object? obj)
        {
            return obj is Inventory inventory && Id == inventory.Id && Value == inventory.Value && ProductId == inventory.ProductId && Quantity == inventory.Quantity && WarehouseLocation == inventory.WarehouseLocation && Count == inventory.Count;
        }
        public override int GetHashCode()
        {
            return HashCode.Combine(Id, Value, Quantity, ProductId, WarehouseLocation, Count);
        }
    }

    public class Shipment : Cell
    {
        private int _productId;
        private string _status;
        public int ProductId
        {
            get { return _productId; }
            set
            {
                if (_productId != value)
                {
                    _productId = value;
                    OnPropertyChanged(nameof(ProductId));
                }
            }
        }
        public string Status
        {
            get { return _status; }
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged(nameof(Status));
                }
            }
        }
        public override bool Equals(object? obj)
        {
            return obj is Shipment shipment && Id == shipment.Id && Value == shipment.Value && ProductId == shipment.ProductId && Status == shipment.Status;
        }
        public override int GetHashCode()
        {
            return HashCode.Combine(Id, Value, ProductId, Status);
        }
    }

    public class Order : Cell
    {
        private string _text;
        private string _targetRank;
        private string _givenBy;
        private bool _isProcessed;

        public string Text
        {
            get { return _text; }
            set
            {
                if (_text != value)
                {
                    _text = value;
                    OnPropertyChanged(nameof(Text));
                }
            }
        }
        public string TargetRank
        {
            get { return _targetRank; }
            set
            {
                if (_targetRank != value)
                {
                    _targetRank = value;
                    OnPropertyChanged(nameof(TargetRank));
                }
            }
        }
        public string GivenBy
        {
            get { return _givenBy; }
            set
            {
                if (_givenBy != value)
                {
                    _givenBy = value;
                    OnPropertyChanged(nameof(GivenBy));
                }
            }
        }
        public bool IsProcessed
        {
            get { return _isProcessed; }
            set
            {
                if (_isProcessed != value)
                {
                    _isProcessed = value;
                    OnPropertyChanged(nameof(IsProcessed));
                }
            }
        }
        public override bool Equals(object? obj)
        {
            return obj is Order order && Id == order.Id && Value == order.Value && Text == order.Text && TargetRank == order.TargetRank && GivenBy == order.GivenBy && IsProcessed == order.IsProcessed;
        }
        public override int GetHashCode()
        {
            return HashCode.Combine(Id, Value, Text, TargetRank, GivenBy, IsProcessed);
        }
    }

    public class Officer : Cell
    {
        private string _name;
        private string _rank;
        private string _reportsToRank;
        private string _underling;
        public string Name
        {
            get { return _name; }
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged(nameof(Name));
                }
            }
        }
        public string Rank
        {
            get { return _rank; }
            set
            {
                if (_rank != value)
                {
                    _rank = value;
                    OnPropertyChanged(nameof(Rank));
                }
            }
        }
        public string ReportsToRank
        {
            get
            {
                if (Rank == "Lieutenant") return "Captain";
                if (Rank == "Captain") return "Major";
                if (Rank == "Major") return "Colonel";
                if (Rank == "Colonel") return "General";
                return null;
            }
            set
            {
                if (_reportsToRank != value)
                {
                    _reportsToRank = value;
                    OnPropertyChanged(nameof(ReportsToRank));
                }
            }
        }

        public string Underling
        {
            get
            {
                if (Rank == "General") return "Colonel";
                if (Rank == "Colonel") return "Major";
                if (Rank == "Major") return "Captain";
                if (Rank == "Captain") return "Lieutenant";
                return null;
            }
            set
            {
                if (_underling != value)
                {
                    _underling = value;
                    OnPropertyChanged(nameof(Underling));
                }
            }
        }

        public override bool Equals(object? obj)
        {
            return obj is Officer officer && Id == officer.Id && Value == officer.Value && Name == officer.Name && Rank == officer.Rank && ReportsToRank == officer.ReportsToRank && Underling == officer.Underling;
        }
        public override int GetHashCode()
        {
            return HashCode.Combine(Id, Value, Name, Rank, ReportsToRank, Underling);
        }
    }

    public class DutyStatus : Cell
    {
        private string _name;
        private bool _onDuty;
        public string Name
        {
            get { return _name; }
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged(nameof(Name));
                }
            }
        }
        public bool OnDuty
        {
            get { return _onDuty; }
            set
            {
                if (_onDuty != value)
                {
                    _onDuty = value;
                    OnPropertyChanged(nameof(OnDuty));
                }
            }
        }
        public override bool Equals(object? obj)
        {
            return obj is DutyStatus dutyStatus && Id == dutyStatus.Id && Value == dutyStatus.Value && OnDuty == dutyStatus.OnDuty;
        }
        public override int GetHashCode()
        {
            return HashCode.Combine(Id, Value, OnDuty);
        }
    }

}
