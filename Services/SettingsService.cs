using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dairy_Management_System.Models.Settings;
using Dairy_Management_System.Repositories;

namespace Dairy_Management_System.Services
{
    public class SettingsService : ISettingsService
    {
        private readonly ISettingsRepository _settingsRepository;

        public SettingsService(ISettingsRepository settingsRepository)
        {
            _settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
        }

        public async Task<SystemSettingsViewModel> GetSettingsAsync(CancellationToken ct = default)
        {
            var dict = await _settingsRepository.GetAllAsync(ct);

            return new SystemSettingsViewModel
            {
                DairyName = dict.TryGetValue("DairyName", out var dn) ? dn : string.Empty,
                ContactPhone = dict.TryGetValue("ContactPhone", out var cp) ? cp : string.Empty,
                ContactEmail = dict.TryGetValue("ContactEmail", out var ce) ? ce : string.Empty,
                OperatingAddress = dict.TryGetValue("OperatingAddress", out var oa) ? oa : string.Empty,
                DefaultCurrency = dict.TryGetValue("DefaultCurrency", out var dc) ? dc : string.Empty,
                MorningShiftCutoff = dict.TryGetValue("MorningShiftCutoff", out var mc) ? mc : string.Empty,
                EveningShiftCutoff = dict.TryGetValue("EveningShiftCutoff", out var ec) ? ec : string.Empty,
                SystemAnnouncement = dict.TryGetValue("SystemAnnouncement", out var sa) ? sa : string.Empty
            };
        }

        public async Task<bool> UpdateSettingsAsync(SystemSettingsViewModel model, int adminUserId, CancellationToken ct = default)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));

            var settingsToUpdate = new Dictionary<string, string?>
            {
                { "DairyName", model.DairyName?.Trim() ?? string.Empty },
                { "ContactPhone", model.ContactPhone?.Trim() ?? string.Empty },
                { "ContactEmail", model.ContactEmail?.Trim() ?? string.Empty },
                { "OperatingAddress", model.OperatingAddress?.Trim() ?? string.Empty },
                { "DefaultCurrency", model.DefaultCurrency?.Trim() ?? string.Empty },
                { "MorningShiftCutoff", model.MorningShiftCutoff?.Trim() ?? string.Empty },
                { "EveningShiftCutoff", model.EveningShiftCutoff?.Trim() ?? string.Empty },
                { "SystemAnnouncement", model.SystemAnnouncement?.Trim() ?? string.Empty }
            };

            foreach (var kvp in settingsToUpdate)
            {
                await _settingsRepository.UpdateAsync(kvp.Key, kvp.Value ?? string.Empty, adminUserId, ct);
            }

            return true;
        }

        public async Task<string?> GetSettingValueAsync(string key, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            var dict = await _settingsRepository.GetAllAsync(ct);
            return dict.TryGetValue(key, out var val) ? val : null;
        }
    }
}
