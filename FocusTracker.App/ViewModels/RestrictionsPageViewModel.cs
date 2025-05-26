using FocusTracker.App.Helpers;
using FocusTracker.Domain.Interfaces;
using FocusTracker.Domain.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;

namespace FocusTracker.App.ViewModels
{
    public class RuleTypeOption
    {
        public string Type { get; set; }
        public string DisplayName { get; set; }
    }

    public class RestrictionsPageViewModel : INotifyPropertyChanged
    {
        private readonly IRestrictionService _restrictionService;
        private readonly ITrackedProgramService _programService;
        private readonly INotificationService _notificationService;


        public string FromHour { get; set; } = "00";
        public string FromMinute { get; set; } = "00";
        public string ToHour { get; set; } = "23";
        public string ToMinute { get; set; } = "59";

        public ObservableCollection<Restriction> Restrictions { get; } = new();
        public ObservableCollection<TrackedProgram> AvailablePrograms { get; } = new();
        public ObservableCollection<TrackedProgram> SelectedPrograms { get; } = new();
        public ObservableCollection<RuleTypeOption> AvailableRuleTypes { get; } = new()
        {
            new RuleTypeOption { Type = "max_total_seconds", DisplayName = "Загальний час використання (хв)" },
            new RuleTypeOption { Type = "time_interval", DisplayName = "Інтервал часу" },
            new RuleTypeOption { Type = "after_task", DisplayName = "Після виконання завдань (хв)" }
        };

        private string _newNote;
        public string NewNote
        {
            get => _newNote;
            set { _newNote = value; OnPropertyChanged(); }
        }

        private RuleTypeOption _selectedRuleType;
        public RuleTypeOption SelectedRuleType
        {
            get => _selectedRuleType;
            set { _selectedRuleType = value; OnPropertyChanged(); }
        }

        private string _ruleValue;
        public string RuleValue
        {
            get => _ruleValue;
            set { _ruleValue = value; OnPropertyChanged(); }
        }

        public ICommand DeleteRestrictionCommand { get; }

        public bool IsEditing { get; set; }
        private Restriction _editingRestriction;
        public string AddButtonText => IsEditing ? "Оновити" : "Додати";

        public RestrictionsPageViewModel(
            IRestrictionService restrictionService,
            ITrackedProgramService programService,
            INotificationService notificationService)
        {
            _restrictionService = restrictionService;
            _programService = programService;
            _notificationService = notificationService;


            DeleteRestrictionCommand = new RelayCommand(async obj => await DeleteRestrictionAsync(obj as Restriction));
            _ = LoadAsync();
        }

        private async Task LoadAsync()
        {
            Restrictions.Clear();
            AvailablePrograms.Clear();

            FromHour = "00";
            FromMinute = "00";
            ToHour = "23";
            ToMinute = "59";

            var restrictions = await _restrictionService.GetAllAsync();

            foreach (var r in restrictions)
            {
                foreach (var rule in r.Rules)
                    rule.DisplayName = GetDisplayName(rule.RuleType);
                Restrictions.Add(r);
            }

            var programs = _programService.GetAll()
                .Where(p => !p.IsHidden && p.Identifier != "FocusTracker.App");

            foreach (var p in programs)
                AvailablePrograms.Add(p);

        }

        private string GetDisplayName(string ruleType) => ruleType switch
        {
            "max_total_seconds" => "Загальний час використання (хв)",
            "time_interval" => "Інтервал часу",
            "after_task" => "Після виконання завдань (хв)",
            _ => ruleType
        };

        public void StartEditing(Restriction restriction)
        {
            if (restriction == null) return;

            IsEditing = true;
            _editingRestriction = restriction;

            NewNote = restriction.Note;
            SelectedPrograms.Clear();
            foreach (var target in restriction.Targets)
                if (target.Program != null)
                    SelectedPrograms.Add(target.Program);

            var rule = restriction.Rules.FirstOrDefault();
            if (rule != null)
            {
                SelectedRuleType = AvailableRuleTypes.FirstOrDefault(r => r.Type == rule.RuleType);

                if (rule.RuleType == "time_interval" && rule.Value.Contains('–'))
                {
                    var parts = rule.Value.Split('–');
                    var fromParts = parts[0].Split(':');
                    var toParts = parts[1].Split(':');

                    FromHour = fromParts.ElementAtOrDefault(0) ?? "00";
                    FromMinute = fromParts.ElementAtOrDefault(1) ?? "00";
                    ToHour = toParts.ElementAtOrDefault(0) ?? "23";
                    ToMinute = toParts.ElementAtOrDefault(1) ?? "59";
                }
                else
                {
                    RuleValue = rule.Value;
                }
            }

            OnPropertyChanged(nameof(NewNote));
            OnPropertyChanged(nameof(SelectedPrograms));
            OnPropertyChanged(nameof(SelectedRuleType));
            OnPropertyChanged(nameof(RuleValue));
            OnPropertyChanged(nameof(FromHour));
            OnPropertyChanged(nameof(FromMinute));
            OnPropertyChanged(nameof(ToHour));
            OnPropertyChanged(nameof(ToMinute));
            OnPropertyChanged(nameof(AddButtonText));
        }

