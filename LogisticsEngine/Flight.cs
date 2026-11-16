using ReteCore;

namespace LogisticsEngine
{
    public class Flight : Fact
    {
        public string FlightNumber { get; set; } = string.Empty;

        private string _status = "EnRoute";
        public string Status
        {
            get => _status;
            set => SetField(ref _status, value);
        }

        private string _assignedGateId = string.Empty;
        public string AssignedGateId
        {
            get => _assignedGateId;
            set => SetField(ref _assignedGateId, value);
        }

        private int _minutesDelayed = 0;
        public int MinutesDelayed
        {
            get => _minutesDelayed;
            set => SetField(ref _minutesDelayed, value);
        }
    }
}
