using System;
using System.ComponentModel.DataAnnotations;

namespace Dairy_Management_System.Models.Settings
{
    public class AppSettingItemModel
    {
        public string SettingKey { get; set; } = string.Empty;
        public string SettingValue { get; set; } = string.Empty;
        public string DataType { get; set; } = "String";
        public string? Description { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class SystemSettingsViewModel
    {
        [Display(Name = "Dairy Enterprise Name")]
        [MaxLength(100)]
        public string DairyName { get; set; } = string.Empty;

        [Display(Name = "Contact Phone")]
        [MaxLength(20)]
        public string ContactPhone { get; set; } = string.Empty;

        [Display(Name = "Contact Email")]
        [EmailAddress]
        [MaxLength(100)]
        public string ContactEmail { get; set; } = string.Empty;

        [Display(Name = "Operating Address")]
        [MaxLength(250)]
        public string OperatingAddress { get; set; } = string.Empty;

        [Display(Name = "Default Currency")]
        [MaxLength(20)]
        public string DefaultCurrency { get; set; } = string.Empty;

        [Display(Name = "Morning Shift Cutoff")]
        [MaxLength(20)]
        public string MorningShiftCutoff { get; set; } = string.Empty;

        [Display(Name = "Evening Shift Cutoff")]
        [MaxLength(20)]
        public string EveningShiftCutoff { get; set; } = string.Empty;

        [Display(Name = "System Announcement Banner")]
        [MaxLength(500)]
        public string SystemAnnouncement { get; set; } = string.Empty;
    }
}