        public async Task AddRestrictionAsync()
        {
            if (string.IsNullOrWhiteSpace(NewNote) ||
                SelectedPrograms.Count == 0 ||
                SelectedRuleType == null ||
                (SelectedRuleType.Type == "time_interval" &&
                 (string.IsNullOrWhiteSpace(FromHour) ||
                  string.IsNullOrWhiteSpace(FromMinute) ||
                  string.IsNullOrWhiteSpace(ToHour) ||
                  string.IsNullOrWhiteSpace(ToMinute))) ||
                (SelectedRuleType.Type != "time_interval" && string.IsNullOrWhiteSpace(RuleValue)))
                return;

            var ruleValue = SelectedRuleType.Type == "time_interval"
                ? $"{FromHour}:{FromMinute}–{ToHour}:{ToMinute}"
                : RuleValue;

            if (IsEditing && _editingRestriction != null)
            {
                _editingRestriction.Note = NewNote;
                _editingRestriction.Rules.Clear();
                _editingRestriction.Rules.Add(new RestrictionRule
                {
                    RuleType = SelectedRuleType.Type,
                    Value = ruleValue,
                    DisplayName = SelectedRuleType.DisplayName
                });

                _editingRestriction.Targets.Clear();
                foreach (var program in SelectedPrograms)
                    _editingRestriction.Targets.Add(new RestrictionToProgram { ProgramId = program.Id });

                await _restrictionService.UpdateAsync(_editingRestriction);
            }
            else
            {
                var restriction = new Restriction
                {
                    Note = NewNote,
                    Rules = new List<RestrictionRule>
                    {
                        new RestrictionRule
                        {
                            RuleType = SelectedRuleType.Type,
                            Value = ruleValue,
                            DisplayName = SelectedRuleType.DisplayName
                        }
                    },
                    Targets = SelectedPrograms
                        .Select(p => new RestrictionToProgram { ProgramId = p.Id })
                        .ToList()
                };

                await _restrictionService.CreateAsync(restriction);
            }

            await LoadAsync();
            ResetForm();

        }

        public void ResetForm()
        {
            NewNote = string.Empty;
            RuleValue = string.Empty;
            SelectedRuleType = null;
            SelectedPrograms.Clear();
            FromHour = "00";
            FromMinute = "00";
            ToHour = "23";
            ToMinute = "59";
            IsEditing = false;
            _editingRestriction = null;

            OnPropertyChanged(nameof(NewNote));
            OnPropertyChanged(nameof(RuleValue));
            OnPropertyChanged(nameof(SelectedRuleType));
            OnPropertyChanged(nameof(FromHour));
            OnPropertyChanged(nameof(FromMinute));
            OnPropertyChanged(nameof(ToHour));
            OnPropertyChanged(nameof(ToMinute));
            OnPropertyChanged(nameof(AddButtonText));
        }

        private async Task DeleteRestrictionAsync(Restriction restriction)
        {
            if (restriction == null) return;
            await _restrictionService.DeleteAsync(restriction.Id);
            await LoadAsync();
        }
        private string GetTimeIntervalDescription(string value)
        {
            try
            {
                var parts = value.Split('–');
                var toParts = parts[1].Split(':');
                var toTime = new TimeSpan(int.Parse(toParts[0]), int.Parse(toParts[1]), 0);
                var now = DateTime.Now.TimeOfDay;

                if (now > toTime)
                    return "Обмеження завершено";
                var left = toTime - now;
                return $"Залишилось {Math.Floor(left.TotalMinutes)} хв";
            }
            catch
            {
                return null;
            }
        }


        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string prop = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }
}
