namespace Dairy_Management_System.Models.Settings
{
    /// <summary>
    /// Delivery Boy field application preferences for mobile runsheets and route workflows.
    /// </summary>
    public class DeliveryBoyPreferencesViewModel
    {
        public string PreferredLanguage { get; set; } = "en"; // "en" or "gu"
        public string DefaultShiftView { get; set; } = "Morning"; // "Morning" or "Evening"
        public bool EnableSoundAlerts { get; set; } = true;
        public bool HighContrastRunsheet { get; set; } = false;
        public bool AutoAdvanceNextCustomer { get; set; } = true;
    }
}
